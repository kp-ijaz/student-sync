using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Scheduling;

/// <summary>
/// Holiday clean-up (owner request 2026-11-17): after a holiday is added, the admin sees the classes booked that day and
/// can cancel them all (reason "Holiday", which counts as nobody's missed hours) or move each one to the same time on the
/// next day that is not a holiday and has no clash. Uses the normal cancel / reschedule rules, so students and teachers
/// are told and the class history keeps the change. A class that cannot be moved is listed, not forced.
/// </summary>
public sealed class HolidayClassesService(IAppDbContext db, ITenantContext tenant, AccessPolicy access, SchedulingService scheduling)
{
    public async Task<HolidayClassesDto> ClassesOnAsync(DateOnly date, CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        var (from, to) = await DayAsync(date, ct);
        var rows = await db.Sessions.Where(s => s.Status == SessionStatus.Scheduled && s.StartUtc >= from && s.StartUtc < to).OrderBy(s => s.StartUtc)
            .Select(s => new HolidayClassDto(s.Id, s.Title, s.StartUtc, s.EndUtc,
                db.Users.Where(u => u.Id == s.TeacherId).Select(u => u.FullName).FirstOrDefault() ?? "Teacher",
                s.Participants.Count(p => p.Status == ParticipantStatus.Active)))
            .ToListAsync(ct);
        return new HolidayClassesDto(date, rows);
    }

    public async Task<HolidayClassesResult> ApplyAsync(HolidayClassesRequest r, CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        if (r.Action is not ("cancel" or "move")) throw RequestValidationException.For("action", "Choose cancel or move.");
        var holiday = await db.Holidays.Where(h => h.Date == r.Date).Select(h => h.Name).FirstOrDefaultAsync(ct)
                      ?? throw RequestValidationException.For("date", "Add the holiday first.");
        var classes = (await ClassesOnAsync(r.Date, ct)).Classes;
        var holidays = (await db.Holidays.Where(h => h.Date > r.Date && h.Date <= r.Date.AddDays(30)).Select(h => h.Date).ToListAsync(ct)).ToHashSet();
        var done = 0;
        var skipped = new List<string>();
        foreach (var c in classes)
        {
            if (r.Action == "cancel")
            {
                await scheduling.CancelAsync(c.SessionId, new CancelSessionRequest($"Holiday: {holiday}", false, CancellationKind.Holiday), ct);
                done++;
                continue;
            }
            var s = await db.Sessions.AsNoTracking().Where(x => x.Id == c.SessionId).Select(x => new { x.TimeZoneId, x.StartUtc, x.EndUtc, x.RowVersion }).FirstAsync(ct);
            var localStart = ZonedTime.ToLocal(s.StartUtc, s.TimeZoneId);
            var minutes = (int)(s.EndUtc - s.StartUtc).TotalMinutes;
            var moved = false;
            for (var day = r.Date.AddDays(1); day <= r.Date.AddDays(7) && !moved; day = day.AddDays(1))
            {
                if (holidays.Contains(day)) continue;
                try
                {
                    var version = await db.Sessions.Where(x => x.Id == c.SessionId).Select(x => x.RowVersion).FirstAsync(ct);
                    await scheduling.RescheduleAsync(c.SessionId, new RescheduleSessionRequest(s.TimeZoneId, day, TimeOnly.FromDateTime(localStart), minutes,
                        $"Holiday: {holiday}", RowVersions.Encode(version)), ct);
                    moved = true;
                    done++;
                }
                catch (ConflictException) { /* clash that day: try the next */ }
            }
            if (!moved) skipped.Add($"{c.Title} ({ZonedTime.ToLocal(c.StartUtc, s.TimeZoneId):HH:mm}): no free day in the next week — reschedule it by hand.");
        }
        return new HolidayClassesResult(done, skipped);
    }

    private async Task<(DateTime From, DateTime To)> DayAsync(DateOnly date, CancellationToken ct)
    {
        var tz = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.DefaultTimeZoneId).FirstAsync(ct);
        return (ZonedTime.ToUtc(date, TimeOnly.MinValue, tz).Utc, ZonedTime.ToUtc(date.AddDays(1), TimeOnly.MinValue, tz).Utc);
    }
}
