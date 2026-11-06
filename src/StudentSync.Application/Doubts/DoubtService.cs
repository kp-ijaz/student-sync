using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Application.Content;
using StudentSync.Contracts;
using StudentSync.Domain.Common;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Content;
using StudentSync.Domain.Doubts;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Doubts;

public sealed class DoubtService(
    IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, AuditService audit, NotificationService notify, FileService files)
{
    public static Expression<Func<Doubt, DoubtListItemDto>> ListProjection(IAppDbContext db) => d => new DoubtListItemDto(
        d.Id, d.Title, d.Status, d.Priority, d.Student!.User!.FullName,
        db.Subjects.Where(s => s.Id == d.SubjectId).Select(s => s.Name).FirstOrDefault(),
        d.AssignedTeacher != null ? d.AssignedTeacher.User!.FullName : null,
        d.SessionId, d.FollowUpSessionId,
        db.Sessions.Where(s => s.Id == d.FollowUpSessionId).Select(s => (DateTime?)s.StartUtc).FirstOrDefault(),
        d.CreatedAtUtc, d.LastActivityAtUtc, d.Messages.Count);

    /// <summary>Doubts the current user may read.</summary>
    private async Task<IQueryable<Doubt>> VisibleAsync(CancellationToken ct)
    {
        var q = db.Doubts.AsQueryable();
        if (access.IsAdmin) return q;
        if (access.IsTeacher)
        {
            var me = user.UserId;
            var taught = access.StudentsTaughtBy(me);
            return q.Where(d => d.AssignedTeacherId == me
                                || (d.AssignedTeacherId == null && taught.Contains(d.StudentId))
                                || db.Sessions.Any(s => s.Id == d.SessionId && s.TeacherId == me));
        }
        if (access.IsStudent) return q.Where(d => d.StudentId == user.UserId);
        if (access.IsParent)
        {
            var children = await access.ChildIdsAsync(ct);
            return q.Where(d => children.Contains(d.StudentId));
        }
        return q.Where(_ => false);
    }

    private bool CanAnswer(Doubt d) => user.Has(Permissions.DoubtsAnswer) && (access.IsAdmin || d.AssignedTeacherId == user.UserId || d.AssignedTeacherId == null);

    public async Task<PagedResult<DoubtListItemDto>> ListAsync(DoubtQuery q, CancellationToken ct)
    {
        var doubts = await VisibleAsync(ct);
        if (q.Status is { } status) doubts = doubts.Where(d => d.Status == status);
        if (q.UnresolvedOnly == true) doubts = doubts.Where(d => d.Status != DoubtStatus.Resolved);
        if (q.SubjectId is { } subjectId) doubts = doubts.Where(d => d.SubjectId == subjectId);
        if (q.SessionId is { } sessionId) doubts = doubts.Where(d => d.SessionId == sessionId || d.FollowUpSessionId == sessionId);
        if (q.AssignedToMe == true) doubts = doubts.Where(d => d.AssignedTeacherId == user.UserId);
        if (!string.IsNullOrWhiteSpace(q.Search)) doubts = doubts.Where(d => d.Title.Contains(q.Search) || d.Description.Contains(q.Search));
        var total = await doubts.CountAsync(ct);
        var items = await doubts.OrderByDescending(d => d.Priority).ThenByDescending(d => d.LastActivityAtUtc)
            .Skip((q.SafePage - 1) * q.SafePageSize).Take(q.SafePageSize).Select(ListProjection(db)).ToListAsync(ct);
        return new PagedResult<DoubtListItemDto>(items, q.SafePage, q.SafePageSize, total);
    }

    public async Task<DoubtDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var d = await (await VisibleAsync(ct)).Include(x => x.Student!.User).Include(x => x.AssignedTeacher!.User)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Doubt");
        var messages = await db.DoubtMessages.Where(m => m.DoubtId == id).OrderBy(m => m.CreatedAtUtc)
            .Select(m => new
            {
                m.Id,
                m.IsTeacherReply,
                m.Body,
                m.CreatedAtUtc,
                Author = db.Users.Where(u => u.Id == m.AuthorUserId).Select(u => u.FullName).FirstOrDefault() ?? "Unknown",
            }).ToListAsync(ct);
        var attachments = await db.DoubtAttachments.Where(a => a.DoubtId == id)
            .Join(db.Files, a => a.FileId, f => f.Id, (a, f) => new { a.MessageId, File = new FileDto(f.Id, f.FileName, f.ContentType, f.SizeBytes) })
            .ToListAsync(ct);
        var history = await db.DoubtStatusChanges.Where(h => h.DoubtId == id).OrderBy(h => h.ChangedAtUtc)
            .Select(h => new DoubtHistoryDto(h.ChangedAtUtc, h.FromStatus, h.ToStatus,
                db.Users.Where(u => u.Id == h.ChangedByUserId).Select(u => u.FullName).FirstOrDefault() ?? "System", h.Note))
            .ToListAsync(ct);
        var subjectName = d.SubjectId is null ? null : await db.Subjects.Where(s => s.Id == d.SubjectId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        var session = d.SessionId is null ? null : await db.Sessions.Where(s => s.Id == d.SessionId).Select(s => s.Title).FirstOrDefaultAsync(ct);
        var followUp = d.FollowUpSessionId is null ? null : await db.Sessions.Where(s => s.Id == d.FollowUpSessionId)
            .Select(s => new { s.Title, s.StartUtc }).FirstOrDefaultAsync(ct);

        return new DoubtDetailDto(d.Id, d.Title, d.Description, d.Status, d.Priority, d.IsPrivate, d.StudentId, d.Student!.User!.FullName,
            d.SubjectId, subjectName, d.ChapterId, d.SessionId, session, d.RecordingId, d.RecordingTimestampSeconds,
            d.AssignedTeacherId, d.AssignedTeacher?.User?.FullName, d.FollowUpSessionId, followUp?.Title, followUp?.StartUtc,
            messages.Select(m => new DoubtMessageDto(m.Id, m.Author, m.IsTeacherReply, m.Body, m.CreatedAtUtc,
                attachments.Where(a => a.MessageId == m.Id).Select(a => a.File).ToList())).ToList(),
            history, attachments.Where(a => a.MessageId == null).Select(a => a.File).ToList(),
            CanAnswer(d), access.IsStudent && d.StudentId == user.UserId, RowVersions.Encode(d.RowVersion));
    }

    public async Task<Guid> CreateAsync(CreateDoubtRequest r, CancellationToken ct)
    {
        access.Require(Permissions.DoubtsAsk);
        var title = Text.Required(r.Title, "title", 200);
        var description = Text.Required(r.Description, "description", 8000);
        if (r.SubjectId is { } subjectId && !await db.Subjects.AnyAsync(s => s.Id == subjectId, ct)) throw RequestValidationException.For("subjectId", "Subject not found.");
        if (r.ChapterId is { } chapterId && !await db.Chapters.AnyAsync(c => c.Id == chapterId && (r.SubjectId == null || c.SubjectId == r.SubjectId), ct))
            throw RequestValidationException.For("chapterId", "Chapter not found for this subject.");

        LearningSession? session = null;
        if (r.SessionId is { } sessionId) session = await access.GetVisibleSessionAsync(sessionId, ct);
        if (r.RecordingId is { } recordingId)
        {
            var rec = await db.Recordings.FirstOrDefaultAsync(x => x.Id == recordingId && x.Status == RecordingStatus.Available, ct) ?? throw new NotFoundException("Recording");
            session ??= await access.GetVisibleSessionAsync(rec.SessionId, ct);
            if (rec.SessionId != session.Id) throw RequestValidationException.For("recordingId", "The recording belongs to a different session.");
        }
        if (r.RecordingTimestampSeconds is < 0) throw RequestValidationException.For("recordingTimestampSeconds", "Invalid timestamp.");

        // Route to the session's teacher, else a teacher assigned to the subject who already teaches this student.
        Guid? teacherId = session?.TeacherId;
        if (teacherId is null && r.SubjectId is { } sid)
        {
            var taughtBy = await db.TeacherSubjects.Where(t => t.SubjectId == sid).Select(t => t.TeacherId).ToListAsync(ct);
            foreach (var candidate in taughtBy)
                if (await access.TeacherTeachesStudentAsync(candidate, user.UserId, ct)) { teacherId = candidate; break; }
        }

        var now = clock.UtcNow;
        var doubt = new Doubt
        {
            StudentId = user.UserId,
            Title = title,
            Description = description,
            SubjectId = r.SubjectId ?? session?.SubjectId,
            ChapterId = r.ChapterId,
            SessionId = session?.Id,
            RecordingId = r.RecordingId,
            RecordingTimestampSeconds = r.RecordingTimestampSeconds,
            IsPrivate = r.IsPrivate,
            AssignedTeacherId = teacherId,
            Status = teacherId is null ? DoubtStatus.New : DoubtStatus.Assigned,
            LastActivityAtUtc = now,
        };
        db.Doubts.Add(doubt);
        foreach (var fileId in await files.ClaimAsync(r.FileIds, FilePurpose.DoubtAttachment, ct))
            db.DoubtAttachments.Add(new DoubtAttachment { DoubtId = doubt.Id, FileId = fileId });
        if (teacherId is not null)
            db.DoubtStatusChanges.Add(new DoubtStatusChange { DoubtId = doubt.Id, FromStatus = DoubtStatus.New, ToStatus = DoubtStatus.Assigned, ChangedByUserId = user.UserId, ChangedAtUtc = now, Note = "Routed automatically" });
        audit.Record("doubt.created", "Doubt", doubt.Id, title);
        if (teacherId is { } t)
            await notify.NotifyAsync([t], NotificationType.DoubtAssigned, "New doubt to answer", title, $"/doubts/{doubt.Id}", $"doubt-assigned:{doubt.Id}:{t}", ct);
        await db.SaveChangesAsync(ct);
        return doubt.Id;
    }

    public async Task ReplyAsync(Guid id, ReplyDoubtRequest r, CancellationToken ct)
    {
        var d = await (await VisibleAsync(ct)).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Doubt");
        var body = Text.Required(r.Body, "body", 8000);
        var isOwner = access.IsStudent && d.StudentId == user.UserId;
        var isAnswerer = CanAnswer(d);
        if (!isOwner && !isAnswerer) throw new ForbiddenException();
        var now = clock.UtcNow;

        var message = new DoubtMessage { DoubtId = d.Id, AuthorUserId = user.UserId, Body = body, IsTeacherReply = isAnswerer && !isOwner };
        db.DoubtMessages.Add(message);
        foreach (var fileId in await files.ClaimAsync(r.FileIds, FilePurpose.DoubtAttachment, ct))
            db.DoubtAttachments.Add(new DoubtAttachment { DoubtId = d.Id, MessageId = message.Id, FileId = fileId });
        d.LastActivityAtUtc = now;

        if (message.IsTeacherReply)
        {
            if (d.AssignedTeacherId is null && access.IsTeacher) d.AssignedTeacherId = user.UserId;
            if (r.MarkAnswered && d.Status != DoubtStatus.Answered)
                db.DoubtStatusChanges.Add(d.TransitionTo(DoubtStatus.Answered, user.UserId, now));
            else if (d.Status is DoubtStatus.New or DoubtStatus.Assigned or DoubtStatus.Reopened)
                db.DoubtStatusChanges.Add(d.TransitionTo(DoubtStatus.InProgress, user.UserId, now));
            await notify.NotifyAsync([d.StudentId], NotificationType.DoubtReplied, "Your teacher replied", d.Title, $"/doubts/{d.Id}", $"doubt-reply:{message.Id}", ct);
        }
        else
        {
            // A follow-up question from the student reopens an answered doubt.
            if (d.Status is DoubtStatus.Answered or DoubtStatus.PendingConfirmation or DoubtStatus.Resolved)
                db.DoubtStatusChanges.Add(d.TransitionTo(DoubtStatus.Reopened, user.UserId, now, "Follow-up question"));
            if (d.AssignedTeacherId is { } t)
                await notify.NotifyAsync([t], NotificationType.DoubtReplied, "Follow-up on a doubt", d.Title, $"/doubts/{d.Id}", $"doubt-reply:{message.Id}", ct);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Guid id, UpdateDoubtRequest r, CancellationToken ct)
    {
        var d = await (await VisibleAsync(ct)).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Doubt");
        if (!CanAnswer(d)) throw new ForbiddenException();
        RowVersions.Apply(db, d, r.RowVersion);
        var now = clock.UtcNow;

        if (r.AssignedTeacherId != d.AssignedTeacherId)
        {
            if (!access.IsAdmin && r.AssignedTeacherId != user.UserId) throw new ForbiddenException("Only administrators can assign doubts to other teachers.");
            if (r.AssignedTeacherId is { } tid)
            {
                if (!await db.Teachers.AnyAsync(t => t.Id == tid, ct)) throw RequestValidationException.For("assignedTeacherId", "Teacher not found.");
                if (d.Status is DoubtStatus.New) db.DoubtStatusChanges.Add(d.TransitionTo(DoubtStatus.Assigned, user.UserId, now));
                await notify.NotifyAsync([tid], NotificationType.DoubtAssigned, "Doubt assigned to you", d.Title, $"/doubts/{d.Id}", $"doubt-assigned:{d.Id}:{tid}", ct);
            }
            d.AssignedTeacherId = r.AssignedTeacherId;
        }
        d.Priority = r.Priority;

        if (r.FollowUpSessionId != d.FollowUpSessionId)
        {
            if (r.FollowUpSessionId is { } sid)
            {
                var s = await access.GetManageableSessionAsync(sid, ct, includeParticipants: true);
                if (s.Status != SessionStatus.Scheduled || s.StartUtc < now) throw RequestValidationException.For("followUpSessionId", "Choose an upcoming scheduled session.");
                if (!s.Participants.Any(p => p.StudentId == d.StudentId && p.Status == ParticipantStatus.Active))
                    throw RequestValidationException.For("followUpSessionId", "The student is not a participant of that session.");
                await AddToAgendaAsync(d, s.Id, ct);
            }
            d.FollowUpSessionId = r.FollowUpSessionId;
        }

        if (r.Status is { } next && next != d.Status)
        {
            if (next is DoubtStatus.Resolved) throw new DomainException("Only the student can confirm a doubt is resolved.", "student_confirmation_required");
            db.DoubtStatusChanges.Add(d.TransitionTo(next, user.UserId, now));
            if (next == DoubtStatus.PendingConfirmation)
                await notify.NotifyAsync([d.StudentId], NotificationType.DoubtConfirmationRequested, "Is your doubt resolved?", d.Title, $"/doubts/{d.Id}", $"doubt-confirm:{d.Id}:{now:yyyyMMddHHmm}", ct);
        }
        d.LastActivityAtUtc = now;
        audit.Record("doubt.updated", "Doubt", d.Id, $"status={d.Status}, priority={d.Priority}");
        await db.SaveChangesAsync(ct);
    }

    public async Task ConfirmAsync(Guid id, ConfirmDoubtRequest r, CancellationToken ct)
    {
        var d = await db.Doubts.FirstOrDefaultAsync(x => x.Id == id && x.StudentId == user.UserId, ct) ?? throw new NotFoundException("Doubt");
        var now = clock.UtcNow;
        db.DoubtStatusChanges.Add(d.TransitionTo(r.Resolved ? DoubtStatus.Resolved : DoubtStatus.Reopened, user.UserId, now, Text.Optional(r.Comment, "comment", 1000)));
        if (!r.Resolved && d.AssignedTeacherId is { } t)
            await notify.NotifyAsync([t], NotificationType.DoubtReplied, "Doubt reopened by student", d.Title, $"/doubts/{d.Id}", $"doubt-reopened:{d.Id}:{now:yyyyMMddHHmm}", ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Teacher marks an agenda item discussed; linked doubts move to PendingConfirmation for the student.</summary>
    public async Task MarkAgendaDiscussedAsync(Guid sessionId, Guid itemId, CancellationToken ct)
    {
        var s = await access.GetManageableSessionAsync(sessionId, ct);
        var item = await db.AgendaItems.FirstOrDefaultAsync(a => a.Id == itemId && a.SessionId == s.Id, ct) ?? throw new NotFoundException("Agenda item");
        if (item.IsDiscussed) return;
        item.IsDiscussed = true;
        if (item.DoubtId is { } doubtId)
        {
            var d = await db.Doubts.FirstAsync(x => x.Id == doubtId, ct);
            if (Doubt.CanTransition(d.Status, DoubtStatus.PendingConfirmation))
            {
                db.DoubtStatusChanges.Add(d.TransitionTo(DoubtStatus.PendingConfirmation, user.UserId, clock.UtcNow, $"Discussed in '{s.Title}'"));
                await notify.NotifyAsync([d.StudentId], NotificationType.DoubtConfirmationRequested, "Your doubt was discussed in class",
                    $"Was '{d.Title}' resolved? Please confirm.", $"/doubts/{d.Id}", $"doubt-discussed:{d.Id}:{s.Id}", ct);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task AddToAgendaAsync(Doubt d, Guid sessionId, CancellationToken ct)
    {
        if (await db.AgendaItems.AnyAsync(a => a.SessionId == sessionId && a.DoubtId == d.Id, ct)) return;
        var order = await db.AgendaItems.Where(a => a.SessionId == sessionId).Select(a => (int?)a.SortOrder).MaxAsync(ct) ?? 0;
        db.AgendaItems.Add(new SessionAgendaItem { TenantId = d.TenantId, SessionId = sessionId, Kind = AgendaItemKind.Doubt, DoubtId = d.Id, Title = $"Doubt: {d.Title}", SortOrder = order + 1 });
    }

    /// <summary>
    /// Automatic follow-up: carries each unresolved doubt to the student's next relevant session, adds it to the
    /// draft agenda and notifies student and teacher once (dedupe keys prevent repeat reminders).
    /// Runs inside a tenant scope; returns the number of doubts scheduled.
    /// </summary>
    public async Task<int> RunFollowUpAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var candidates = await db.Doubts
            .Where(d => (d.Status == DoubtStatus.New || d.Status == DoubtStatus.Assigned || d.Status == DoubtStatus.InProgress || d.Status == DoubtStatus.Reopened)
                        && (d.FollowUpSessionId == null || db.Sessions.Any(s => s.Id == d.FollowUpSessionId && (s.Status == SessionStatus.Cancelled || s.EndUtc < now))))
            .OrderBy(d => d.CreatedAtUtc).Take(500).ToListAsync(ct);
        var scheduled = 0;
        foreach (var d in candidates)
        {
            var next = await db.Sessions
                .Where(s => s.Status == SessionStatus.Scheduled && s.StartUtc > now
                            && s.Participants.Any(p => p.StudentId == d.StudentId && p.Status == ParticipantStatus.Active)
                            && (d.AssignedTeacherId == null ? (d.SubjectId == null || s.SubjectId == d.SubjectId) : s.TeacherId == d.AssignedTeacherId))
                .OrderBy(s => d.SubjectId != null && s.SubjectId == d.SubjectId ? 0 : 1).ThenBy(s => s.StartUtc)
                .Select(s => new { s.Id, s.Title, s.TeacherId, s.StartUtc, s.TimeZoneId })
                .FirstOrDefaultAsync(ct);
            if (next is null) continue;
            d.FollowUpSessionId = next.Id;
            await AddToAgendaAsync(d, next.Id, ct);
            var when = Scheduling.SchedulingService.DescribeLocal(next.StartUtc, next.TimeZoneId);
            await notify.NotifyAsync([d.StudentId], NotificationType.DoubtFollowUp, "Your open doubt will be discussed",
                $"'{d.Title}' is on the agenda for {next.Title} ({when}).", $"/doubts/{d.Id}", $"doubt-followup:{d.Id}:{next.Id}", ct);
            await notify.NotifyAsync([next.TeacherId], NotificationType.DoubtFollowUp, "Unresolved doubt added to your class",
                $"'{d.Title}' was added to the agenda for {next.Title}.", $"/sessions/{next.Id}/prepare", $"doubt-followup:{d.Id}:{next.Id}", ct);
            scheduled++;
        }
        await db.SaveChangesAsync(ct);
        return scheduled;
    }
}
