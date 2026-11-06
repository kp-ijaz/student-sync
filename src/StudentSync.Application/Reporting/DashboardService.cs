using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Assignments;
using StudentSync.Application.Common;
using StudentSync.Application.Doubts;
using StudentSync.Application.Scheduling;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.AI;
using StudentSync.Domain.Assessments;
using StudentSync.Domain.Assignments;
using StudentSync.Domain.Content;
using StudentSync.Domain.Doubts;
using StudentSync.Domain.People;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Reporting;

/// <summary>Role dashboards. Every figure is computed from stored records; nothing is hard-coded.</summary>
public sealed class DashboardService(IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, ProgressService progress, AssignmentService assignments, PhotoUrls photos)
{
    public async Task<AdminDashboardDto> AdminAsync(CancellationToken ct)
    {
        if (!access.IsAdmin) throw new ForbiddenException();
        var now = clock.UtcNow;
        var from30 = now.AddDays(-30);
        var students = await db.Students.CountAsync(s => s.Status == PersonStatus.Active, ct);
        var teachers = await db.Teachers.CountAsync(t => t.Status == PersonStatus.Active, ct);
        var courses = await db.Courses.CountAsync(c => c.Status == CatalogStatus.Active, ct);
        var batches = await db.Batches.CountAsync(b => b.Status == CatalogStatus.Active, ct);
        var arrangements = await db.Arrangements.CountAsync(a => a.Status == CatalogStatus.Active, ct);
        var week = await db.Sessions.CountAsync(s => s.Status != SessionStatus.Cancelled && s.StartUtc >= now && s.StartUtc < now.AddDays(7), ct);
        var openDoubts = await db.Doubts.CountAsync(d => d.Status != DoubtStatus.Resolved, ct);

        var upcoming = await photos.WithTeacherPhotosAsync(await SchedulingService.Project(db.Sessions.Where(s => s.Status == SessionStatus.Scheduled && s.EndUtc >= now).OrderBy(s => s.StartUtc).Take(8)).ToListAsync(ct), ct);
        var attendance = await AttendanceSummaryAsync(db.Sessions.Where(s => s.StartUtc >= from30 && s.StartUtc <= now), ct);
        var byType = await SessionTypeCountsAsync(db.Sessions.Where(s => s.StartUtc >= from30 && s.StartUtc <= now.AddDays(30) && s.Status != SessionStatus.Cancelled), ct);

        var alerts = new List<AlertDto>();
        var unmarked = await db.Sessions.CountAsync(s => s.Status != SessionStatus.Cancelled && s.EndUtc < now && s.EndUtc >= from30
            && s.Participants.Any(p => p.Status == ParticipantStatus.Active && !db.Attendance.Any(a => a.SessionId == s.Id && a.StudentId == p.StudentId)), ct);
        if (unmarked > 0) alerts.Add(new AlertDto("warning", $"{unmarked} past session(s) in the last 30 days have incomplete attendance.", "/sessions?filter=attendance"));
        var stale = await db.Doubts.CountAsync(d => d.Status != DoubtStatus.Resolved && d.LastActivityAtUtc < now.AddDays(-3), ct);
        if (stale > 0) alerts.Add(new AlertDto("warning", $"{stale} doubt(s) have had no activity for 3+ days.", "/doubts"));
        var sub = await db.Subscriptions.Include(s => s.Plan).FirstOrDefaultAsync(ct);
        if (sub?.Plan is { } plan && students >= plan.MaxStudents * 0.9)
            alerts.Add(new AlertDto("info", $"You are using {students} of {plan.MaxStudents} student places on the {plan.Name} plan.", "/settings"));
        if (sub is { CurrentPeriodEndUtc: { } end } && end < now.AddDays(7))
            alerts.Add(new AlertDto("info", $"Your subscription period ends on {end:d MMM yyyy}.", "/settings"));

        var activity = (await db.AuditLogs.Where(a => a.TenantId == user.TenantId && !a.Action.StartsWith("auth."))
                .OrderByDescending(a => a.CreatedAtUtc).Take(10)
                .Select(a => new { a.CreatedAtUtc, a.Action, a.Summary, UserName = db.Users.Where(u => u.Id == a.UserId).Select(u => u.FullName).FirstOrDefault() })
                .ToListAsync(ct))
            .Select(a => new ActivityDto(a.CreatedAtUtc, DescribeAction(a.Action) + (a.Summary is null ? "" : ": " + a.Summary), a.UserName)).ToList();

        var stats = new List<StatDto>
        {
            new("students", "Active students", students.ToString(CultureInfo.InvariantCulture)),
            new("teachers", "Active teachers", teachers.ToString(CultureInfo.InvariantCulture)),
            new("courses", "Active courses", courses.ToString(CultureInfo.InvariantCulture), $"Batches: {batches} · private arrangements: {arrangements}"),
            new("sessions", "Sessions next 7 days", week.ToString(CultureInfo.InvariantCulture)),
            new("doubts", "Open doubts", openDoubts.ToString(CultureInfo.InvariantCulture)),
        };
        return new AdminDashboardDto(stats, upcoming, attendance, byType, alerts, activity);
    }

    public async Task<TeacherDashboardDto> TeacherAsync(CancellationToken ct)
    {
        if (!access.IsTeacher) throw new ForbiddenException();
        var me = user.UserId;
        var now = clock.UtcNow;
        var local = ZonedTime.ToLocal(now, user.TimeZoneId);
        var dayStart = ZonedTime.ToUtc(DateOnly.FromDateTime(local), TimeOnly.MinValue, user.TimeZoneId).Utc;
        var dayEnd = ZonedTime.ToUtc(DateOnly.FromDateTime(local).AddDays(1), TimeOnly.MinValue, user.TimeZoneId).Utc;
        var mine = db.Sessions.Where(s => s.TeacherId == me && s.Status != SessionStatus.Cancelled);

        var next = await photos.WithTeacherPhotoAsync(await SchedulingService.Project(mine.Where(s => s.EndUtc > now && s.Status != SessionStatus.Completed).OrderBy(s => s.StartUtc).Take(1)).FirstOrDefaultAsync(ct), ct);
        var today = await photos.WithTeacherPhotosAsync(await SchedulingService.Project(mine.Where(s => s.StartUtc >= dayStart && s.StartUtc < dayEnd).OrderBy(s => s.StartUtc)).ToListAsync(ct), ct);
        var upcoming = await photos.WithTeacherPhotosAsync(await SchedulingService.Project(mine.Where(s => s.StartUtc >= dayEnd && s.StartUtc < now.AddDays(7)).OrderBy(s => s.StartUtc).Take(10)).ToListAsync(ct), ct);
        var pendingAttendance = await photos.WithTeacherPhotosAsync(await SchedulingService.Project(mine.Where(s => s.EndUtc < now && s.EndUtc >= now.AddDays(-14)
            && s.Participants.Any(p => p.Status == ParticipantStatus.Active && !db.Attendance.Any(a => a.SessionId == s.Id && a.StudentId == p.StudentId)))
            .OrderByDescending(s => s.StartUtc).Take(10)).ToListAsync(ct), ct);

        var doubts = await db.Doubts.Where(d => d.AssignedTeacherId == me && d.Status != DoubtStatus.Resolved && d.Status != DoubtStatus.PendingConfirmation)
            .OrderByDescending(d => d.Priority).ThenBy(d => d.CreatedAtUtc).Take(8).Select(DoubtService.ListProjection(db)).ToListAsync(ct);
        var toGrade = await db.Submissions.Where(s => s.Status == SubmissionStatus.Submitted && s.Assignment!.TeacherId == me).OrderBy(s => s.SubmittedAtUtc).Take(10)
            .Select(s => new SubmissionToGradeDto(s.Id, s.AssignmentId, s.Assignment!.Title, s.Student!.User!.FullName, s.SubmittedAtUtc, s.IsLate)).ToListAsync(ct);
        var drafts = await db.AiDrafts.Where(d => d.Status == AiDraftStatus.AwaitingTeacherReview && db.Sessions.Any(s => s.Id == d.SessionId && s.TeacherId == me))
            .OrderBy(d => d.CreatedAtUtc).Take(10)
            .Select(d => new AiDraftSummaryWithSessionDto(d.Id, d.SessionId, db.Sessions.Where(s => s.Id == d.SessionId).Select(s => s.Title).First(), d.CreatedAtUtc)).ToListAsync(ct);
        var studentCount = await access.StudentsTaughtBy(me).CountAsync(ct);
        var oneToOne = await mine.CountAsync(s => s.SessionType == SessionType.OneToOne && s.StartUtc >= now && s.StartUtc < now.AddDays(7), ct);
        var group = await mine.CountAsync(s => s.SessionType != SessionType.OneToOne && s.StartUtc >= now && s.StartUtc < now.AddDays(7), ct);

        var stats = new List<StatDto>
        {
            new("students", "My students", studentCount.ToString(CultureInfo.InvariantCulture)),
            new("week", "Classes next 7 days", (oneToOne + group).ToString(CultureInfo.InvariantCulture), $"{group} group/batch · {oneToOne} one-to-one"),
            new("doubts", "Doubts to answer", doubts.Count.ToString(CultureInfo.InvariantCulture)),
            new("grading", "Submissions to grade", toGrade.Count.ToString(CultureInfo.InvariantCulture)),
        };
        return new TeacherDashboardDto(next, today, upcoming, pendingAttendance, doubts, toGrade, drafts, stats);
    }

    public async Task<StudentDashboardDto> StudentAsync(CancellationToken ct)
    {
        if (!access.IsStudent) throw new ForbiddenException();
        var me = user.UserId;
        var now = clock.UtcNow;
        var name = await db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync(ct);
        var mine = db.Sessions.Where(s => s.Status != SessionStatus.Cancelled && s.Participants.Any(p => p.StudentId == me && p.Status == ParticipantStatus.Active));
        var upcoming = await photos.WithTeacherPhotosAsync(await SchedulingService.Project(mine.Where(s => s.EndUtc > now && s.Status != SessionStatus.Completed).OrderBy(s => s.StartUtc).Take(6)).ToListAsync(ct), ct);
        var due = await assignments.Project(db.Assignments.Where(a => a.Status == AssignmentStatus.Published && a.Recipients.Any(r => r.StudentId == me)
            && !db.Submissions.Any(s => s.AssignmentId == a.Id && s.StudentId == me && (s.Status == SubmissionStatus.Submitted || s.Status == SubmissionStatus.Graded)))
            .OrderBy(a => a.DueAtUtc).Take(6), me).ToListAsync(ct);
        var doubts = await db.Doubts.Where(d => d.StudentId == me && d.Status != DoubtStatus.Resolved).OrderByDescending(d => d.LastActivityAtUtc).Take(5)
            .Select(DoubtService.ListProjection(db)).ToListAsync(ct);
        var sessionIds = mine.Select(s => s.Id);
        var notes = await db.Notes.Where(n => n.Status == PublicationStatus.Published && n.Visibility == NoteVisibility.Participants && sessionIds.Contains(n.SessionId))
            .OrderByDescending(n => n.PublishedAtUtc).Take(4)
            .Select(n => new RecentContentDto("Note", n.Id, n.SessionId, n.Title, db.Sessions.Where(s => s.Id == n.SessionId).Select(s => s.Title).First(), n.PublishedAtUtc ?? n.CreatedAtUtc)).ToListAsync(ct);
        var recs = await db.Recordings.Where(r => r.Status == RecordingStatus.Available && sessionIds.Contains(r.SessionId)).OrderByDescending(r => r.CreatedAtUtc).Take(4)
            .Select(r => new RecentContentDto("Recording", r.Id, r.SessionId, r.Title, db.Sessions.Where(s => s.Id == r.SessionId).Select(s => s.Title).First(), r.CreatedAtUtc)).ToListAsync(ct);
        var notifications = await db.Notifications.Where(n => n.UserId == me).OrderByDescending(n => n.CreatedAtUtc).Take(5)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Link, n.CreatedAtUtc, n.ReadAtUtc != null)).ToListAsync(ct);
        return new StudentDashboardDto(name.Split(' ')[0], upcoming.FirstOrDefault(), upcoming, due, doubts,
            notes.Concat(recs).OrderByDescending(x => x.AtUtc).Take(6).ToList(), await progress.ForStudentAsync(me, ct), notifications);
    }

    public async Task<ParentDashboardDto> ParentAsync(Guid? childId, CancellationToken ct)
    {
        if (!access.IsParent) throw new ForbiddenException();
        var ids = await access.ChildIdsAsync(ct);
        var children = await db.Students.Where(s => ids.Contains(s.Id)).OrderBy(s => s.User!.FullName).Select(s => new LookupItem(s.Id, s.User!.FullName, s.StudentCode)).ToListAsync(ct);
        var selected = childId is { } c && ids.Contains(c) ? c : children.FirstOrDefault()?.Id;
        if (childId is { } requested && !ids.Contains(requested)) throw new NotFoundException("Student");
        if (selected is not { } sid) return new ParentDashboardDto(children, null, null);

        var now = clock.UtcNow;
        var sessions = db.Sessions.Where(s => s.Participants.Any(p => p.StudentId == sid && p.Status == ParticipantStatus.Active));
        var upcoming = await photos.WithTeacherPhotosAsync(await SchedulingService.Project(sessions.Where(s => s.Status == SessionStatus.Scheduled && s.EndUtc > now).OrderBy(s => s.StartUtc).Take(6)).ToListAsync(ct), ct);
        var attendance = await db.Attendance.Where(a => a.StudentId == sid).OrderByDescending(a => a.Session!.StartUtc).Take(10)
            .Select(a => new AttendanceItemDto(a.SessionId, a.Session!.Title, a.Session.StartUtc, a.Session.SessionType, a.Status)).ToListAsync(ct);
        var work = await assignments.Project(db.Assignments.Where(a => a.Status != AssignmentStatus.Draft && a.Recipients.Any(r => r.StudentId == sid))
            .OrderByDescending(a => a.DueAtUtc).Take(8), sid).ToListAsync(ct);
        var results = await db.ExamAttempts.Where(a => a.StudentId == sid && a.Exam!.ResultsReleased && a.Status == AttemptStatus.Graded).OrderByDescending(a => a.SubmittedAtUtc).Take(6)
            .Select(a => new ExamResultItemDto(a.ExamId, a.Exam!.Title, a.SubmittedAtUtc, a.Score, a.Exam.Questions.Sum(q => q.Marks))).ToListAsync(ct);
        var name = children.First(x => x.Id == sid).Name;
        return new ParentDashboardDto(children, sid, new ChildOverviewDto(sid, name, upcoming, attendance, work, results, await progress.ForStudentAsync(sid, ct)));
    }

    /// <summary>"session.participant_added" → "Session participant added".</summary>
    public static string DescribeAction(string action)
    {
        var text = action.Replace('.', ' ').Replace('_', ' ');
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }

    // Shared report helpers

    public async Task<AttendanceSummaryDto> AttendanceSummaryAsync(IQueryable<LearningSession> sessions, CancellationToken ct)
    {
        var s = sessions.Where(x => x.Status != SessionStatus.Cancelled);
        var rows = await db.Attendance.Where(a => s.Any(x => x.Id == a.SessionId)).GroupBy(a => a.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var places = await db.SessionParticipants.CountAsync(p => p.Status == ParticipantStatus.Active && s.Any(x => x.Id == p.SessionId && x.EndUtc < clock.UtcNow), ct);
        int Get(AttendanceStatus st) => rows.FirstOrDefault(r => r.Key == st)?.Count ?? 0;
        var present = Get(AttendanceStatus.Present); var late = Get(AttendanceStatus.Late); var absent = Get(AttendanceStatus.Absent); var excused = Get(AttendanceStatus.Excused);
        var marked = present + late + absent + excused;
        var countable = present + late + absent;
        return new AttendanceSummaryDto(present, late, absent, excused, Math.Max(0, places - marked), countable == 0 ? null : Math.Round(100.0 * (present + late) / countable, 1));
    }

    /// <summary>Counts sessions per teaching mode. Each session is counted once, by its own type, so modes are never double-counted.</summary>
    public async Task<IReadOnlyList<SessionTypeCountDto>> SessionTypeCountsAsync(IQueryable<LearningSession> sessions, CancellationToken ct)
    {
        var rows = await sessions.GroupBy(s => s.SessionType)
            .Select(g => new { g.Key, Sessions = g.Count(), Places = g.Sum(s => s.Participants.Count(p => p.Status == ParticipantStatus.Active)) }).ToListAsync(ct);
        return Enum.GetValues<SessionType>().Select(t => new SessionTypeCountDto(t, rows.FirstOrDefault(r => r.Key == t)?.Sessions ?? 0, rows.FirstOrDefault(r => r.Key == t)?.Places ?? 0)).ToList();
    }
}
