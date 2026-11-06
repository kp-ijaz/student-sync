using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Application.Content;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Assignments;
using StudentSync.Domain.Common;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Content;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Assignments;

public sealed class AssignmentService(
    IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, AuditService audit, NotificationService notify, FileService files)
{
    private async Task<IQueryable<Assignment>> VisibleAsync(CancellationToken ct)
    {
        if (access.IsAdmin) return db.Assignments;
        if (access.IsTeacher) return db.Assignments.Where(a => a.TeacherId == user.UserId);
        var students = access.IsParent ? await access.ChildIdsAsync(ct) : access.IsStudent ? [user.UserId] : [];
        return db.Assignments.Where(a => a.Status != AssignmentStatus.Draft && a.Recipients.Any(r => students.Contains(r.StudentId)));
    }

    public async Task<PagedResult<AssignmentListItemDto>> ListAsync(AssignmentQuery q, CancellationToken ct)
    {
        var items = await VisibleAsync(ct);
        if (q.Status is { } status) items = items.Where(a => a.Status == status);
        if (q.SessionId is { } sid) items = items.Where(a => a.SessionId == sid);
        Guid? focus = access.IsStudent ? user.UserId : null;
        if (q.StudentId is { } studentId)
        {
            await access.EnsureCanViewStudentAsync(studentId, ct);
            items = items.Where(a => a.Recipients.Any(r => r.StudentId == studentId));
            focus = studentId;
        }
        if (!string.IsNullOrWhiteSpace(q.Search)) items = items.Where(a => a.Title.Contains(q.Search));
        var total = await items.CountAsync(ct);
        var page = await Project(items.OrderByDescending(a => a.DueAtUtc), focus).Skip((q.SafePage - 1) * q.SafePageSize).Take(q.SafePageSize).ToListAsync(ct);
        return new PagedResult<AssignmentListItemDto>(page, q.SafePage, q.SafePageSize, total);
    }

    public IQueryable<AssignmentListItemDto> Project(IQueryable<Assignment> q, Guid? studentId) => q.Select(a => new AssignmentListItemDto(
        a.Id, a.Title, a.Status, a.DueAtUtc, a.MaxMarks, a.Teacher!.User!.FullName,
        db.Sessions.Where(s => s.Id == a.SessionId).Select(s => s.Title).FirstOrDefault(),
        a.Recipients.Count,
        db.Submissions.Count(s => s.AssignmentId == a.Id && s.Status != SubmissionStatus.Draft),
        db.Submissions.Count(s => s.AssignmentId == a.Id && s.Status == SubmissionStatus.Graded),
        studentId == null ? null : db.Submissions.Where(s => s.AssignmentId == a.Id && s.StudentId == studentId).Select(s => (SubmissionStatus?)s.Status).FirstOrDefault(),
        studentId == null ? null : db.Submissions.Where(s => s.AssignmentId == a.Id && s.StudentId == studentId && s.Status == SubmissionStatus.Graded).Select(s => s.Marks).FirstOrDefault()));

    public async Task<AssignmentDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var a = await (await VisibleAsync(ct)).Include(x => x.Teacher!.User).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Assignment");
        var attachments = await db.AssignmentAttachments.Where(x => x.AssignmentId == id).Join(db.Files, x => x.FileId, f => f.Id, (x, f) => new FileDto(f.Id, f.FileName, f.ContentType, f.SizeBytes)).ToListAsync(ct);
        var canManage = access.IsAdmin || a.TeacherId == user.UserId;
        var recipients = canManage
            ? await db.AssignmentRecipients.Where(r => r.AssignmentId == id).Select(r => new LookupItem(r.StudentId, r.Student!.User!.FullName, null)).ToListAsync(ct)
            : [];
        var sessionTitle = a.SessionId is null ? null : await db.Sessions.Where(s => s.Id == a.SessionId).Select(s => s.Title).FirstOrDefaultAsync(ct);
        return new AssignmentDetailDto(a.Id, a.Title, a.InstructionsMarkdown, a.Status, a.DueAtUtc, a.MaxMarks, a.AllowLateSubmission, a.AllowResubmission,
            a.TeacherId, a.Teacher!.User!.FullName, a.SessionId, sessionTitle, a.BatchId, a.SubjectId, attachments, recipients, canManage, RowVersions.Encode(a.RowVersion));
    }

    /// <summary>Resolves recipients: explicit students, else the linked session's participants, else the batch roster.</summary>
    private async Task<List<Guid>> ResolveRecipientsAsync(SaveAssignmentRequest r, Guid teacherId, CancellationToken ct)
    {
        List<Guid> ids;
        if (r.StudentIds is { Count: > 0 }) ids = r.StudentIds.Distinct().ToList();
        else if (r.SessionId is { } sid) ids = await db.SessionParticipants.Where(p => p.SessionId == sid && p.Status == ParticipantStatus.Active).Select(p => p.StudentId).ToListAsync(ct);
        else if (r.BatchId is { } bid) ids = await db.BatchEnrolments.Where(e => e.BatchId == bid && e.Status == EnrolmentStatus.Active).Select(e => e.StudentId).ToListAsync(ct);
        else ids = [];
        if (ids.Count > 500) throw RequestValidationException.For("studentIds", "Too many recipients.");
        var valid = await db.Students.CountAsync(s => ids.Contains(s.Id), ct);
        if (valid != ids.Count) throw RequestValidationException.For("studentIds", "One or more students were not found.");
        if (!access.IsAdmin)
        {
            var taught = await access.StudentsTaughtBy(teacherId).Where(x => ids.Contains(x)).CountAsync(ct);
            if (taught != ids.Count) throw RequestValidationException.For("studentIds", "You can only assign work to students you teach.");
        }
        return ids;
    }

    private async Task ValidateLinksAsync(SaveAssignmentRequest r, CancellationToken ct)
    {
        if (r.SessionId is { } sid) await access.GetManageableSessionAsync(sid, ct);
        if (r.BatchId is { } bid && !await db.Batches.AnyAsync(b => b.Id == bid && (access.IsAdmin || b.TeacherId == user.UserId), ct))
            throw RequestValidationException.For("batchId", "Batch not found.");
        if (r.SubjectId is { } subj && !await db.Subjects.AnyAsync(s => s.Id == subj, ct)) throw RequestValidationException.For("subjectId", "Subject not found.");
        if (r.MaxMarks is <= 0 or > 1000) throw RequestValidationException.For("maxMarks", "Maximum marks must be between 1 and 1000.");
    }

    public async Task<Guid> CreateAsync(SaveAssignmentRequest r, CancellationToken ct)
    {
        access.Require(Permissions.AssignmentsManage);
        await ValidateLinksAsync(r, ct);
        var teacherId = access.IsTeacher ? user.UserId
            : r.SessionId is { } sid ? (await db.Sessions.FirstAsync(s => s.Id == sid, ct)).TeacherId
            : r.BatchId is { } bid ? (await db.Batches.FirstAsync(b => b.Id == bid, ct)).TeacherId
            : throw RequestValidationException.For("sessionId", "Administrators must link the assignment to a session or batch.");
        var a = new Assignment
        {
            Title = Text.Required(r.Title, "title", 200),
            InstructionsMarkdown = Text.Required(r.InstructionsMarkdown, "instructionsMarkdown", 50_000),
            TeacherId = teacherId,
            SessionId = r.SessionId,
            BatchId = r.BatchId,
            SubjectId = r.SubjectId,
            DueAtUtc = DateTime.SpecifyKind(r.DueAtUtc, DateTimeKind.Utc),
            MaxMarks = r.MaxMarks,
            AllowLateSubmission = r.AllowLateSubmission,
            AllowResubmission = r.AllowResubmission,
        };
        foreach (var sid2 in await ResolveRecipientsAsync(r, teacherId, ct)) a.Recipients.Add(new AssignmentRecipient { StudentId = sid2 });
        db.Assignments.Add(a);
        audit.Record("assignment.created", "Assignment", a.Id, a.Title);
        await db.SaveChangesAsync(ct);
        return a.Id;
    }

    public async Task UpdateAsync(Guid id, SaveAssignmentRequest r, CancellationToken ct)
    {
        var a = await GetManageableAsync(id, ct);
        RowVersions.Apply(db, a, r.RowVersion);
        await ValidateLinksAsync(r, ct);
        a.Title = Text.Required(r.Title, "title", 200);
        a.InstructionsMarkdown = Text.Required(r.InstructionsMarkdown, "instructionsMarkdown", 50_000);
        a.DueAtUtc = DateTime.SpecifyKind(r.DueAtUtc, DateTimeKind.Utc);
        a.AllowLateSubmission = r.AllowLateSubmission;
        a.AllowResubmission = r.AllowResubmission;
        if (a.Status == AssignmentStatus.Draft)
        {
            a.MaxMarks = r.MaxMarks;
            a.SessionId = r.SessionId; a.BatchId = r.BatchId; a.SubjectId = r.SubjectId;
            var ids = await ResolveRecipientsAsync(r, a.TeacherId, ct);
            a.Recipients.RemoveAll(x => !ids.Contains(x.StudentId));
            foreach (var sid in ids.Where(x => a.Recipients.All(y => y.StudentId != x))) a.Recipients.Add(new AssignmentRecipient { StudentId = sid });
        }
        else if (r.MaxMarks != a.MaxMarks) throw new DomainException("Maximum marks cannot change after publishing.", "assignment_published");
        audit.Record("assignment.updated", "Assignment", a.Id, a.Title);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Assignment> GetManageableAsync(Guid id, CancellationToken ct)
    {
        access.Require(Permissions.AssignmentsManage);
        var a = await db.Assignments.Include(x => x.Recipients).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Assignment");
        if (!access.IsAdmin && a.TeacherId != user.UserId) throw new NotFoundException("Assignment");
        return a;
    }

    public async Task AttachAsync(Guid id, Stream content, string fileName, string contentType, long length, CancellationToken ct)
    {
        var a = await GetManageableAsync(id, ct);
        if (await db.AssignmentAttachments.CountAsync(x => x.AssignmentId == a.Id, ct) >= 10) throw RequestValidationException.For("file", "At most 10 attachments.");
        var file = await files.UploadAsync(content, fileName, contentType, length, FilePurpose.AssignmentAttachment, ct);
        db.AssignmentAttachments.Add(new AssignmentAttachment { AssignmentId = a.Id, FileId = file.Id });
        await db.SaveChangesAsync(ct);
    }

    public async Task PublishAsync(Guid id, CancellationToken ct)
    {
        var a = await GetManageableAsync(id, ct);
        a.Publish(clock.UtcNow);
        audit.Record("assignment.published", "Assignment", a.Id, a.Title);
        var recipients = await notify.WithGuardiansAsync(a.Recipients.Select(r => r.StudentId), ct);
        await notify.NotifyAsync(recipients, NotificationType.AssignmentPublished, $"New assignment: {a.Title}", $"Due {a.DueAtUtc:u}", $"/assignments/{a.Id}", $"assignment-published:{a.Id}", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task CloseAsync(Guid id, CancellationToken ct)
    {
        var a = await GetManageableAsync(id, ct);
        if (a.Status != AssignmentStatus.Published) throw new DomainException("Only published assignments can be closed.", "invalid_assignment_state");
        a.Status = AssignmentStatus.Closed;
        audit.Record("assignment.closed", "Assignment", a.Id, a.Title);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Submissions ----------------

    private IQueryable<SubmissionDto> ProjectSubmissions(IQueryable<AssignmentSubmission> q) => q.Select(s => new SubmissionDto(
        s.Id, s.AssignmentId, s.Assignment!.Title, s.StudentId, s.Student!.User!.FullName, s.Status, s.TextAnswer, s.SubmittedAtUtc, s.IsLate,
        s.AttemptNumber, s.Marks, s.Assignment.MaxMarks, s.Feedback, s.GradedAtUtc,
        db.SubmissionFiles.Where(f => f.SubmissionId == s.Id).Join(db.Files, f => f.FileId, f => f.Id, (x, f) => new FileDto(f.Id, f.FileName, f.ContentType, f.SizeBytes)).ToList(),
        Convert.ToBase64String(s.RowVersion)));

    public async Task<IReadOnlyList<SubmissionDto>> SubmissionsAsync(Guid assignmentId, CancellationToken ct)
    {
        var a = await GetManageableAsync(assignmentId, ct);
        return await ProjectSubmissions(db.Submissions.Where(s => s.AssignmentId == a.Id && s.Status != SubmissionStatus.Draft).OrderBy(s => s.SubmittedAtUtc)).ToListAsync(ct);
    }

    /// <summary>The current student's own submission (or a parent's view of a child's).</summary>
    public async Task<SubmissionDto?> MySubmissionAsync(Guid assignmentId, Guid? studentId, CancellationToken ct)
    {
        var a = await (await VisibleAsync(ct)).FirstOrDefaultAsync(x => x.Id == assignmentId, ct) ?? throw new NotFoundException("Assignment");
        var target = access.IsStudent ? user.UserId : studentId ?? throw RequestValidationException.For("studentId", "Choose a student.");
        if (!access.IsStudent) await access.EnsureCanViewStudentAsync(target, ct);
        var q = db.Submissions.Where(s => s.AssignmentId == a.Id && s.StudentId == target);
        if (!access.IsStudent) q = q.Where(s => s.Status != SubmissionStatus.Draft);
        return await ProjectSubmissions(q).FirstOrDefaultAsync(ct);
    }

    public async Task<Guid> SaveSubmissionAsync(Guid assignmentId, SaveSubmissionRequest r, CancellationToken ct)
    {
        access.Require(Permissions.AssignmentsSubmit);
        var a = await db.Assignments.FirstOrDefaultAsync(x => x.Id == assignmentId && x.Status != AssignmentStatus.Draft
                    && x.Recipients.Any(rc => rc.StudentId == user.UserId), ct) ?? throw new NotFoundException("Assignment");
        var sub = await db.Submissions.Include(s => s.Files).FirstOrDefaultAsync(s => s.AssignmentId == a.Id && s.StudentId == user.UserId, ct);
        if (sub is null) db.Submissions.Add(sub = new AssignmentSubmission { AssignmentId = a.Id, StudentId = user.UserId });
        else RowVersions.Apply(db, sub, r.RowVersion);
        if (sub.Status == SubmissionStatus.Graded) throw new DomainException("This submission has been graded and can no longer be changed.", "submission_locked");
        if (sub.Status == SubmissionStatus.Submitted && !a.AllowResubmission) throw new DomainException("Resubmission is not allowed for this assignment.", "submission_locked");

        sub.TextAnswer = Text.Optional(r.TextAnswer, "textAnswer", 50_000);
        if (r.FileIds is not null)
        {
            var keep = await files.ClaimAsync(r.FileIds.Where(id => sub.Files.All(f => f.FileId != id)), FilePurpose.Submission, ct);
            sub.Files.RemoveAll(f => !r.FileIds.Contains(f.FileId));
            foreach (var fid in keep) sub.Files.Add(new SubmissionFile { FileId = fid });
        }
        if (r.Submit)
        {
            sub.Submit(a, clock.UtcNow);
            audit.Record("submission.submitted", "AssignmentSubmission", sub.Id, $"{a.Title} attempt {sub.AttemptNumber}{(sub.IsLate ? " (late)" : "")}");
            await notify.NotifyAsync([a.TeacherId], NotificationType.SubmissionReceived, $"Submission received: {a.Title}", sub.IsLate ? "Submitted after the deadline." : "Ready to grade.",
                $"/assignments/{a.Id}", $"submission:{sub.Id}:{sub.AttemptNumber + 1}", ct);
        }
        await db.SaveChangesAsync(ct);
        return sub.Id;
    }

    public async Task GradeAsync(Guid submissionId, GradeSubmissionRequest r, CancellationToken ct)
    {
        var sub = await db.Submissions.Include(s => s.Assignment).FirstOrDefaultAsync(s => s.Id == submissionId, ct) ?? throw new NotFoundException("Submission");
        var a = await GetManageableAsync(sub.AssignmentId, ct);
        RowVersions.Apply(db, sub, r.RowVersion);
        var previous = sub.Marks;
        sub.Grade(r.Marks, a.MaxMarks, Text.Optional(r.Feedback, "feedback", 10_000), r.ReturnForCorrection, user.UserId, clock.UtcNow);
        audit.Record("submission.graded", "AssignmentSubmission", sub.Id, $"{previous?.ToString() ?? "none"} -> {r.Marks}/{a.MaxMarks}{(r.ReturnForCorrection ? " (returned)" : "")}");
        await notify.NotifyAsync(await notify.WithGuardiansAsync([sub.StudentId], ct), NotificationType.AssignmentGraded,
            r.ReturnForCorrection ? $"Corrections requested: {a.Title}" : $"Graded: {a.Title}", $"{r.Marks}/{a.MaxMarks}",
            $"/assignments/{a.Id}", $"graded:{sub.Id}:{clock.UtcNow:yyyyMMddHHmmss}", ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Daily: reminds recipients who have not submitted, 24 hours before the deadline. Runs per tenant.</summary>
    public async Task<int> SendDueRemindersAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await db.Assignments.Where(a => a.Status == AssignmentStatus.Published && a.DueAtUtc > now && a.DueAtUtc <= now.AddHours(24))
            .Select(a => new
            {
                a.Id,
                a.Title,
                a.DueAtUtc,
                Pending = a.Recipients.Where(r => !db.Submissions.Any(s => s.AssignmentId == a.Id && s.StudentId == r.StudentId && s.Status != SubmissionStatus.Draft)).Select(r => r.StudentId).ToList(),
            }).ToListAsync(ct);
        foreach (var a in due)
            await notify.NotifyAsync(a.Pending, NotificationType.AssignmentDueSoon, $"Due soon: {a.Title}", $"Due {a.DueAtUtc:u}", $"/assignments/{a.Id}", $"assignment-due:{a.Id}", ct);
        await db.SaveChangesAsync(ct);
        return due.Sum(a => a.Pending.Count);
    }
}
