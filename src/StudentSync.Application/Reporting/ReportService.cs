using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Assignments;
using StudentSync.Domain.Doubts;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Reporting;

public sealed class ReportService(IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, DashboardService dashboards)
{
    public async Task<InstitutionReportDto> InstitutionAsync(ReportQuery q, CancellationToken ct)
    {
        access.Require(Permissions.ReportsView);
        var tz = await db.Tenants.Where(t => t.Id == user.TenantId).Select(t => t.DefaultTimeZoneId).FirstAsync(ct);
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz));
        var from = q.From ?? today.AddDays(-30);
        var to = q.To ?? today;
        if (to < from) throw RequestValidationException.For("to", "The end date must be after the start date.");
        if (to.DayNumber - from.DayNumber > 366) throw RequestValidationException.For("to", "Reports cover at most one year.");
        var fromUtc = ZonedTime.ToUtc(from, TimeOnly.MinValue, tz).Utc;
        var toUtc = ZonedTime.ToUtc(to.AddDays(1), TimeOnly.MinValue, tz).Utc;

        var sessions = db.Sessions.Where(s => s.StartUtc >= fromUtc && s.StartUtc < toUtc);
        if (q.TeacherId is { } t) sessions = sessions.Where(s => s.TeacherId == t);
        if (q.BatchId is { } b) sessions = sessions.Where(s => s.BatchId == b);
        if (q.SessionType is { } st) sessions = sessions.Where(s => s.SessionType == st);

        var byType = await dashboards.SessionTypeCountsAsync(sessions.Where(s => s.Status != SessionStatus.Cancelled), ct);
        var attendance = await dashboards.AttendanceSummaryAsync(sessions, ct);

        var teacherRows = await db.Teachers.Select(x => new { x.Id, x.User!.FullName }).OrderBy(x => x.FullName).ToListAsync(ct);
        var teachers = new List<TeacherReportRowDto>();
        foreach (var tr in teacherRows.Where(x => q.TeacherId is null || x.Id == q.TeacherId))
        {
            var ts = sessions.Where(s => s.TeacherId == tr.Id);
            var delivered = await ts.CountAsync(s => s.Status == SessionStatus.Completed || (s.Status != SessionStatus.Cancelled && s.EndUtc < clock.UtcNow), ct);
            var cancelled = await ts.CountAsync(s => s.Status == SessionStatus.Cancelled, ct);
            var att = await dashboards.AttendanceSummaryAsync(ts, ct);
            var resolved = await db.Doubts.Where(d => d.AssignedTeacherId == tr.Id && d.ResolvedAtUtc >= fromUtc && d.ResolvedAtUtc < toUtc)
                .Select(d => new { d.CreatedAtUtc, d.ResolvedAtUtc }).ToListAsync(ct);
            var gradedCount = await db.Submissions.CountAsync(s => s.GradedByUserId == tr.Id && s.GradedAtUtc >= fromUtc && s.GradedAtUtc < toUtc, ct);
            teachers.Add(new TeacherReportRowDto(tr.Id, tr.FullName, delivered, cancelled, att.AttendanceRatePercent, resolved.Count,
                resolved.Count == 0 ? null : Math.Round(resolved.Average(r => (r.ResolvedAtUtc!.Value - r.CreatedAtUtc).TotalHours), 1), gradedCount));
        }

        var batchRows = await db.Batches.Where(x => x.Status != CatalogStatus.Archived && (q.BatchId == null || x.Id == q.BatchId))
            .Select(x => new { x.Id, x.Name, Students = x.Enrolments.Count(e => e.Status == EnrolmentStatus.Active) }).ToListAsync(ct);
        var batches = new List<BatchReportRowDto>();
        foreach (var br in batchRows)
        {
            var bs = sessions.Where(s => s.BatchId == br.Id && s.Status != SessionStatus.Cancelled);
            batches.Add(new BatchReportRowDto(br.Id, br.Name, br.Students, await bs.CountAsync(ct), (await dashboards.AttendanceSummaryAsync(bs, ct)).AttendanceRatePercent));
        }

        var raised = await db.Doubts.CountAsync(d => d.CreatedAtUtc >= fromUtc && d.CreatedAtUtc < toUtc, ct);
        var resolvedAll = await db.Doubts.Where(d => d.ResolvedAtUtc >= fromUtc && d.ResolvedAtUtc < toUtc).Select(d => new { d.CreatedAtUtc, d.ResolvedAtUtc }).ToListAsync(ct);
        var open = await db.Doubts.CountAsync(d => d.Status != DoubtStatus.Resolved, ct);
        var doubts = new DoubtReportDto(raised, resolvedAll.Count, open, resolvedAll.Count == 0 ? null : Math.Round(resolvedAll.Average(r => (r.ResolvedAtUtc!.Value - r.CreatedAtUtc).TotalHours), 1));

        var published = db.Assignments.Where(a => a.Status != AssignmentStatus.Draft && a.DueAtUtc >= fromUtc && a.DueAtUtc < toUtc);
        var expected = await db.AssignmentRecipients.CountAsync(r => published.Any(a => a.Id == r.AssignmentId), ct);
        var subs = db.Submissions.Where(s => published.Any(a => a.Id == s.AssignmentId) && s.Status != SubmissionStatus.Draft);
        var assignments = new AssignmentReportDto(await published.CountAsync(ct), expected, await subs.CountAsync(ct),
            await subs.CountAsync(s => s.Status == SubmissionStatus.Graded, ct), await subs.CountAsync(s => s.IsLate, ct));

        return new InstitutionReportDto(from, to, byType, attendance, teachers, batches, doubts, assignments);
    }

    public async Task<string> AttendanceCsvAsync(ReportQuery q, CancellationToken ct)
    {
        access.Require(Permissions.ReportsView);
        var from = q.From ?? DateOnly.FromDateTime(clock.UtcNow.AddDays(-30));
        var to = q.To ?? DateOnly.FromDateTime(clock.UtcNow);
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rows = await db.Attendance.Where(a => a.Session!.StartUtc >= fromUtc && a.Session.StartUtc < toUtc)
            .Where(a => q.SessionType == null || a.Session!.SessionType == q.SessionType)
            .Where(a => q.TeacherId == null || a.Session!.TeacherId == q.TeacherId)
            .Where(a => q.BatchId == null || a.Session!.BatchId == q.BatchId)
            .OrderBy(a => a.Session!.StartUtc).Take(50_000)
            .Select(a => new { a.Session!.StartUtc, a.Session.Title, a.Session.SessionType, Student = a.Student!.User!.FullName, a.Student.StudentCode, a.Status })
            .ToListAsync(ct);
        var sb = new StringBuilder("SessionStartUtc,Session,Type,Student,StudentCode,Status\n");
        foreach (var r in rows)
            sb.Append(CultureInfo.InvariantCulture, $"{r.StartUtc:yyyy-MM-dd HH:mm},{Csv(r.Title)},{r.SessionType},{Csv(r.Student)},{Csv(r.StudentCode)},{r.Status}\n");
        return sb.ToString();
    }

    /// <summary>Escapes CSV and neutralises spreadsheet formula injection.</summary>
    private static string Csv(string v)
    {
        if (v.Length > 0 && "=+-@".Contains(v[0])) v = "'" + v;
        return v.Contains(',') || v.Contains('"') || v.Contains('\n') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }
}
