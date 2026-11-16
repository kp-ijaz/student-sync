using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Automation;
using StudentSync.Application.Common;
using StudentSync.Application.Fees;
using StudentSync.Contracts;
using StudentSync.Domain.Assignments;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.People;

/// <summary>
/// Monthly progress reports (owner request 2026-11-17). On the 1st a draft is prepared for every student who had classes
/// last month, from real records only, and given to the teacher who taught them most. The teacher (or an admin) adds a
/// remark and sends it; only then does the student see it (and the guardian get an SMS, if switched on). Teachers see only
/// their own reports.
/// </summary>
public sealed class ProgressReportService(IAppDbContext db, ITenantContext tenant, ICurrentUser user, AccessPolicy access, IClock clock, AuditService audit,
    NotificationService notify, SmsOutbox sms, AutomationSettingsService settings, FeeService fees)
{
    /// <summary>Job: drafts for <paramref name="month"/> (first day). Existing reports are left alone.</summary>
    public async Task<int> PrepareAsync(DateOnly month, CancellationToken ct)
    {
        month = new DateOnly(month.Year, month.Month, 1);
        var next = month.AddMonths(1);
        var tz = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.DefaultTimeZoneId).FirstAsync(ct);
        var startUtc = ZonedTime.ToUtc(month, TimeOnly.MinValue, tz).Utc;
        var endUtc = ZonedTime.ToUtc(next, TimeOnly.MinValue, tz).Utc;

        var rows = await (from p in db.SessionParticipants
                          join s in db.Sessions on p.SessionId equals s.Id
                          where s.StartUtc >= startUtc && s.StartUtc < endUtc && p.Student!.Status == PersonStatus.Active
                          let mark = db.Attendance.Where(a => a.SessionId == s.Id && a.StudentId == p.StudentId).Select(a => (AttendanceStatus?)a.Status).FirstOrDefault()
                          where p.Status == ParticipantStatus.Active || mark != null
                          select new { p.StudentId, s.TeacherId, s.StartUtc, s.EndUtc, s.Status, s.CancelledBecause, Mark = mark }).ToListAsync(ct);
        if (rows.Count == 0) return 0;
        var existing = await db.ProgressReports.Where(r => r.Month == month).Select(r => r.StudentId).ToListAsync(ct);
        var graded = await db.Submissions.Where(x => x.GradedAtUtc >= startUtc && x.GradedAtUtc < endUtc && x.Marks != null && x.Assignment!.MaxMarks > 0)
            .Select(x => new { x.StudentId, Percent = x.Marks!.Value * 100 / x.Assignment!.MaxMarks }).ToListAsync(ct);
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz));
        var (balances, _) = await fees.BalancesAsync(today, ct);

        var created = new List<ProgressReport>();
        foreach (var g in rows.GroupBy(r => r.StudentId).Where(g => !existing.Contains(g.Key)))
        {
            var live = g.Where(r => r.Status != SessionStatus.Cancelled).ToList();
            var attended = live.Where(r => r.Mark is AttendanceStatus.Present or AttendanceStatus.Late).ToList();
            var missed = live.Count(r => r.Mark == AttendanceStatus.Absent) + g.Count(r => r.Status == SessionStatus.Cancelled && r.CancelledBecause == CancellationKind.StudentAbsent);
            var teacher = (attended.Count > 0 ? attended : live.Count > 0 ? live : g.ToList())
                .GroupBy(r => r.TeacherId).OrderByDescending(t => t.Sum(r => FeeService.HoursOf(r.StartUtc, r.EndUtc))).First().Key;
            var marks = graded.Where(x => x.StudentId == g.Key).ToList();
            var report = new ProgressReport
            {
                TenantId = tenant.TenantId!.Value,
                StudentId = g.Key,
                TeacherId = teacher,
                Month = month,
                ClassesAttended = attended.Count,
                ClassesMissed = missed,
                HoursAttended = attended.Sum(r => FeeService.HoursOf(r.StartUtc, r.EndUtc)),
                HoursLeft = balances.FirstOrDefault(b => b.StudentId == g.Key)?.Left,
                AssignmentsGraded = marks.Count,
                AverageMarkPercent = marks.Count == 0 ? null : decimal.Round(marks.Average(m => m.Percent), 1),
                Status = ProgressReportStatus.Draft,
                CreatedAtUtc = clock.UtcNow,
            };
            db.ProgressReports.Add(report);
            created.Add(report);
        }
        foreach (var t in created.GroupBy(r => r.TeacherId))
            await notify.NotifyAsync([t.Key], NotificationType.ProgressReportReady, $"{t.Count()} progress report{(t.Count() == 1 ? "" : "s")} to check",
                $"{Fmt.Month(month)} is ready for your students. Add a short remark and send.", "/progress-reports", $"progress-ready:{t.Key}:{Fmt.MonthKey(month)}", ct);
        await db.SaveChangesAsync(ct);
        return created.Count;
    }

    public async Task<List<ProgressReportDto>> ListAsync(string? status, CancellationToken ct)
    {
        if (!access.IsAdmin && !access.IsTeacher) throw new ForbiddenException();
        var q = db.ProgressReports.AsQueryable();
        if (!access.IsAdmin) q = q.Where(r => r.TeacherId == user.UserId);
        if (Enum.TryParse<ProgressReportStatus>(status, true, out var st)) q = q.Where(r => r.Status == st);
        return await q.OrderByDescending(r => r.Month).ThenBy(r => r.Status).Take(200)
            .Select(r => new ProgressReportDto(r.Id, r.StudentId, db.Users.Where(u => u.Id == r.StudentId).Select(u => u.FullName).FirstOrDefault() ?? "Student",
                db.Users.Where(u => u.Id == r.TeacherId).Select(u => u.FullName).FirstOrDefault() ?? "Teacher", r.Month,
                r.ClassesAttended, r.ClassesMissed, r.HoursAttended, r.HoursLeft, r.AssignmentsGraded, r.AverageMarkPercent, r.Remark, r.Status.ToString(), r.SentAtUtc))
            .ToListAsync(ct);
    }

    public async Task SendAsync(Guid id, SendProgressReportRequest r, CancellationToken ct)
    {
        var report = await OwnAsync(id, ct);
        if (report.Status == ProgressReportStatus.Sent) throw new ConflictException("This report was already sent.", "already_sent");
        report.Remark = Text.Optional(r.Remark, "remark", 1000);
        (report.Status, report.SentByUserId, report.SentAtUtc) = (ProgressReportStatus.Sent, user.UserId, clock.UtcNow);
        var name = await db.Users.Where(u => u.Id == report.StudentId).Select(u => u.FullName).FirstAsync(ct);
        var teacher = await db.Users.Where(u => u.Id == report.TeacherId).Select(u => u.FullName).FirstAsync(ct);
        await notify.NotifyAsync([report.StudentId], NotificationType.ProgressReport, $"Your progress report for {Fmt.Month(report.Month)}",
            Summary(report, teacher), "/student", $"progress:{report.Id}", ct);
        if ((await settings.CurrentAsync(ct)).ProgressReportSms)
        {
            var remark = report.Remark is null ? "" : $" Teacher: {(report.Remark.Length > 120 ? report.Remark[..119] + "…" : report.Remark)}";
            await sms.QueueFamilyAsync(report.StudentId, "progress-report",
                $"{Fmt.FirstName(name)}'s {report.Month.ToString("MMMM", CultureInfo.InvariantCulture)} report: attended {report.ClassesAttended} of {report.ClassesAttended + report.ClassesMissed} classes ({report.HoursAttended.ToString("0.##", CultureInfo.InvariantCulture)} h).{remark}",
                $"sms-progress:{report.Id}", ct);
        }
        audit.Record("progress_report.sent", "Student", report.StudentId, $"{Fmt.MonthKey(report.Month)}");
        await db.SaveChangesAsync(ct);
    }

    public async Task SkipAsync(Guid id, CancellationToken ct)
    {
        var report = await OwnAsync(id, ct);
        if (report.Status == ProgressReportStatus.Sent) throw new ConflictException("This report was already sent.", "already_sent");
        report.Status = ProgressReportStatus.Skipped;
        audit.Record("progress_report.skipped", "Student", report.StudentId, Fmt.MonthKey(report.Month));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The text the student receives.</summary>
    public static string Summary(ProgressReport r, string teacherName)
    {
        var lines = new List<string>
        {
            $"Classes attended: {r.ClassesAttended} of {r.ClassesAttended + r.ClassesMissed} ({Fmt.Hours(r.HoursAttended)}).",
        };
        if (r.AssignmentsGraded > 0) lines.Add($"Assignments marked: {r.AssignmentsGraded}{(r.AverageMarkPercent is { } a ? $", average {a.ToString("0.#", CultureInfo.InvariantCulture)}%" : "")}.");
        if (r.HoursLeft is { } left) lines.Add($"Learning hours left: {Fmt.Hours(Math.Max(0, left))}.");
        if (!string.IsNullOrWhiteSpace(r.Remark)) lines.Add($"{teacherName}: “{r.Remark}”");
        return string.Join("\n", lines);
    }

    private async Task<ProgressReport> OwnAsync(Guid id, CancellationToken ct)
    {
        var report = await db.ProgressReports.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Progress report");
        if (!access.IsAdmin && !(access.IsTeacher && report.TeacherId == user.UserId)) throw new NotFoundException("Progress report");
        return report;
    }
}
