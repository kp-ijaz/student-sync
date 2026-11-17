using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Common;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Scheduling;

/// <summary>
/// The student's teachers and class change requests (owner request 2026-11-17). A student sees the teachers they have
/// classes with and can ask to postpone or reschedule one of their own upcoming classes; the request goes to that class's
/// teacher (admins may answer too). Accepting moves the class through <see cref="SchedulingService.RescheduleAsync"/>, so
/// clash, hours and notification rules all apply; declining tells the student why. Students see only their own requests,
/// teachers only those for their classes.
/// </summary>
public sealed class ClassChangeService(IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, AuditService audit,
    NotificationService notify, SchedulingService scheduling, PhotoUrls photos)
{
    public async Task<List<MyTeacherDto>> MyTeachersAsync(CancellationToken ct)
    {
        if (!access.IsStudent) throw new ForbiddenException();
        var me = user.UserId;
        var now = clock.UtcNow;
        var rows = await (from p in db.SessionParticipants
                          where p.StudentId == me && p.Status == ParticipantStatus.Active
                          join s in db.Sessions on p.SessionId equals s.Id
                          where s.Status != SessionStatus.Cancelled
                          select new
                          {
                              s.Id,
                              s.Title,
                              s.TeacherId,
                              s.StartUtc,
                              s.EndUtc,
                              s.Status,
                              s.TimeZoneId,
                              Subject = s.SubjectId != null ? db.Subjects.Where(x => x.Id == s.SubjectId).Select(x => x.Name).FirstOrDefault() : null,
                          }).ToListAsync(ct);
        if (rows.Count == 0) return [];
        var teacherIds = rows.Select(r => r.TeacherId).Distinct().ToList();
        var teachers = await db.Users.Where(u => teacherIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.PhotoFileId }).ToListAsync(ct);
        var upcomingIds = rows.Where(r => r.Status == SessionStatus.Scheduled && r.EndUtc > now).Select(r => r.Id).ToList();
        var requests = (await Query(db.ClassChangeRequests.Where(r => r.StudentId == me && upcomingIds.Contains(r.SessionId) && r.Status == ClassChangeStatus.Pending))
            .ToListAsync(ct)).ToDictionary(r => r.SessionId);

        return teachers.Select(t =>
        {
            var mine = rows.Where(r => r.TeacherId == t.Id).ToList();
            var upcoming = mine.Where(r => r.Status == SessionStatus.Scheduled && r.EndUtc > now).OrderBy(r => r.StartUtc).Take(10)
                .Select(r => new MyTeacherClassDto(r.Id, r.Title, r.Subject, r.StartUtc, r.EndUtc, r.TimeZoneId, requests.GetValueOrDefault(r.Id))).ToList();
            return new MyTeacherDto(t.Id, t.FullName, photos.For(t.PhotoFileId),
                mine.Select(r => r.Subject).OfType<string>().Distinct().Order().ToList(), mine.Count(r => r.Status == SessionStatus.Completed), upcoming);
        })
        // Teachers with a class coming up first (soonest first), then the others.
        .OrderBy(t => t.Upcoming.Count == 0).ThenBy(t => t.Upcoming.FirstOrDefault()?.StartUtc ?? DateTime.MaxValue).ThenBy(t => t.Name).ToList();
    }

    public async Task<Guid> CreateAsync(CreateClassChangeRequest r, CancellationToken ct)
    {
        if (!access.IsStudent) throw new ForbiddenException("Only students can ask to move their own classes.");
        if (!Enum.TryParse<ClassChangeKind>(r.Kind, true, out var kind)) throw RequestValidationException.For("kind", "Choose postpone or reschedule.");
        var reason = Text.Required(r.Reason, "reason", 500);
        var s = await db.Sessions.Where(x => x.Id == r.SessionId && x.Participants.Any(p => p.StudentId == user.UserId && p.Status == ParticipantStatus.Active))
            .Select(x => new { x.Id, x.Title, x.TeacherId, x.StartUtc, x.EndUtc, x.Status, x.TimeZoneId }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Class");
        if (s.Status != SessionStatus.Scheduled || s.StartUtc <= clock.UtcNow) throw new DomainException("Only upcoming classes can be moved.", "class_not_upcoming");
        if (kind == ClassChangeKind.Reschedule)
        {
            if (r.ProposedDate is not { } d || r.ProposedTime is not { } t) throw RequestValidationException.For("proposedDate", "Choose the new date and time.");
            if (ZonedTime.ToUtc(d, t, s.TimeZoneId).Utc <= clock.UtcNow) throw RequestValidationException.For("proposedDate", "The new time must be in the future.");
            if (d > DateOnly.FromDateTime(clock.UtcNow).AddDays(180)) throw RequestValidationException.For("proposedDate", "Choose a date within the next 6 months.");
        }
        if (await db.ClassChangeRequests.AnyAsync(x => x.SessionId == s.Id && x.StudentId == user.UserId && x.Status == ClassChangeStatus.Pending, ct))
            throw new ConflictException("You already asked to move this class. Wait for your teacher's answer, or withdraw it first.", "request_pending");

        var request = new ClassChangeRequest
        {
            SessionId = s.Id,
            StudentId = user.UserId,
            TeacherId = s.TeacherId,
            Kind = kind,
            ProposedDate = kind == ClassChangeKind.Reschedule ? r.ProposedDate : null,
            ProposedTime = kind == ClassChangeKind.Reschedule ? r.ProposedTime : null,
            TimeZoneId = s.TimeZoneId,
            Reason = reason,
            Status = ClassChangeStatus.Pending,
        };
        db.ClassChangeRequests.Add(request);
        var name = await db.Users.Where(u => u.Id == user.UserId).Select(u => u.FullName).FirstAsync(ct);
        var when = SchedulingService.DescribeLocal(s.StartUtc, s.TimeZoneId);
        var proposal = kind == ClassChangeKind.Reschedule ? $" to {r.ProposedDate:ddd d MMM} at {r.ProposedTime:HH\\:mm}" : "";
        var admins = await db.Users.Where(u => u.TenantId == user.TenantId && u.Role == UserRole.InstitutionAdmin && u.IsActive).Select(u => u.Id).ToListAsync(ct);
        await notify.NotifyAsync(admins.Append(s.TeacherId), NotificationType.ClassChangeRequested,
            $"{name} asks to {(kind == ClassChangeKind.Postpone ? "postpone" : "reschedule")} a class",
            $"{s.Title}, {when}{proposal}. Reason: {reason}", "/class-requests", $"class-change:{request.Id}", ct);
        audit.Record("class_change.requested", "LearningSession", s.Id, $"{kind}{proposal}");
        await db.SaveChangesAsync(ct);
        return request.Id;
    }

    public async Task WithdrawAsync(Guid id, CancellationToken ct)
    {
        var r = await db.ClassChangeRequests.FirstOrDefaultAsync(x => x.Id == id && x.StudentId == user.UserId, ct) ?? throw new NotFoundException("Request");
        if (r.Status != ClassChangeStatus.Pending) throw new ConflictException("This request was already answered.", "request_closed");
        r.Status = ClassChangeStatus.Withdrawn;
        audit.Record("class_change.withdrawn", "LearningSession", r.SessionId, null);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ClassChangeRequestDto>> ListAsync(string? status, CancellationToken ct)
    {
        var q = db.ClassChangeRequests.AsQueryable();
        if (access.IsStudent) q = q.Where(r => r.StudentId == user.UserId);
        else if (access.IsTeacher) q = q.Where(r => r.TeacherId == user.UserId);
        else if (!access.IsAdmin) throw new ForbiddenException();
        if (Enum.TryParse<ClassChangeStatus>(status, true, out var st)) q = q.Where(r => r.Status == st);
        q = q.OrderBy(r => r.Status == ClassChangeStatus.Pending ? 0 : 1).ThenByDescending(r => r.CreatedAtUtc).Take(200);
        return await Query(q).ToListAsync(ct);
    }

    public async Task AnswerAsync(Guid id, AnswerClassChangeRequest a, CancellationToken ct)
    {
        var r = await db.ClassChangeRequests.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Request");
        if (!access.IsAdmin && !(access.IsTeacher && r.TeacherId == user.UserId)) throw new NotFoundException("Request");
        if (r.Status != ClassChangeStatus.Pending) throw new ConflictException("This request was already answered or withdrawn.", "request_closed");
        var s = await db.Sessions.AsNoTracking().FirstAsync(x => x.Id == r.SessionId, ct);
        var note = Text.Optional(a.Note, "note", 500);
        (r.ResponseNote, r.RespondedByUserId, r.RespondedAtUtc) = (note, user.UserId, clock.UtcNow);

        if (!a.Accept)
        {
            r.Status = ClassChangeStatus.Declined;
            await notify.NotifyAsync([r.StudentId], NotificationType.ClassChangeAnswered, $"Your class stays as booked: {s.Title}",
                $"{SchedulingService.DescribeLocal(s.StartUtc, s.TimeZoneId)}.{(note is null ? "" : $" {note}")}", $"/sessions/{s.Id}", $"class-change-answer:{r.Id}", ct);
            audit.Record("class_change.declined", "LearningSession", s.Id, note);
            await db.SaveChangesAsync(ct);
            return;
        }

        if (s.Status != SessionStatus.Scheduled || s.EndUtc <= clock.UtcNow) throw new DomainException("This class has already taken place or was cancelled.", "class_not_upcoming");
        var date = a.NewDate ?? r.ProposedDate ?? throw RequestValidationException.For("newDate", "Choose the new date and time for the class.");
        var time = a.NewTime ?? r.ProposedTime ?? throw RequestValidationException.For("newTime", "Choose the new date and time for the class.");
        var tz = s.TimeZoneId;
        var start = ZonedTime.ToUtc(date, time, tz).Utc;
        if (start <= clock.UtcNow) throw RequestValidationException.For("newDate", "The new time must be in the future.");
        (r.Status, r.NewStartUtc) = (ClassChangeStatus.Accepted, start);
        await notify.NotifyAsync([r.StudentId], NotificationType.ClassChangeAnswered, $"Class moved: {s.Title}",
            $"Now {SchedulingService.DescribeLocal(start, tz)}.{(note is null ? "" : $" {note}")}", $"/sessions/{s.Id}", $"class-change-answer:{r.Id}", ct);
        audit.Record("class_change.accepted", "LearningSession", s.Id, $"{date:yyyy-MM-dd} {time:HH\\:mm}");
        // The request update and its notification are saved together with the reschedule (one SaveChanges inside it);
        // if the new time clashes or does not fit the hours, nothing is saved and the request stays pending.
        await scheduling.RescheduleAsync(s.Id, new RescheduleSessionRequest(tz, date, time, (int)(s.EndUtc - s.StartUtc).TotalMinutes,
            $"Asked by the student: {r.Reason}", RowVersions.Encode(s.RowVersion), a.AcknowledgeConflicts), ct);
    }

    private IQueryable<ClassChangeRequestDto> Query(IQueryable<ClassChangeRequest> requests) =>
        from r in requests
        join s in db.Sessions on r.SessionId equals s.Id
        select new ClassChangeRequestDto(r.Id, r.SessionId, s.Title, s.StartUtc, s.EndUtc, s.TimeZoneId, r.StudentId,
            db.Users.Where(u => u.Id == r.StudentId).Select(u => u.FullName).FirstOrDefault() ?? "Student", r.TeacherId,
            db.Users.Where(u => u.Id == r.TeacherId).Select(u => u.FullName).FirstOrDefault() ?? "Teacher",
            r.Kind == ClassChangeKind.Postpone ? "Postpone" : "Reschedule", r.ProposedDate, r.ProposedTime, r.Reason,
            r.Status == ClassChangeStatus.Pending ? "Pending" : r.Status == ClassChangeStatus.Accepted ? "Accepted" : r.Status == ClassChangeStatus.Declined ? "Declined" : "Withdrawn",
            r.ResponseNote, r.CreatedAtUtc, r.RespondedAtUtc, r.NewStartUtc);
}
