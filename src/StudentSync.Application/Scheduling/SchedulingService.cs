using System.Text;
using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Application.Content;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Common;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Scheduling;

public sealed class SchedulingService(
    IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, AuditService audit, NotificationService notify, PhotoUrls photos)
{
    private sealed record Occurrence(DateOnly LocalDate, DateTime StartUtc, DateTime EndUtc, LocalTimeResolution Resolution);

    private sealed record Plan(
        CreateSessionRequest Request, string Title, Teacher Teacher, Batch? Batch, TeachingArrangement? Arrangement,
        List<Student> Students, List<Occurrence> Occurrences, List<(DateOnly Date, string Name)> SkippedHolidays, List<string> Warnings);

    // ---------------- Create ----------------

    public async Task<ConflictCheckResponse> CheckAsync(CreateSessionRequest request, CancellationToken ct)
    {
        var plan = await BuildPlanAsync(request, ct);
        var conflicts = await FindConflictsAsync(plan.Teacher.Id, plan.Students.Select(s => s.Id).ToList(),
            plan.Occurrences.Select(o => (o.StartUtc, o.EndUtc)).ToList(), excludeSessionId: null, ct);
        var preview = plan.Occurrences.Select(o => new OccurrencePreviewDto(o.LocalDate, o.StartUtc, o.EndUtc, o.Resolution.ToString(), false))
            .Concat(plan.SkippedHolidays.Select(h => new OccurrencePreviewDto(h.Date, default, default, $"Skipped: {h.Name}", true)))
            .OrderBy(o => o.LocalDate).ToList();
        return new ConflictCheckResponse(conflicts.Any(c => c.Kind == "Teacher"), conflicts, preview, plan.Warnings);
    }

    public async Task<CreateSessionResponse> CreateAsync(CreateSessionRequest request, CancellationToken ct)
    {
        var plan = await BuildPlanAsync(request, ct);
        var conflicts = await FindConflictsAsync(plan.Teacher.Id, plan.Students.Select(s => s.Id).ToList(),
            plan.Occurrences.Select(o => (o.StartUtc, o.EndUtc)).ToList(), null, ct);
        ThrowIfConflicts(conflicts, request.AcknowledgeConflicts);

        var r = plan.Request;
        SessionSeries? series = null;
        if (r.Recurrence is { } rec)
        {
            series = new SessionSeries
            {
                Title = plan.Title,
                SessionType = r.SessionType,
                TeacherId = plan.Teacher.Id,
                BatchId = plan.Batch?.Id,
                ArrangementId = plan.Arrangement?.Id,
                CourseId = r.CourseId,
                SubjectId = r.SubjectId,
                TimeZoneId = r.TimeZoneId,
                LocalStartTime = r.LocalStartTime,
                DurationMinutes = r.DurationMinutes,
                Frequency = rec.Frequency,
                Interval = rec.Interval,
                Days = rec.Days,
                StartDate = r.LocalDate,
                EndDate = rec.EndDate,
                OccurrenceCount = rec.Count,
                SkipHolidays = rec.SkipHolidays,
                DeliveryMode = r.DeliveryMode,
                MeetingUrl = Text.OptionalUrl(r.MeetingUrl, "meetingUrl"),
                Location = Text.Optional(r.Location, "location", 300),
            };
            db.SessionSeries.Add(series);
        }

        var created = new List<LearningSession>();
        foreach (var o in plan.Occurrences)
        {
            var session = new LearningSession
            {
                Title = plan.Title,
                SessionType = r.SessionType,
                TeacherId = plan.Teacher.Id,
                BatchId = plan.Batch?.Id,
                ArrangementId = plan.Arrangement?.Id,
                CourseId = r.CourseId ?? plan.Batch?.CourseId,
                SubjectId = r.SubjectId ?? plan.Batch?.SubjectId ?? plan.Arrangement?.SubjectId,
                ChapterId = r.ChapterId,
                StartUtc = o.StartUtc,
                EndUtc = o.EndUtc,
                TimeZoneId = r.TimeZoneId,
                DeliveryMode = r.DeliveryMode,
                MeetingProvider = string.IsNullOrWhiteSpace(r.MeetingUrl) ? MeetingProvider.None : MeetingProvider.Manual,
                MeetingUrl = Text.OptionalUrl(r.MeetingUrl, "meetingUrl"),
                Location = Text.Optional(r.Location, "location", 300),
                Objectives = Text.Optional(r.Objectives, "objectives", 4000),
                OccurrenceDate = series is null ? null : o.LocalDate,
            };
            if (series is not null) session.SeriesId = series.Id;
            foreach (var s in plan.Students)
                session.Participants.Add(new SessionParticipant { StudentId = s.Id, Status = ParticipantStatus.Active });
            db.Sessions.Add(session);
            created.Add(session);
        }

        audit.Record("session.created", "LearningSession", created[0].Id,
            $"{r.SessionType} '{plan.Title}' x{created.Count} (batch: {(plan.Batch?.Code ?? "none")})");

        var first = created.OrderBy(s => s.StartUtc).First();
        var recipients = await notify.WithGuardiansAsync(plan.Students.Select(s => s.Id), ct);
        var when = DescribeLocal(first.StartUtc, r.TimeZoneId);
        await notify.NotifyAsync(recipients, NotificationType.SessionScheduled,
            created.Count > 1 ? $"New recurring class: {plan.Title}" : $"New class: {plan.Title}",
            created.Count > 1 ? $"{created.Count} sessions scheduled, starting {when}." : $"Scheduled for {when}.",
            $"/sessions/{first.Id}", $"session-scheduled:{series?.Id ?? first.Id}", ct);

        await db.SaveChangesAsync(ct);
        return new CreateSessionResponse(created.OrderBy(s => s.StartUtc).Select(s => s.Id).ToList(), series?.Id, plan.Warnings);
    }

    private async Task<Plan> BuildPlanAsync(CreateSessionRequest r, CancellationToken ct)
    {
        var title = Text.Required(r.Title, "title", 200);
        if (r.DurationMinutes is < 15 or > 720) throw RequestValidationException.For("durationMinutes", "Duration must be between 15 and 720 minutes.");
        if (!ZonedTime.TryFind(r.TimeZoneId, out _)) throw RequestValidationException.For("timeZoneId", "Choose a valid time zone.");
        var warnings = new List<string>();

        // Teacher: admins choose; teachers may only schedule themselves.
        Guid teacherId;
        if (user.Has(Permissions.SessionsManageAll))
            teacherId = r.TeacherId ?? throw RequestValidationException.For("teacherId", "Choose a teacher.");
        else if (user.Has(Permissions.SessionsManageOwn) && user.Role == UserRole.Teacher)
        {
            if (r.TeacherId is { } t && t != user.UserId) throw new ForbiddenException("Teachers can only schedule their own sessions.");
            teacherId = user.UserId;
        }
        else throw new ForbiddenException();

        var teacher = await db.Teachers.Include(t => t.User).FirstOrDefaultAsync(t => t.Id == teacherId, ct)
                      ?? throw RequestValidationException.For("teacherId", "Teacher not found.");
        if (teacher.Status != PersonStatus.Active) throw RequestValidationException.For("teacherId", "This teacher is not active.");

        Batch? batch = null;
        if (r.BatchId is { } batchId)
        {
            batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == batchId, ct) ?? throw RequestValidationException.For("batchId", "Batch not found.");
            if (batch.Status != CatalogStatus.Active) throw RequestValidationException.For("batchId", "This batch is not active.");
            if (!user.Has(Permissions.SessionsManageAll) && batch.TeacherId != teacherId)
                throw new ForbiddenException("You can only schedule sessions for your own batches.");
        }

        TeachingArrangement? arrangement = null;
        if (r.ArrangementId is { } arrId)
        {
            arrangement = await db.Arrangements.Include(a => a.Students).FirstOrDefaultAsync(a => a.Id == arrId, ct)
                          ?? throw RequestValidationException.For("arrangementId", "Teaching arrangement not found.");
            if (arrangement.TeacherId != teacherId) throw RequestValidationException.For("arrangementId", "The arrangement belongs to a different teacher.");
            var expected = arrangement.Type == ArrangementType.OneToOne ? SessionType.OneToOne : SessionType.PrivateGroup;
            if (r.SessionType != expected) throw RequestValidationException.For("sessionType", $"This arrangement is {arrangement.Type}; schedule a {expected} session.");
        }

        // Participants: batch sessions use the batch roster; other types use the explicit selection (or the arrangement roster).
        List<Guid> studentIds = r.SessionType switch
        {
            SessionType.Batch when batch is not null => await db.BatchEnrolments
                .Where(e => e.BatchId == batch.Id && e.Status == EnrolmentStatus.Active).Select(e => e.StudentId).ToListAsync(ct),
            _ when r.StudentIds is { Count: > 0 } => r.StudentIds.Distinct().ToList(),
            _ when arrangement is not null => arrangement.Students.Where(s => s.Status == EnrolmentStatus.Active).Select(s => s.StudentId).ToList(),
            _ => [],
        };
        if (r.SessionType == SessionType.Batch && batch is not null && studentIds.Count == 0)
            throw RequestValidationException.For("batchId", "This batch has no active students yet.");
        var students = await db.Students.Include(s => s.User).Where(s => studentIds.Contains(s.Id)).ToListAsync(ct);
        if (students.Count != studentIds.Count) throw RequestValidationException.For("studentIds", "One or more selected students were not found.");
        if (students.Any(s => s.Status != PersonStatus.Active)) throw RequestValidationException.For("studentIds", "Inactive students cannot be scheduled.");

        LearningSession.ValidateShape(r.SessionType, batch?.Id, students.Count);

        if (r.CourseId is { } courseId && !await db.Courses.AnyAsync(c => c.Id == courseId, ct))
            throw RequestValidationException.For("courseId", "Course not found.");
        if (r.SubjectId is { } subjectId && !await db.Subjects.AnyAsync(s => s.Id == subjectId, ct))
            throw RequestValidationException.For("subjectId", "Subject not found.");
        if (r.ChapterId is { } chapterId)
        {
            var chapter = await db.Chapters.FirstOrDefaultAsync(c => c.Id == chapterId, ct) ?? throw RequestValidationException.For("chapterId", "Chapter not found.");
            if (r.SubjectId is { } sid && chapter.SubjectId != sid) throw RequestValidationException.For("chapterId", "The chapter belongs to a different subject.");
        }

        // Expand occurrences in local time and convert each one, so DST never moves the wall-clock time.
        var dates = new List<DateOnly>();
        var skipped = new List<(DateOnly, string)>();
        if (r.Recurrence is { } rec)
        {
            var pattern = new RecurrencePattern(rec.Frequency, rec.Interval, rec.Days, r.LocalDate, rec.EndDate, rec.Count);
            List<DateOnly> all;
            try { all = pattern.Dates().ToList(); }
            catch (DomainException ex) { throw RequestValidationException.For("recurrence", ex.Message); }
            var holidays = rec.SkipHolidays
                ? await db.Holidays.Where(h => h.Date >= r.LocalDate).ToDictionaryAsync(h => h.Date, h => h.Name, ct)
                : [];
            foreach (var d in all)
            {
                if (holidays.TryGetValue(d, out var name)) skipped.Add((d, name));
                else dates.Add(d);
            }
            if (skipped.Count > 0) warnings.Add($"{skipped.Count} occurrence(s) skipped because of institution holidays.");
        }
        else dates.Add(r.LocalDate);
        if (dates.Count == 0) throw RequestValidationException.For("recurrence", "The schedule produces no sessions.");

        var occurrences = new List<Occurrence>();
        foreach (var d in dates)
        {
            var zoned = ZonedTime.ToUtc(d, r.LocalStartTime, r.TimeZoneId);
            var end = zoned.Utc.AddMinutes(r.DurationMinutes);
            occurrences.Add(new Occurrence(d, zoned.Utc, end, zoned.Resolution));
            if (zoned.Resolution == LocalTimeResolution.SkippedShiftedForward)
                warnings.Add($"{d:yyyy-MM-dd}: {r.LocalStartTime:HH\\:mm} does not exist in {r.TimeZoneId} (clocks go forward); the session starts later by the DST gap.");
            if (zoned.Resolution == LocalTimeResolution.AmbiguousEarlierChosen)
                warnings.Add($"{d:yyyy-MM-dd}: {r.LocalStartTime:HH\\:mm} occurs twice in {r.TimeZoneId} (clocks go back); the first occurrence is used.");
        }
        if (occurrences.Any(o => o.StartUtc < clock.UtcNow)) warnings.Add("Some sessions are in the past.");

        return new Plan(r, title, teacher, batch, arrangement, students, occurrences, skipped, warnings);
    }

    // ---------------- Conflicts ----------------

    private async Task<List<ScheduleConflictDto>> FindConflictsAsync(Guid teacherId, List<Guid> studentIds, List<(DateTime Start, DateTime End)> slots, Guid? excludeSessionId, CancellationToken ct)
    {
        if (slots.Count == 0) return [];
        var min = slots.Min(s => s.Start);
        var max = slots.Max(s => s.End);
        var candidates = await db.Sessions
            .Where(s => s.Status != SessionStatus.Cancelled && s.StartUtc < max && s.EndUtc > min && s.Id != excludeSessionId
                        && (s.TeacherId == teacherId || s.Participants.Any(p => studentIds.Contains(p.StudentId) && p.Status == ParticipantStatus.Active)))
            .Select(s => new
            {
                s.Id,
                s.Title,
                s.StartUtc,
                s.EndUtc,
                s.TeacherId,
                TeacherName = s.Teacher!.User!.FullName,
                Students = s.Participants.Where(p => p.Status == ParticipantStatus.Active && studentIds.Contains(p.StudentId))
                    .Select(p => new { p.StudentId, p.Student!.User!.FullName }).ToList(),
            })
            .ToListAsync(ct);

        var conflicts = new List<ScheduleConflictDto>();
        foreach (var (start, end) in slots)
        {
            foreach (var c in candidates.Where(c => c.StartUtc < end && c.EndUtc > start))
            {
                if (c.TeacherId == teacherId)
                    conflicts.Add(new ScheduleConflictDto("Teacher", teacherId, c.TeacherName, c.Id, c.Title, c.StartUtc, c.EndUtc));
                foreach (var s in c.Students)
                    conflicts.Add(new ScheduleConflictDto("Student", s.StudentId, s.FullName, c.Id, c.Title, c.StartUtc, c.EndUtc));
            }
        }
        return conflicts.DistinctBy(c => (c.Kind, c.PersonId, c.SessionId)).Take(100).ToList();
    }

    private static void ThrowIfConflicts(List<ScheduleConflictDto> conflicts, bool acknowledged)
    {
        if (conflicts.Any(c => c.Kind == "Teacher"))
            throw new ConflictException("The teacher already has a session at this time.", "teacher_conflict", conflicts);
        if (conflicts.Count > 0 && !acknowledged)
            throw new ConflictException("Some students already have a session at this time. Review and confirm to continue.", "student_conflict", conflicts);
    }

    // ---------------- Queries ----------------

    public async Task<IReadOnlyList<SessionListItemDto>> ListAsync(SessionQuery q, CancellationToken ct)
    {
        var sessions = await access.VisibleSessionsAsync(ct);
        if (q.FromUtc is { } from) sessions = sessions.Where(s => s.EndUtc >= from);
        if (q.ToUtc is { } to) sessions = sessions.Where(s => s.StartUtc <= to);
        if (q.SessionType is { } type) sessions = sessions.Where(s => s.SessionType == type);
        if (q.Status is { } status) sessions = sessions.Where(s => s.Status == status);
        if (q.TeacherId is { } teacherId) sessions = sessions.Where(s => s.TeacherId == teacherId);
        if (q.BatchId is { } batchId) sessions = sessions.Where(s => s.BatchId == batchId);
        if (q.StudentId is { } studentId)
        {
            await access.EnsureCanViewStudentAsync(studentId, ct);
            sessions = sessions.Where(s => s.Participants.Any(p => p.StudentId == studentId && p.Status == ParticipantStatus.Active));
        }
        var ordered = q.FromUtc is null && q.ToUtc is not null ? sessions.OrderByDescending(s => s.StartUtc) : sessions.OrderBy(s => s.StartUtc);
        return await photos.WithTeacherPhotosAsync(await Project(ordered.Take(Math.Clamp(q.Limit ?? 200, 1, 500))).ToListAsync(ct), ct);
    }

    public static IQueryable<SessionListItemDto> Project(IQueryable<LearningSession> q) => q.Select(s => new SessionListItemDto(
        s.Id, s.Title, s.SessionType, s.Status, s.StartUtc, s.EndUtc, s.TimeZoneId, s.TeacherId, s.Teacher!.User!.FullName,
        s.BatchId, s.Batch != null ? s.Batch.Name : null, s.Subject != null ? s.Subject.Name : null,
        s.Participants.Count(p => p.Status == ParticipantStatus.Active), s.DeliveryMode, s.MeetingUrl != null, s.SeriesId != null, null));

    public async Task<SessionDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var s = await access.GetVisibleSessionAsync(id, ct);
        var detail = await db.Sessions.Where(x => x.Id == s.Id).Select(x => new
        {
            TeacherName = x.Teacher!.User!.FullName,
            TeacherPhotoFileId = x.Teacher.User.PhotoFileId,
            BatchName = x.Batch != null ? x.Batch.Name : null,
            ArrangementTitle = x.Arrangement != null ? x.Arrangement.Title : null,
            CourseName = x.Course != null ? x.Course.Name : null,
            SubjectName = x.Subject != null ? x.Subject.Name : null,
            ChapterTitle = x.Chapter != null ? x.Chapter.Title : null,
        }).FirstAsync(ct);

        var participants = db.SessionParticipants.Where(p => p.SessionId == id);
        var canManage = access.CanManageSession(s);
        if (!canManage)
        {
            // Students and parents see only their own participation; classmates are never disclosed.
            var own = access.IsParent ? await access.ChildIdsAsync(ct) : [user.UserId];
            participants = participants.Where(p => own.Contains(p.StudentId));
        }
        var participantDtos = await participants
            .OrderBy(p => p.Student!.User!.FullName)
            .Select(p => new ParticipantDto(p.StudentId, p.Student!.User!.FullName, p.Status,
                db.Attendance.Where(a => a.SessionId == id && a.StudentId == p.StudentId).Select(a => (AttendanceStatus?)a.Status).FirstOrDefault()))
            .ToListAsync(ct);
        var count = await db.SessionParticipants.CountAsync(p => p.SessionId == id && p.Status == ParticipantStatus.Active, ct);

        return new SessionDetailDto(s.Id, s.Title, s.SessionType, s.Status, s.StartUtc, s.EndUtc, s.TimeZoneId, s.TeacherId, detail.TeacherName,
            s.BatchId, detail.BatchName, s.ArrangementId, detail.ArrangementTitle, s.SeriesId, s.CourseId, detail.CourseName,
            s.SubjectId, detail.SubjectName, s.ChapterId, detail.ChapterTitle, s.DeliveryMode, s.MeetingProvider,
            s.Status == SessionStatus.Cancelled ? null : s.MeetingUrl, s.Location, s.Objectives, s.CancellationReason,
            canManage, canManage && user.Has(Permissions.AttendanceMark) && s.Status != SessionStatus.Cancelled,
            count, participantDtos, RowVersions.Encode(s.RowVersion), photos.For(detail.TeacherPhotoFileId));
    }

    public async Task<IReadOnlyList<ScheduleChangeDto>> HistoryAsync(Guid id, CancellationToken ct)
    {
        var s = await access.GetVisibleSessionAsync(id, ct);
        var canManage = access.CanManageSession(s);
        return await db.SessionChanges.Where(c => c.SessionId == id)
            .Where(c => canManage || c.ChangeType == ScheduleChangeType.Rescheduled || c.ChangeType == ScheduleChangeType.Cancelled)
            .OrderByDescending(c => c.ChangedAtUtc)
            .Select(c => new ScheduleChangeDto(c.ChangedAtUtc, c.ChangeType, c.PreviousStartUtc, c.NewStartUtc,
                c.StudentId == null ? null : db.Users.Where(u => u.Id == c.StudentId).Select(u => u.FullName).FirstOrDefault(),
                c.Reason, db.Users.Where(u => u.Id == c.ChangedByUserId).Select(u => u.FullName).FirstOrDefault() ?? "System"))
            .ToListAsync(ct);
    }

    // ---------------- Changes ----------------

    public async Task UpdateDetailsAsync(Guid id, UpdateSessionDetailsRequest r, CancellationToken ct)
    {
        var s = await access.GetManageableSessionAsync(id, ct);
        RowVersions.Apply(db, s, r.RowVersion);
        if (s.Status == SessionStatus.Cancelled) throw new DomainException("Cancelled sessions cannot be edited.", "invalid_session_state");
        if (r.SubjectId is { } subjectId && !await db.Subjects.AnyAsync(x => x.Id == subjectId, ct)) throw RequestValidationException.For("subjectId", "Subject not found.");
        if (r.ChapterId is { } chapterId && !await db.Chapters.AnyAsync(x => x.Id == chapterId && (r.SubjectId == null || x.SubjectId == r.SubjectId), ct))
            throw RequestValidationException.For("chapterId", "Chapter not found for this subject.");
        s.Title = Text.Required(r.Title, "title", 200);
        s.SubjectId = r.SubjectId;
        s.ChapterId = r.ChapterId;
        s.DeliveryMode = r.DeliveryMode;
        s.MeetingUrl = Text.OptionalUrl(r.MeetingUrl, "meetingUrl");
        s.MeetingProvider = s.MeetingUrl is null ? MeetingProvider.None : MeetingProvider.Manual;
        s.Location = Text.Optional(r.Location, "location", 300);
        s.Objectives = Text.Optional(r.Objectives, "objectives", 4000);
        audit.Record("session.updated", "LearningSession", s.Id, s.Title);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> RescheduleAsync(Guid id, RescheduleSessionRequest r, CancellationToken ct)
    {
        var s = await access.GetManageableSessionAsync(id, ct, includeParticipants: true);
        RowVersions.Apply(db, s, r.RowVersion);
        if (r.DurationMinutes is < 15 or > 720) throw RequestValidationException.For("durationMinutes", "Duration must be between 15 and 720 minutes.");
        if (!ZonedTime.TryFind(r.TimeZoneId, out _)) throw RequestValidationException.For("timeZoneId", "Choose a valid time zone.");
        var zoned = ZonedTime.ToUtc(r.LocalDate, r.LocalStartTime, r.TimeZoneId);
        var end = zoned.Utc.AddMinutes(r.DurationMinutes);
        var studentIds = s.Participants.Where(p => p.Status == ParticipantStatus.Active).Select(p => p.StudentId).ToList();
        var conflicts = await FindConflictsAsync(s.TeacherId, studentIds, [(zoned.Utc, end)], s.Id, ct);
        ThrowIfConflicts(conflicts, r.AcknowledgeConflicts);

        await VideoService.PinUnlockedAsync(db, s, clock.UtcNow, ct);
        var change = s.Reschedule(zoned.Utc, end, r.TimeZoneId, Text.Optional(r.Reason, "reason", 500), user.UserId, clock.UtcNow);
        db.SessionChanges.Add(change);
        audit.Record("session.rescheduled", "LearningSession", s.Id, $"{change.PreviousStartUtc:u} -> {change.NewStartUtc:u}");
        await notify.NotifyAsync(await notify.WithGuardiansAsync(studentIds, ct), NotificationType.SessionRescheduled,
            $"Class rescheduled: {s.Title}", $"Now {DescribeLocal(zoned.Utc, r.TimeZoneId)}.", $"/sessions/{s.Id}",
            $"session-rescheduled:{s.Id}:{zoned.Utc.Ticks}", ct);
        await db.SaveChangesAsync(ct);

        var warnings = new List<string>();
        if (zoned.Resolution != LocalTimeResolution.Exact) warnings.Add($"The local time was adjusted for daylight saving ({zoned.Resolution}).");
        return warnings;
    }

    public async Task<int> CancelAsync(Guid id, CancelSessionRequest r, CancellationToken ct)
    {
        var s = await access.GetManageableSessionAsync(id, ct, includeParticipants: true);
        var reason = Text.Required(r.Reason, "reason", 500);
        var targets = new List<LearningSession> { s };
        if (r.CancelFutureSeriesOccurrences && s.SeriesId is { } seriesId)
        {
            targets.AddRange(await db.Sessions.Include(x => x.Participants)
                .Where(x => x.SeriesId == seriesId && x.Id != s.Id && x.StartUtc > s.StartUtc && x.Status == SessionStatus.Scheduled)
                .ToListAsync(ct));
            var series = await db.SessionSeries.FirstAsync(x => x.Id == seriesId, ct);
            series.IsActive = false;
        }
        foreach (var t in targets)
        {
            db.SessionChanges.Add(t.Cancel(reason, user.UserId, clock.UtcNow));
            // Unresolved doubts scheduled for this class must be carried forward again.
            await db.Doubts.Where(d => d.FollowUpSessionId == t.Id).ExecuteUpdateAsync(u => u.SetProperty(d => d.FollowUpSessionId, (Guid?)null), ct);
        }
        audit.Record("session.cancelled", "LearningSession", s.Id, $"{targets.Count} session(s): {reason}");
        var studentIds = targets.SelectMany(t => t.Participants).Where(p => p.Status == ParticipantStatus.Active).Select(p => p.StudentId);
        await notify.NotifyAsync(await notify.WithGuardiansAsync(studentIds, ct), NotificationType.SessionCancelled,
            $"Class cancelled: {s.Title}", targets.Count > 1 ? $"{targets.Count} sessions cancelled. Reason: {reason}" : $"{DescribeLocal(s.StartUtc, s.TimeZoneId)} — {reason}",
            $"/sessions/{s.Id}", $"session-cancelled:{s.Id}", ct);
        await db.SaveChangesAsync(ct);
        return targets.Count;
    }

    public async Task SetStatusAsync(Guid id, SetSessionStatusRequest r, CancellationToken ct)
    {
        var s = await access.GetManageableSessionAsync(id, ct);
        s.SetStatus(r.Status);
        audit.Record("session.status", "LearningSession", s.Id, r.Status.ToString());
        await db.SaveChangesAsync(ct);
    }

    public async Task AddParticipantAsync(Guid id, AddParticipantRequest r, CancellationToken ct)
    {
        var s = await access.GetManageableSessionAsync(id, ct, includeParticipants: true);
        if (s.Status is SessionStatus.Cancelled or SessionStatus.Completed) throw new DomainException("Participants cannot be changed after a session ends or is cancelled.", "invalid_session_state");
        var student = await db.Students.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == r.StudentId, ct) ?? throw RequestValidationException.For("studentId", "Student not found.");
        if (student.Status != PersonStatus.Active) throw RequestValidationException.For("studentId", "This student is not active.");
        var existing = s.Participants.FirstOrDefault(p => p.StudentId == student.Id);
        if (existing?.Status == ParticipantStatus.Active) throw new ConflictException("The student is already a participant.", "duplicate_participant");
        var activeAfter = s.Participants.Count(p => p.Status == ParticipantStatus.Active) + 1;
        LearningSession.ValidateShape(s.SessionType, s.BatchId, activeAfter);
        var conflicts = await FindConflictsAsync(Guid.Empty, [student.Id], [(s.StartUtc, s.EndUtc)], s.Id, ct);
        if (conflicts.Count > 0) throw new ConflictException($"{student.User!.FullName} already has a session at this time.", "student_conflict", conflicts);

        if (existing is not null) { existing.Status = ParticipantStatus.Active; existing.RemovedAtUtc = null; existing.RemovalReason = null; }
        else s.Participants.Add(new SessionParticipant { TenantId = s.TenantId, SessionId = s.Id, StudentId = student.Id });
        db.SessionChanges.Add(new SessionScheduleChange { TenantId = s.TenantId, SessionId = s.Id, ChangeType = ScheduleChangeType.ParticipantAdded, StudentId = student.Id, ChangedByUserId = user.UserId, ChangedAtUtc = clock.UtcNow });
        audit.Record("session.participant_added", "LearningSession", s.Id, student.User!.FullName);
        await notify.NotifyAsync(await notify.WithGuardiansAsync([student.Id], ct), NotificationType.SessionScheduled,
            $"Added to class: {s.Title}", DescribeLocal(s.StartUtc, s.TimeZoneId), $"/sessions/{s.Id}", $"participant-added:{s.Id}:{student.Id}:{clock.UtcNow:yyyyMMddHHmm}", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveParticipantAsync(Guid id, Guid studentId, RemoveParticipantRequest r, CancellationToken ct)
    {
        var s = await access.GetManageableSessionAsync(id, ct, includeParticipants: true);
        if (s.Status is SessionStatus.Cancelled or SessionStatus.Completed) throw new DomainException("Participants cannot be changed after a session ends or is cancelled.", "invalid_session_state");
        var p = s.Participants.FirstOrDefault(x => x.StudentId == studentId && x.Status == ParticipantStatus.Active) ?? throw new NotFoundException("Participant");
        if (s.SessionType == SessionType.OneToOne) throw new DomainException("A one-to-one session needs its student. Cancel the session instead.", "one_to_one_student_count");
        LearningSession.ValidateShape(s.SessionType, s.BatchId, s.Participants.Count(x => x.Status == ParticipantStatus.Active) - 1);
        p.Status = ParticipantStatus.Removed;
        p.RemovedAtUtc = clock.UtcNow;
        p.RemovalReason = Text.Required(r.Reason, "reason", 500);
        db.SessionChanges.Add(new SessionScheduleChange { TenantId = s.TenantId, SessionId = s.Id, ChangeType = ScheduleChangeType.ParticipantRemoved, StudentId = studentId, Reason = p.RemovalReason, ChangedByUserId = user.UserId, ChangedAtUtc = clock.UtcNow });
        audit.Record("session.participant_removed", "LearningSession", s.Id, $"{studentId}: {p.RemovalReason}");
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Agenda & class preparation ----------------

    public async Task<IReadOnlyList<AgendaItemDto>> AgendaAsync(Guid id, CancellationToken ct)
    {
        var s = await access.GetManageableSessionAsync(id, ct);
        return await db.AgendaItems.Where(a => a.SessionId == s.Id).OrderBy(a => a.SortOrder)
            .Select(a => new AgendaItemDto(a.Id, a.Kind, a.DoubtId, a.Title, a.IsDiscussed,
                a.DoubtId == null ? null : db.Doubts.Where(d => d.Id == a.DoubtId).Select(d => d.Student!.User!.FullName).FirstOrDefault()))
            .ToListAsync(ct);
    }

    public async Task<Guid> AddAgendaItemAsync(Guid id, AddAgendaItemRequest r, CancellationToken ct)
    {
        var s = await access.GetManageableSessionAsync(id, ct);
        var order = await db.AgendaItems.Where(a => a.SessionId == s.Id).Select(a => (int?)a.SortOrder).MaxAsync(ct) ?? 0;
        var item = new SessionAgendaItem { SessionId = s.Id, Kind = AgendaItemKind.Topic, Title = Text.Required(r.Title, "title", 300), SortOrder = order + 1 };
        db.AgendaItems.Add(item);
        await db.SaveChangesAsync(ct);
        return item.Id;
    }

    public async Task<ClassPreparationDto> PreparationAsync(Guid id, CancellationToken ct)
    {
        var detail = await GetAsync(id, ct);
        if (!detail.CanManage) throw new ForbiddenException();
        var agenda = await AgendaAsync(id, ct);
        var studentIds = detail.Participants.Where(p => p.Status == ParticipantStatus.Active).Select(p => p.StudentId).ToList();
        var since = clock.UtcNow.AddDays(-30);

        var openDoubts = await db.Doubts
            .Where(d => studentIds.Contains(d.StudentId) && Domain.Doubts.Doubt.Unresolved.Contains(d.Status)
                        && (d.AssignedTeacherId == detail.TeacherId || d.FollowUpSessionId == id || (detail.SubjectId != null && d.SubjectId == detail.SubjectId)))
            .OrderBy(d => d.CreatedAtUtc).Take(50)
            .Select(Doubts.DoubtService.ListProjection(db)).ToListAsync(ct);

        var students = new List<PrepStudentDto>();
        foreach (var p in detail.Participants.Where(p => p.Status == ParticipantStatus.Active))
        {
            var missed = await db.Attendance.CountAsync(a => a.StudentId == p.StudentId && a.Status == AttendanceStatus.Absent && a.Session!.StartUtc >= since && a.Session.TeacherId == detail.TeacherId, ct);
            var pending = await db.AssignmentRecipients.CountAsync(ar => ar.StudentId == p.StudentId
                && db.Assignments.Any(a => a.Id == ar.AssignmentId && a.TeacherId == detail.TeacherId && a.Status == Domain.Assignments.AssignmentStatus.Published)
                && !db.Submissions.Any(sub => sub.AssignmentId == ar.AssignmentId && sub.StudentId == p.StudentId && sub.Status != Domain.Assignments.SubmissionStatus.Draft), ct);
            var doubts = openDoubts.Count(d => d.StudentName == p.StudentName);
            students.Add(new PrepStudentDto(p.StudentId, p.StudentName, missed, pending, doubts));
        }
        return new ClassPreparationDto(detail, agenda, openDoubts, students);
    }

    // ---------------- Availability ----------------

    public async Task<IReadOnlyList<AvailabilityDto>> AvailabilityAsync(Guid teacherId, CancellationToken ct)
    {
        if (!access.IsAdmin && teacherId != user.UserId) throw new ForbiddenException();
        return await db.TeacherAvailability.Where(a => a.TeacherId == teacherId).OrderBy(a => a.DayOfWeek).ThenBy(a => a.StartLocalTime)
            .Select(a => new AvailabilityDto(a.Id, a.DayOfWeek, a.StartLocalTime, a.EndLocalTime, a.TimeZoneId)).ToListAsync(ct);
    }

    public async Task SaveAvailabilityAsync(Guid teacherId, SaveAvailabilityRequest r, CancellationToken ct)
    {
        if (!access.IsAdmin && teacherId != user.UserId) throw new ForbiddenException();
        if (!await db.Teachers.AnyAsync(t => t.Id == teacherId, ct)) throw new NotFoundException("Teacher");
        if (!ZonedTime.TryFind(r.TimeZoneId, out _)) throw RequestValidationException.For("timeZoneId", "Choose a valid time zone.");
        if (r.Slots.Count > 50) throw RequestValidationException.For("slots", "Too many availability slots.");
        foreach (var slot in r.Slots)
            if (slot.EndLocalTime <= slot.StartLocalTime) throw RequestValidationException.For("slots", "Each slot must end after it starts.");
        foreach (var day in r.Slots.GroupBy(s => s.DayOfWeek))
        {
            var ordered = day.OrderBy(s => s.StartLocalTime).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if (ordered[i].StartLocalTime < ordered[i - 1].EndLocalTime) throw RequestValidationException.For("slots", $"Slots on {day.Key} overlap.");
        }
        await db.TeacherAvailability.Where(a => a.TeacherId == teacherId).ExecuteDeleteAsync(ct);
        foreach (var slot in r.Slots)
            db.TeacherAvailability.Add(new TeacherAvailability { TeacherId = teacherId, DayOfWeek = slot.DayOfWeek, StartLocalTime = slot.StartLocalTime, EndLocalTime = slot.EndLocalTime, TimeZoneId = r.TimeZoneId });
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Calendar export ----------------

    public async Task<string> ExportIcsAsync(CancellationToken ct)
    {
        var sessions = await (await access.VisibleSessionsAsync(ct))
            .Where(s => s.EndUtc >= clock.UtcNow.AddDays(-30) && s.StartUtc <= clock.UtcNow.AddDays(180))
            .OrderBy(s => s.StartUtc).Take(1000)
            .Select(s => new { s.Id, s.Title, s.StartUtc, s.EndUtc, s.Status, s.Location, s.MeetingUrl, Teacher = s.Teacher!.User!.FullName })
            .ToListAsync(ct);
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Student Sync//Calendar//EN\r\nCALSCALE:GREGORIAN\r\n");
        foreach (var s in sessions)
        {
            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append($"UID:{s.Id}@studentsync\r\n");
            sb.Append($"DTSTAMP:{clock.UtcNow:yyyyMMdd'T'HHmmss'Z'}\r\n");
            sb.Append($"DTSTART:{s.StartUtc:yyyyMMdd'T'HHmmss'Z'}\r\n");
            sb.Append($"DTEND:{s.EndUtc:yyyyMMdd'T'HHmmss'Z'}\r\n");
            sb.Append($"SUMMARY:{IcsEscape(s.Title)}\r\n");
            sb.Append($"DESCRIPTION:{IcsEscape($"Teacher: {s.Teacher}" + (s.MeetingUrl is null ? "" : $"\nJoin: {s.MeetingUrl}"))}\r\n");
            if (s.Location is not null) sb.Append($"LOCATION:{IcsEscape(s.Location)}\r\n");
            if (s.Status == SessionStatus.Cancelled) sb.Append("STATUS:CANCELLED\r\n");
            sb.Append("END:VEVENT\r\n");
        }
        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }

    private static string IcsEscape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");

    public static string DescribeLocal(DateTime utc, string timeZoneId)
        => $"{ZonedTime.ToLocal(utc, timeZoneId):ddd d MMM yyyy, HH:mm} ({timeZoneId})";
}
