using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StudentSync.Application.Common;
using StudentSync.Application.Platform;
using StudentSync.Domain.Platform;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Automation;

/// <summary>
/// Automated SMS to a student or their guardian. <see cref="QueueAsync"/> adds an <see cref="OutboundMessage"/> to the
/// caller's unit of work (saved with the event that caused it); <see cref="SendDueAsync"/> (job, every minute) sends what
/// is due through the institute's SMS connection. Messages are never sent twice (dedupe key), never between 21:00 and
/// 08:00 in the centre's time, never to demo accounts, and skipped — visibly, in the log — when there is no usable number
/// or SMS is not connected.
/// </summary>
public sealed class SmsOutbox(IAppDbContext db, ITenantContext tenant, IClock clock, IntegrationSettingsService integrations, IIntegrationClients clients,
    ILogger<SmsOutbox> logger)
{
    public const int MaxLength = 480;
    private static readonly TimeOnly QuietFrom = new(21, 0), QuietUntil = new(8, 0);

    /// <summary>Queues one SMS; returns false when it was already queued or sent under this key.</summary>
    public async Task<bool> QueueAsync(Guid studentId, OutboundRecipient to, string kind, string text, string dedupeKey, CancellationToken ct)
    {
        var key = dedupeKey.Length > 200 ? dedupeKey[..200] : dedupeKey;
        if (db.OutboundMessages.Local.Any(m => m.DedupeKey == key) || await db.OutboundMessages.AnyAsync(m => m.DedupeKey == key, ct)) return false;

        var centre = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => new { t.Name, t.CountryCode, t.DefaultTimeZoneId }).FirstAsync(ct);
        var phone = await PhoneAsync(studentId, to, centre.CountryCode, ct);
        var body = $"{centre.Name}: {text}";
        var message = new OutboundMessage
        {
            TenantId = tenant.TenantId!.Value,
            Channel = OutboundChannel.Sms,
            Kind = kind,
            StudentId = studentId,
            Recipient = to,
            PhoneMasked = phone is null ? "—" : PhoneNumbers.Mask(phone),
            Text = body.Length <= MaxLength ? body : body[..(MaxLength - 1)] + "…",
            DedupeKey = key,
            Status = OutboundStatus.Pending,
            NotBeforeUtc = NextAllowedUtc(clock.UtcNow, centre.DefaultTimeZoneId),
            CreatedAtUtc = clock.UtcNow,
        };
        if (phone is null) (message.Status, message.Error) = (OutboundStatus.Skipped, to == OutboundRecipient.Guardian ? "No valid guardian phone number" : "No valid student phone number");
        db.OutboundMessages.Add(message);
        return true;
    }

    /// <summary>
    /// Queues to the family: the guardian when a guardian number is on file, otherwise the student
    /// (<paramref name="preferStudent"/> reverses that, e.g. for "class today" reminders).
    /// </summary>
    public async Task<bool> QueueFamilyAsync(Guid studentId, string kind, string text, string dedupeKey, CancellationToken ct, bool preferStudent = false)
    {
        var p = await db.Students.Where(x => x.Id == studentId).Select(x => new { x.Phone, x.GuardianPhone }).FirstOrDefaultAsync(ct);
        if (p is null) return false;
        var country = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.CountryCode).FirstAsync(ct);
        var student = PhoneNumbers.ToE164(p.Phone, country) is not null;
        var guardian = PhoneNumbers.ToE164(p.GuardianPhone, country) is not null;
        var to = preferStudent ? (student || !guardian ? OutboundRecipient.Student : OutboundRecipient.Guardian)
                               : (guardian || !student ? OutboundRecipient.Guardian : OutboundRecipient.Student);
        return await QueueAsync(studentId, to, kind, text, dedupeKey, ct);
    }

    /// <summary>
    /// Sends due messages for the current tenant (job). Each message is first claimed with one conditional UPDATE (still
    /// Pending and due → pushed 10 minutes ahead); a run that loses the claim skips it, so overlapping runs never send the
    /// same SMS twice. Results are written the same way, one row at a time, so a crash never re-sends an accepted SMS.
    /// </summary>
    public async Task<int> SendDueAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await db.OutboundMessages.AsNoTracking().Where(m => m.Status == OutboundStatus.Pending && m.NotBeforeUtc <= now)
            .OrderBy(m => m.NotBeforeUtc).Take(50).ToListAsync(ct);
        if (due.Count == 0) return 0;

        var settings = await integrations.ActiveSmsAsync(ct);
        var country = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.CountryCode).FirstAsync(ct);
        var ids = due.Select(m => m.StudentId).ToList();
        var demo = await db.Students.Where(s => ids.Contains(s.Id) && s.User!.IsDemo).Select(s => s.Id).ToListAsync(ct);
        var sent = 0;
        foreach (var m in due)
        {
            var claimed = await db.OutboundMessages.Where(x => x.Id == m.Id && x.Status == OutboundStatus.Pending && x.NotBeforeUtc <= now)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.NotBeforeUtc, now.AddMinutes(10)).SetProperty(x => x.Attempts, x => x.Attempts + 1), ct);
            if (claimed == 0) continue; // another run has it

            string? skip = settings is null ? "SMS is not switched on (Settings ▸ Integrations)"
                : demo.Contains(m.StudentId) ? "Demo account"
                : m.Kind == "absence" && !await StillAbsentAsync(m.DedupeKey, m.StudentId, ct) ? "Attendance was corrected before sending"
                : null;
            var phone = skip is null ? await PhoneAsync(m.StudentId, m.Recipient, country, ct) : null;
            if (skip is null && phone is null) skip = "No valid phone number";
            if (skip is not null) { await FinishAsync(m.Id, OutboundStatus.Skipped, null, skip, ct); continue; }

            var result = await clients.SendSmsAsync(settings!, phone!, m.Text, ct);
            sent++;
            if (result.Accepted) await FinishAsync(m.Id, OutboundStatus.Sent, result.ProviderMessageId, null, ct);
            else if (m.Attempts + 1 >= 3) await FinishAsync(m.Id, OutboundStatus.Failed, null, Short(result.Error), ct);
            else
            {
                var error = Short(result.Error);
                var retryAt = now.AddMinutes(5 * (m.Attempts + 1));
                await db.OutboundMessages.Where(x => x.Id == m.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.Error, error).SetProperty(x => x.NotBeforeUtc, retryAt), ct);
                logger.LogWarning("SMS {Id} attempt {Attempt} failed", m.Id, m.Attempts + 1);
            }
        }
        return sent;
    }

    private Task FinishAsync(Guid id, OutboundStatus status, string? providerId, string? error, CancellationToken ct)
    {
        var provider = providerId is { Length: > 120 } ? providerId[..120] : providerId;
        DateTime? sentAt = status == OutboundStatus.Sent ? clock.UtcNow : null;
        return db.OutboundMessages.Where(x => x.Id == id).ExecuteUpdateAsync(u => u
            .SetProperty(x => x.Status, status).SetProperty(x => x.ProviderMessageId, provider)
            .SetProperty(x => x.Error, error).SetProperty(x => x.SentAtUtc, sentAt), ct);
    }

    /// <summary>"sms-absent:{session}:{student}": the student is still marked absent for that class.</summary>
    private async Task<bool> StillAbsentAsync(string dedupeKey, Guid studentId, CancellationToken ct)
    {
        var parts = dedupeKey.Split(':');
        if (parts.Length < 3 || !Guid.TryParse(parts[1], out var sessionId)) return true;
        return await db.Attendance.AnyAsync(a => a.SessionId == sessionId && a.StudentId == studentId && a.Status == AttendanceStatus.Absent, ct);
    }

    private async Task<string?> PhoneAsync(Guid studentId, OutboundRecipient to, string? country, CancellationToken ct)
    {
        var s = await db.Students.Where(x => x.Id == studentId).Select(x => new { x.Phone, x.GuardianPhone }).FirstOrDefaultAsync(ct);
        return s is null ? null : PhoneNumbers.ToE164(to == OutboundRecipient.Guardian ? s.GuardianPhone : s.Phone, country);
    }

    /// <summary>Now, or 08:00 (centre time) when now is inside quiet hours.</summary>
    public static DateTime NextAllowedUtc(DateTime utcNow, string timeZoneId)
    {
        var local = ZonedTime.ToLocal(utcNow, timeZoneId);
        var t = TimeOnly.FromDateTime(local);
        if (t >= QuietUntil && t < QuietFrom) return utcNow;
        var day = DateOnly.FromDateTime(local);
        if (t >= QuietFrom) day = day.AddDays(1);
        return ZonedTime.ToUtc(day, QuietUntil, timeZoneId).Utc;
    }

    private static string? Short(string? e) => e is { Length: > 500 } ? e[..500] : e;
}
