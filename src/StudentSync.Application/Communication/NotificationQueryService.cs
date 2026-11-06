using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Communication;

namespace StudentSync.Application.Communication;

public sealed class NotificationQueryService(IAppDbContext db, ICurrentUser user, IClock clock, IEmailSender email, IPushSender push)
{
    public async Task<PagedResult<NotificationDto>> ListAsync(PageQuery q, bool unreadOnly, CancellationToken ct)
    {
        var items = db.Notifications.Where(n => n.UserId == user.UserId);
        if (unreadOnly) items = items.Where(n => n.ReadAtUtc == null);
        var total = await items.CountAsync(ct);
        var page = await items.OrderByDescending(n => n.CreatedAtUtc).Skip((q.SafePage - 1) * q.SafePageSize).Take(q.SafePageSize)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Link, n.CreatedAtUtc, n.ReadAtUtc != null)).ToListAsync(ct);
        return new PagedResult<NotificationDto>(page, q.SafePage, q.SafePageSize, total);
    }

    public async Task<UnreadCountsDto> UnreadAsync(CancellationToken ct)
    {
        var me = user.UserId;
        var notifications = await db.Notifications.CountAsync(n => n.UserId == me && n.ReadAtUtc == null, ct);
        var messages = await db.Messages.CountAsync(m => m.SenderUserId != me && db.ConversationParticipants.Any(p => p.ConversationId == m.ConversationId && p.UserId == me
            && (p.LastReadAtUtc == null || m.CreatedAtUtc > p.LastReadAtUtc)), ct);
        return new UnreadCountsDto(notifications, messages);
    }

    public async Task MarkReadAsync(Guid? id, CancellationToken ct)
    {
        var q = db.Notifications.Where(n => n.UserId == user.UserId && n.ReadAtUtc == null);
        if (id is { } nid) q = q.Where(n => n.Id == nid);
        await q.ExecuteUpdateAsync(u => u.SetProperty(n => n.ReadAtUtc, clock.UtcNow), ct);
    }

    public async Task<NotificationPreferencesDto> PreferencesAsync(CancellationToken ct)
    {
        var p = await db.NotificationPreferences.FirstOrDefaultAsync(x => x.UserId == user.UserId, ct) ?? new NotificationPreference();
        return new NotificationPreferencesDto(p.EmailEnabled, p.PushEnabled, p.QuietHoursStart, p.QuietHoursEnd, ParseMuted(p.MutedTypes), email.IsConfigured, push.IsConfigured);
    }

    public async Task SavePreferencesAsync(UpdateNotificationPreferencesRequest r, CancellationToken ct)
    {
        if ((r.QuietHoursStart is null) != (r.QuietHoursEnd is null)) throw RequestValidationException.For("quietHoursStart", "Set both the start and end of quiet hours.");
        var p = await db.NotificationPreferences.FirstOrDefaultAsync(x => x.UserId == user.UserId, ct);
        if (p is null) db.NotificationPreferences.Add(p = new NotificationPreference { UserId = user.UserId });
        p.EmailEnabled = r.EmailEnabled; p.PushEnabled = r.PushEnabled; p.QuietHoursStart = r.QuietHoursStart; p.QuietHoursEnd = r.QuietHoursEnd;
        p.MutedTypes = string.Join(',', r.MutedTypes.Distinct());
        await db.SaveChangesAsync(ct);
    }

    public async Task RegisterDeviceAsync(RegisterDeviceRequest r, CancellationToken ct)
    {
        var token = Text.Required(r.Token, "token", 500);
        var platform = Text.Required(r.Platform, "platform", 20).ToLowerInvariant();
        if (platform is not ("android" or "ios" or "web")) throw RequestValidationException.For("platform", "Platform must be android, ios or web.");
        var d = await db.Devices.FirstOrDefaultAsync(x => x.Token == token, ct);
        if (d is null) db.Devices.Add(d = new DeviceRegistration { Token = token, RegisteredAtUtc = clock.UtcNow });
        d.UserId = user.UserId; d.Platform = platform; d.LastSeenAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task UnregisterDeviceAsync(string token, CancellationToken ct)
        => await db.Devices.Where(d => d.Token == token && d.UserId == user.UserId).ExecuteDeleteAsync(ct);

    public static List<NotificationType> ParseMuted(string? muted) =>
        (muted ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => Enum.TryParse<NotificationType>(x, out var t) ? (NotificationType?)t : null)
            .Where(x => x is not null).Select(x => x!.Value).ToList();
}
