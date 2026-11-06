using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Assessments;
using StudentSync.Domain.Assignments;
using StudentSync.Domain.Doubts;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Reporting;

/// <summary>
/// Evidence-based progress metrics. Every metric states how it is calculated; there is no invented composite score,
/// and recording/viewing data is never used to rank or evaluate students.
/// </summary>
public sealed class ProgressService(IAppDbContext db, AccessPolicy access, IClock clock)
{
    public async Task<ProgressDto> ForStudentAsync(Guid studentId, CancellationToken ct)
    {
        await access.EnsureCanViewStudentAsync(studentId, ct);
        var since = clock.UtcNow.AddDays(-90);

        var attendance = await db.Attendance.Where(a => a.StudentId == studentId && a.Session!.StartUtc >= since)
            .GroupBy(a => a.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var attended = attendance.Where(a => a.Key is AttendanceStatus.Present or AttendanceStatus.Late).Sum(a => a.Count);
        var countable = attendance.Where(a => a.Key != AttendanceStatus.Excused).Sum(a => a.Count);

        var assigned = await db.AssignmentRecipients.CountAsync(r => r.StudentId == studentId
            && db.Assignments.Any(a => a.Id == r.AssignmentId && a.Status != AssignmentStatus.Draft && a.DueAtUtc <= clock.UtcNow), ct);
        var submitted = await db.Submissions.CountAsync(s => s.StudentId == studentId && s.Status != SubmissionStatus.Draft && s.Assignment!.DueAtUtc <= clock.UtcNow, ct);
        var graded = await db.Submissions.Where(s => s.StudentId == studentId && s.Status == SubmissionStatus.Graded && s.Marks != null && s.Assignment!.MaxMarks > 0)
            .Select(s => s.Marks!.Value / s.Assignment!.MaxMarks).ToListAsync(ct);
        var exams = await db.ExamAttempts.Where(a => a.StudentId == studentId && a.Status == AttemptStatus.Graded && a.Exam!.ResultsReleased && a.Score != null)
            .Select(a => new { a.Score, Total = a.Exam!.Questions.Sum(q => q.Marks) }).ToListAsync(ct);
        var examPct = exams.Where(e => e.Total > 0).Select(e => (double)(e.Score!.Value / e.Total)).ToList();
        var openDoubts = await db.Doubts.CountAsync(d => d.StudentId == studentId && d.Status != DoubtStatus.Resolved, ct);
        var weeks = await db.Attendance.Where(a => a.StudentId == studentId && a.Session!.StartUtc >= clock.UtcNow.AddDays(-56)
                && (a.Status == AttendanceStatus.Present || a.Status == AttendanceStatus.Late))
            .Select(a => a.Session!.StartUtc).ToListAsync(ct);
        var activeWeeks = weeks.Select(w => ISOWeek.GetWeekOfYear(w) + ISOWeek.GetYear(w) * 100).Distinct().Count();

        static string Pct(double? v) => v is null ? "—" : $"{v:0}%";
        double? attRate = countable == 0 ? null : 100.0 * attended / countable;
        double? subRate = assigned == 0 ? null : 100.0 * submitted / assigned;
        double? avgMark = graded.Count == 0 ? null : (double)graded.Average() * 100;
        double? avgExam = examPct.Count == 0 ? null : examPct.Average() * 100;

        return new ProgressDto(studentId,
            new MetricDto("Attendance", Pct(attRate), "Present or late ÷ marked sessions in the last 90 days (excused absences are not counted).", attRate),
            new MetricDto("Assignments submitted", subRate is null ? "—" : $"{submitted}/{assigned}", "Submitted ÷ assignments whose deadline has passed.", subRate),
            new MetricDto("Average assignment mark", Pct(avgMark), "Mean of marks ÷ maximum marks across graded assignments.", avgMark),
            new MetricDto("Average exam score", Pct(avgExam), "Mean percentage across exams whose results have been released.", avgExam),
            new MetricDto("Open doubts", openDoubts.ToString(CultureInfo.InvariantCulture), "Questions you asked that are not yet confirmed as resolved."),
            new MetricDto("Lessons attended", attended.ToString(CultureInfo.InvariantCulture), "Sessions marked present or late in the last 90 days."),
            new MetricDto("Active weeks", $"{activeWeeks}/8", "Weeks in the last 8 with at least one attended session. Missing a week is never penalised beyond this count.", activeWeeks / 8.0 * 100));
    }
}
