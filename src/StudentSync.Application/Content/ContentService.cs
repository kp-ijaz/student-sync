using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Common;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Content;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Content;

public sealed class ContentService(
    IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, AuditService audit, NotificationService notify, FileService files)
{
    // ---------------- Notes ----------------

    public async Task<IReadOnlyList<NoteDto>> NotesAsync(Guid sessionId, CancellationToken ct)
    {
        var s = await access.GetVisibleSessionAsync(sessionId, ct);
        var q = db.Notes.Where(n => n.SessionId == s.Id);
        if (!access.CanManageSession(s)) q = q.Where(n => n.Status == PublicationStatus.Published && n.Visibility == NoteVisibility.Participants);
        return await ProjectNotes(q.OrderBy(n => n.CreatedAtUtc)).ToListAsync(ct);
    }

    private IQueryable<NoteDto> ProjectNotes(IQueryable<SessionNote> q) => q.Select(n => new NoteDto(
        n.Id, n.SessionId, n.Title, n.BodyMarkdown, n.Visibility, n.Status, n.Source, n.PublishedAtUtc, n.UpdatedAtUtc ?? n.CreatedAtUtc,
        db.Users.Where(u => u.Id == n.AuthorUserId).Select(u => u.FullName).FirstOrDefault() ?? "", n.AvailableOffline, Convert.ToBase64String(n.RowVersion)));

    public async Task<Guid> CreateNoteAsync(Guid sessionId, SaveNoteRequest r, CancellationToken ct)
    {
        access.Require(Permissions.ContentManage);
        var s = await access.GetManageableSessionAsync(sessionId, ct);
        var note = new SessionNote
        {
            SessionId = s.Id,
            AuthorUserId = user.UserId,
            Title = Text.Required(r.Title, "title", 200),
            BodyMarkdown = Text.Required(r.BodyMarkdown, "bodyMarkdown", 200_000),
            Visibility = r.Visibility,
            AvailableOffline = r.AvailableOffline,
        };
        db.Notes.Add(note);
        audit.Record("note.created", "SessionNote", note.Id, note.Title);
        await db.SaveChangesAsync(ct);
        return note.Id;
    }

    public async Task UpdateNoteAsync(Guid noteId, SaveNoteRequest r, CancellationToken ct)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, ct) ?? throw new NotFoundException("Note");
        await access.GetManageableSessionAsync(note.SessionId, ct);
        RowVersions.Apply(db, note, r.RowVersion);
        note.Title = Text.Required(r.Title, "title", 200);
        note.BodyMarkdown = Text.Required(r.BodyMarkdown, "bodyMarkdown", 200_000);
        note.Visibility = r.Visibility;
        note.AvailableOffline = r.AvailableOffline;
        audit.Record("note.updated", "SessionNote", note.Id, note.Title);
        await db.SaveChangesAsync(ct);
    }

    public async Task PublishNoteAsync(Guid noteId, bool publish, CancellationToken ct)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, ct) ?? throw new NotFoundException("Note");
        var s = await access.GetManageableSessionAsync(note.SessionId, ct, includeParticipants: true);
        note.Status = publish ? PublicationStatus.Published : PublicationStatus.Draft;
        note.PublishedAtUtc = publish ? clock.UtcNow : null;
        if (publish && note.Visibility == NoteVisibility.Participants)
        {
            var students = s.Participants.Where(p => p.Status == ParticipantStatus.Active).Select(p => p.StudentId);
            await notify.NotifyAsync(students, NotificationType.NotesPublished, $"New notes: {note.Title}", s.Title, $"/sessions/{s.Id}", $"note-published:{note.Id}", ct);
        }
        audit.Record(publish ? "note.published" : "note.unpublished", "SessionNote", note.Id, note.Title);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteNoteAsync(Guid noteId, CancellationToken ct)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, ct) ?? throw new NotFoundException("Note");
        await access.GetManageableSessionAsync(note.SessionId, ct);
        if (note.AiDraftId is not null && note.Status == PublicationStatus.Published)
            throw new DomainException("Unpublish this AI-derived note before deleting it.", "unpublish_first");
        db.Notes.Remove(note);
        audit.Record("note.deleted", "SessionNote", note.Id, note.Title);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Materials ----------------

    public static async Task<bool> CanViewMaterialAsync(IAppDbContext db, AccessPolicy access, ICurrentUser user, LearningMaterial m, CancellationToken ct)
    {
        if (access.IsAdmin) return true;
        if (m.SessionId is { } sid) return await (await access.VisibleSessionsAsync(ct)).AnyAsync(s => s.Id == sid, ct);
        var students = access.IsParent ? await access.ChildIdsAsync(ct) : access.IsStudent ? [user.UserId] : [];
        if (m.BatchId is { } bid)
            return access.IsTeacher
                ? await db.Batches.AnyAsync(b => b.Id == bid && b.TeacherId == user.UserId, ct)
                : await db.BatchEnrolments.AnyAsync(e => e.BatchId == bid && students.Contains(e.StudentId) && e.Status == EnrolmentStatus.Active, ct);
        if (m.SubjectId is { } subj)
            return access.IsTeacher
                ? await db.TeacherSubjects.AnyAsync(t => t.SubjectId == subj && t.TeacherId == user.UserId, ct)
                : await db.CourseEnrolments.AnyAsync(e => students.Contains(e.StudentId) && e.Status == EnrolmentStatus.Active
                    && db.Subjects.Any(s => s.Id == subj && s.CourseId == e.CourseId), ct);
        return access.IsTeacher && m.CreatedByTeacherOrAdminId == user.UserId;
    }

    private async Task<IQueryable<LearningMaterial>> VisibleMaterialsAsync(CancellationToken ct)
    {
        if (access.IsAdmin) return db.Materials;
        var sessions = (await access.VisibleSessionsAsync(ct)).Select(s => s.Id);
        if (access.IsTeacher)
        {
            var me = user.UserId;
            return db.Materials.Where(m => (m.SessionId != null && sessions.Contains(m.SessionId.Value))
                || (m.BatchId != null && db.Batches.Any(b => b.Id == m.BatchId && b.TeacherId == me))
                || (m.SubjectId != null && m.SessionId == null && m.BatchId == null && db.TeacherSubjects.Any(t => t.SubjectId == m.SubjectId && t.TeacherId == me))
                || m.CreatedByTeacherOrAdminId == me);
        }
        var students = access.IsParent ? await access.ChildIdsAsync(ct) : [user.UserId];
        return db.Materials.Where(m => (m.SessionId != null && sessions.Contains(m.SessionId.Value))
            || (m.SessionId == null && m.BatchId != null && db.BatchEnrolments.Any(e => e.BatchId == m.BatchId && students.Contains(e.StudentId) && e.Status == EnrolmentStatus.Active))
            || (m.SessionId == null && m.BatchId == null && m.SubjectId != null && db.CourseEnrolments.Any(e => students.Contains(e.StudentId) && e.Status == EnrolmentStatus.Active
                && db.Subjects.Any(s => s.Id == m.SubjectId && s.CourseId == e.CourseId))));
    }

    public async Task<PagedResult<MaterialDto>> MaterialsAsync(LibraryQuery q, CancellationToken ct)
    {
        var materials = await VisibleMaterialsAsync(ct);
        if (q.SessionId is { } sid) materials = materials.Where(m => m.SessionId == sid);
        if (q.SubjectId is { } subj) materials = materials.Where(m => m.SubjectId == subj || db.Sessions.Any(s => s.Id == m.SessionId && s.SubjectId == subj));
        if (!string.IsNullOrWhiteSpace(q.Search)) materials = materials.Where(m => m.Title.Contains(q.Search));
        var total = await materials.CountAsync(ct);
        var items = await materials.OrderByDescending(m => m.CreatedAtUtc).Skip((q.SafePage - 1) * q.SafePageSize).Take(q.SafePageSize)
            .Select(m => new MaterialDto(m.Id, m.Title, m.Description, m.Kind, m.Url,
                m.File == null ? null : new FileDto(m.File.Id, m.File.FileName, m.File.ContentType, m.File.SizeBytes),
                m.SessionId, db.Sessions.Where(s => s.Id == m.SessionId).Select(s => s.Title).FirstOrDefault(),
                m.SubjectId, db.Subjects.Where(s => s.Id == m.SubjectId).Select(s => s.Name).FirstOrDefault(), m.AvailableOffline, m.CreatedAtUtc))
            .ToListAsync(ct);
        return new PagedResult<MaterialDto>(items, q.SafePage, q.SafePageSize, total);
    }

    private async Task ValidateMaterialTargetAsync(Guid? sessionId, Guid? subjectId, Guid? chapterId, Guid? batchId, CancellationToken ct)
    {
        access.Require(Permissions.ContentManage);
        if (sessionId is null && subjectId is null && batchId is null)
            throw RequestValidationException.For("sessionId", "Attach the material to a session, subject or batch.");
        if (sessionId is { } sid) await access.GetManageableSessionAsync(sid, ct);
        if (subjectId is { } subj)
        {
            if (!await db.Subjects.AnyAsync(s => s.Id == subj, ct)) throw RequestValidationException.For("subjectId", "Subject not found.");
            if (access.IsTeacher && sessionId is null && !await db.TeacherSubjects.AnyAsync(t => t.SubjectId == subj && t.TeacherId == user.UserId, ct))
                throw new ForbiddenException("You are not assigned to this subject.");
        }
        if (chapterId is { } chap && !await db.Chapters.AnyAsync(c => c.Id == chap && (subjectId == null || c.SubjectId == subjectId), ct))
            throw RequestValidationException.For("chapterId", "Chapter not found.");
        if (batchId is { } bid && !await db.Batches.AnyAsync(b => b.Id == bid && (access.IsAdmin || b.TeacherId == user.UserId), ct))
            throw RequestValidationException.For("batchId", "Batch not found.");
    }

    public async Task<Guid> CreateLinkMaterialAsync(CreateLinkMaterialRequest r, CancellationToken ct)
    {
        await ValidateMaterialTargetAsync(r.SessionId, r.SubjectId, r.ChapterId, r.BatchId, ct);
        var m = new LearningMaterial
        {
            Title = Text.Required(r.Title, "title", 200),
            Description = Text.Optional(r.Description, "description", 2000),
            Kind = MaterialKind.Link,
            Url = Text.OptionalUrl(r.Url, "url") ?? throw RequestValidationException.For("url", "A link is required."),
            SessionId = r.SessionId,
            SubjectId = r.SubjectId,
            ChapterId = r.ChapterId,
            BatchId = r.BatchId,
            AvailableOffline = false,
            CreatedByTeacherOrAdminId = user.UserId,
        };
        db.Materials.Add(m);
        audit.Record("material.created", "LearningMaterial", m.Id, m.Title);
        await db.SaveChangesAsync(ct);
        return m.Id;
    }

    public async Task<Guid> CreateFileMaterialAsync(Stream content, string fileName, string contentType, long length, string title, string? description,
        Guid? sessionId, Guid? subjectId, Guid? chapterId, Guid? batchId, bool availableOffline, CancellationToken ct)
    {
        await ValidateMaterialTargetAsync(sessionId, subjectId, chapterId, batchId, ct);
        var file = await files.UploadAsync(content, fileName, contentType, length, FilePurpose.Material, ct);
        var m = new LearningMaterial
        {
            Title = Text.Required(title, "title", 200),
            Description = Text.Optional(description, "description", 2000),
            Kind = MaterialKind.File,
            FileId = file.Id,
            SessionId = sessionId,
            SubjectId = subjectId,
            ChapterId = chapterId,
            BatchId = batchId,
            AvailableOffline = availableOffline,
            CreatedByTeacherOrAdminId = user.UserId,
        };
        db.Materials.Add(m);
        audit.Record("material.created", "LearningMaterial", m.Id, m.Title);
        await db.SaveChangesAsync(ct);
        return m.Id;
    }

    public async Task DeleteMaterialAsync(Guid id, CancellationToken ct)
    {
        access.Require(Permissions.ContentManage);
        var m = await db.Materials.Include(x => x.File).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Material");
        if (!access.IsAdmin && m.CreatedByTeacherOrAdminId != user.UserId)
        {
            if (m.SessionId is { } sid) await access.GetManageableSessionAsync(sid, ct);
            else throw new ForbiddenException();
        }
        if (m.File is not null) m.File.DeletedAtUtc = clock.UtcNow;
        db.Materials.Remove(m);
        audit.Record("material.deleted", "LearningMaterial", m.Id, m.Title);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Recordings & consent ----------------

    public async Task<ConsentStatusDto> ConsentStatusAsync(Guid sessionId, CancellationToken ct)
    {
        var s = await access.GetVisibleSessionAsync(sessionId, ct);
        var tenant = await db.Tenants.FirstAsync(t => t.Id == s.TenantId, ct);
        var participants = db.SessionParticipants.Where(p => p.SessionId == s.Id && p.Status == ParticipantStatus.Active);
        if (!access.CanManageSession(s))
        {
            var own = access.IsParent ? await access.ChildIdsAsync(ct) : [user.UserId];
            participants = participants.Where(p => own.Contains(p.StudentId));
        }
        var rows = await participants.Select(p => new { p.StudentId, Name = p.Student!.User!.FullName }).ToListAsync(ct);
        var consents = await db.Consents.Where(c => c.SessionId == s.Id).OrderByDescending(c => c.RecordedAtUtc).ToListAsync(ct);
        bool? Latest(Guid studentId, ConsentType type) => consents.FirstOrDefault(c => c.StudentId == studentId && c.Type == type)?.Granted;
        var dtos = rows.Select(r => new StudentConsentDto(r.StudentId, r.Name, Latest(r.StudentId, ConsentType.Recording), Latest(r.StudentId, ConsentType.Transcription), Latest(r.StudentId, ConsentType.AiProcessing))).ToList();
        var all = await AllConsentedAsync(s.Id, ct);
        return new ConsentStatusDto(s.Id, tenant.RecordingEnabled, tenant.TranscriptionEnabled, tenant.AiAssistantEnabled, dtos, all.Recording, all.Ai);
    }

    /// <summary>True only when every active participant's latest consent of that type is a grant.</summary>
    public async Task<(bool Recording, bool Transcription, bool Ai)> AllConsentedAsync(Guid sessionId, CancellationToken ct)
    {
        var students = await db.SessionParticipants.Where(p => p.SessionId == sessionId && p.Status == ParticipantStatus.Active).Select(p => p.StudentId).ToListAsync(ct);
        var consents = await db.Consents.Where(c => c.SessionId == sessionId).OrderByDescending(c => c.RecordedAtUtc).ToListAsync(ct);
        bool All(ConsentType t) => students.Count > 0 && students.All(sid => consents.FirstOrDefault(c => c.StudentId == sid && c.Type == t)?.Granted == true);
        return (All(ConsentType.Recording), All(ConsentType.Transcription), All(ConsentType.AiProcessing));
    }

    public async Task GiveConsentAsync(Guid sessionId, GiveConsentRequest r, CancellationToken ct)
    {
        var s = await access.GetVisibleSessionAsync(sessionId, ct);
        if (!await db.SessionParticipants.AnyAsync(p => p.SessionId == s.Id && p.StudentId == r.StudentId && p.Status == ParticipantStatus.Active, ct))
            throw new NotFoundException("Participant");
        string method;
        if (access.IsStudent && r.StudentId == user.UserId) method = "Student in app";
        else if (access.IsParent && (await access.ChildIdsAsync(ct)).Contains(r.StudentId)) method = "Guardian in app";
        else if (access.CanManageSession(s)) method = "Recorded by staff from signed form";
        else throw new ForbiddenException();
        db.Consents.Add(new ConsentRecord
        {
            SessionId = s.Id,
            StudentId = r.StudentId,
            Type = r.Type,
            Granted = r.Granted,
            GivenByUserId = user.UserId,
            GivenByRole = user.Role.ToString(),
            RecordedAtUtc = clock.UtcNow,
            Method = method,
        });
        audit.Record(r.Granted ? "consent.granted" : "consent.withdrawn", "LearningSession", s.Id, $"{r.Type} for {r.StudentId} ({method})");
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RecordingDto>> RecordingsAsync(Guid sessionId, CancellationToken ct)
    {
        var s = await access.GetVisibleSessionAsync(sessionId, ct);
        var manage = access.CanManageSession(s);
        return await ProjectRecordings(db.Recordings.Where(r => r.SessionId == s.Id && (manage ? r.Status != RecordingStatus.Deleted : r.Status == RecordingStatus.Available)))
            .ToListAsync(ct);
    }

    private IQueryable<RecordingDto> ProjectRecordings(IQueryable<ClassRecording> q)
    {
        var me = user.UserId;
        return q.OrderByDescending(r => r.CreatedAtUtc).Select(r => new RecordingDto(r.Id, r.SessionId,
            db.Sessions.Where(s => s.Id == r.SessionId).Select(s => s.Title).FirstOrDefault() ?? "", r.Title, r.Status, r.DurationSeconds, r.CreatedAtUtc, r.RetainUntilUtc,
            db.RecordingProgress.Where(p => p.RecordingId == r.Id && p.StudentId == me).Select(p => (int?)p.PositionSeconds).FirstOrDefault(),
            db.RecordingProgress.Any(p => p.RecordingId == r.Id && p.StudentId == me && p.Completed), r.FileId));
    }

    public async Task<Guid> UploadRecordingAsync(Guid sessionId, Stream content, string fileName, string contentType, long length, string title, int? durationSeconds, CancellationToken ct)
    {
        access.Require(Permissions.RecordingsManage);
        var s = await access.GetManageableSessionAsync(sessionId, ct);
        var tenant = await db.Tenants.FirstAsync(t => t.Id == s.TenantId, ct);
        if (!tenant.RecordingEnabled) throw new DomainException("Recording is disabled by your institution.", "recording_disabled");
        if (!(await AllConsentedAsync(s.Id, ct)).Recording)
            throw new DomainException("Every participant (or their guardian) must consent to recording before a recording can be added.", "consent_required");
        if (durationSeconds is < 0 or > 86_400) throw RequestValidationException.For("durationSeconds", "Invalid duration.");
        var file = await files.UploadAsync(content, fileName, contentType, length, FilePurpose.Recording, ct);
        var rec = new ClassRecording
        {
            SessionId = s.Id,
            Title = Text.Required(title, "title", 200),
            Source = RecordingSource.Upload,
            FileId = file.Id,
            DurationSeconds = durationSeconds,
            Status = RecordingStatus.Available,
            RetainUntilUtc = clock.UtcNow.AddDays(tenant.RecordingRetentionDays),
            UploadedByUserId = user.UserId,
        };
        db.Recordings.Add(rec);
        var students = await db.SessionParticipants.Where(p => p.SessionId == s.Id && p.Status == ParticipantStatus.Active).Select(p => p.StudentId).ToListAsync(ct);
        await notify.NotifyAsync(students, NotificationType.RecordingAvailable, $"Recording available: {s.Title}", rec.Title, $"/sessions/{s.Id}", $"recording:{rec.Id}", ct);
        audit.Record("recording.uploaded", "ClassRecording", rec.Id, rec.Title);
        await db.SaveChangesAsync(ct);
        return rec.Id;
    }

    public async Task DeleteRecordingAsync(Guid recordingId, CancellationToken ct)
    {
        var rec = await db.Recordings.Include(r => r.File).FirstOrDefaultAsync(r => r.Id == recordingId, ct) ?? throw new NotFoundException("Recording");
        await access.GetManageableSessionAsync(rec.SessionId, ct);
        rec.Status = RecordingStatus.Deleted;
        rec.DeletedAtUtc = clock.UtcNow;
        if (rec.File is not null) rec.File.DeletedAtUtc = clock.UtcNow;
        audit.Record("recording.deleted", "ClassRecording", rec.Id, rec.Title);
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveProgressAsync(Guid recordingId, RecordingProgressRequest r, CancellationToken ct)
    {
        if (!access.IsStudent) throw new ForbiddenException();
        var rec = await db.Recordings.FirstOrDefaultAsync(x => x.Id == recordingId && x.Status == RecordingStatus.Available, ct) ?? throw new NotFoundException("Recording");
        await access.GetVisibleSessionAsync(rec.SessionId, ct);
        var p = await db.RecordingProgress.FirstOrDefaultAsync(x => x.RecordingId == rec.Id && x.StudentId == user.UserId, ct);
        if (p is null) db.RecordingProgress.Add(p = new RecordingProgress { RecordingId = rec.Id, StudentId = user.UserId });
        p.PositionSeconds = Math.Max(0, r.PositionSeconds);
        p.Completed |= r.Completed;
        p.LastWatchedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Recordings across all sessions the user can see (the learning library).</summary>
    public async Task<PagedResult<RecordingDto>> LibraryRecordingsAsync(LibraryQuery q, CancellationToken ct)
    {
        var sessions = await access.VisibleSessionsAsync(ct);
        if (q.SubjectId is { } subj) sessions = sessions.Where(s => s.SubjectId == subj);
        var recs = db.Recordings.Where(r => r.Status == RecordingStatus.Available && sessions.Any(s => s.Id == r.SessionId));
        if (!string.IsNullOrWhiteSpace(q.Search)) recs = recs.Where(r => r.Title.Contains(q.Search));
        var total = await recs.CountAsync(ct);
        var items = await ProjectRecordings(recs).Skip((q.SafePage - 1) * q.SafePageSize).Take(q.SafePageSize).ToListAsync(ct);
        return new PagedResult<RecordingDto>(items, q.SafePage, q.SafePageSize, total);
    }

    /// <summary>Available recordings of the visible classes of one subject (the learning-library rule, scoped to the subject).</summary>
    public async Task<IReadOnlyList<RecordingDto>> SubjectRecordingsAsync(Guid subjectId, CancellationToken ct)
    {
        var sessions = (await access.VisibleSessionsAsync(ct)).Where(s => s.SubjectId == subjectId);
        return await ProjectRecordings(db.Recordings.Where(r => r.Status == RecordingStatus.Available && sessions.Any(s => s.Id == r.SessionId)))
            .ToListAsync(ct);
    }

    /// <summary>Published notes across all sessions the user can see.</summary>
    public async Task<PagedResult<NoteDto>> LibraryNotesAsync(LibraryQuery q, CancellationToken ct)
    {
        var sessions = await access.VisibleSessionsAsync(ct);
        if (q.SubjectId is { } subj) sessions = sessions.Where(s => s.SubjectId == subj);
        var notes = db.Notes.Where(n => n.Status == PublicationStatus.Published && n.Visibility == NoteVisibility.Participants && sessions.Any(s => s.Id == n.SessionId));
        if (!string.IsNullOrWhiteSpace(q.Search)) notes = notes.Where(n => n.Title.Contains(q.Search) || n.BodyMarkdown.Contains(q.Search));
        var total = await notes.CountAsync(ct);
        var items = await ProjectNotes(notes.OrderByDescending(n => n.PublishedAtUtc)).Skip((q.SafePage - 1) * q.SafePageSize).Take(q.SafePageSize).ToListAsync(ct);
        return new PagedResult<NoteDto>(items, q.SafePage, q.SafePageSize, total);
    }

    /// <summary>Retention: removes recordings whose retention period has passed. Runs per tenant.</summary>
    public async Task<int> ApplyRetentionAsync(IFileStorage storage, CancellationToken ct)
    {
        var expired = await db.Recordings.Include(r => r.File).Where(r => r.Status != RecordingStatus.Deleted && r.RetainUntilUtc < clock.UtcNow).Take(200).ToListAsync(ct);
        foreach (var rec in expired)
        {
            rec.Status = RecordingStatus.Deleted;
            rec.DeletedAtUtc = clock.UtcNow;
            if (rec.File is not null)
            {
                await storage.DeleteAsync(rec.File.StorageKey, ct);
                rec.File.DeletedAtUtc = clock.UtcNow;
            }
            audit.Record("recording.retention_deleted", "ClassRecording", rec.Id, rec.Title);
        }
        await db.SaveChangesAsync(ct);
        return expired.Count;
    }
}
