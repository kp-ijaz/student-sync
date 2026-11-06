using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Assessments;
using StudentSync.Domain.Content;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Content;

/// <summary>
/// Uploads go to private object storage. Downloads require a short-lived, user-bound token that is only issued
/// after checking that the caller can access a record referencing the file.
/// </summary>
public sealed class FileService(IAppDbContext db, ICurrentUser user, ITenantContext tenant, AccessPolicy access, IFileStorage storage, IFileAccessTokens tokens, IClock clock, AuditService audit)
{
    private static readonly Dictionary<FilePurpose, (long MaxBytes, string[] Types)> Rules = new()
    {
        [FilePurpose.Material] = (50_000_000, ["application/pdf", "image/png", "image/jpeg", "image/webp", "text/plain", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/vnd.openxmlformats-officedocument.presentationml.presentation", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"]),
        [FilePurpose.NoteImage] = (10_000_000, ["image/png", "image/jpeg", "image/webp"]),
        [FilePurpose.QuestionImage] = (10_000_000, ["image/png", "image/jpeg", "image/webp"]),
        [FilePurpose.Recording] = (2_000_000_000, ["video/mp4", "video/quicktime", "video/webm", "audio/mpeg", "audio/mp4", "audio/webm", "audio/wav"]),
        // video/quicktime: iPhone camera-roll videos are QuickTime .mov files.
        [FilePurpose.PrerecordedVideo] = (2_000_000_000, ["video/mp4", "video/quicktime", "video/webm"]),
        [FilePurpose.Transcript] = (5_000_000, ["text/vtt", "application/x-subrip", "text/plain"]),
        [FilePurpose.AssignmentAttachment] = (25_000_000, ["application/pdf", "image/png", "image/jpeg", "image/webp", "text/plain", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"]),
        [FilePurpose.Submission] = (25_000_000, ["application/pdf", "image/png", "image/jpeg", "image/webp", "text/plain", "audio/mpeg", "audio/mp4", "audio/webm", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"]),
        [FilePurpose.DoubtAttachment] = (15_000_000, ["application/pdf", "image/png", "image/jpeg", "image/webp", "audio/mpeg", "audio/mp4", "audio/webm"]),
        [FilePurpose.MessageAttachment] = (15_000_000, ["application/pdf", "image/png", "image/jpeg", "image/webp"]),
        // Profile photos: metadata (EXIF GPS etc.) is stripped before storage, so only formats we can strip are accepted.
        [FilePurpose.Avatar] = (5_000_000, ["image/jpeg", "image/png"]),
    };

    // Magic numbers for formats where a mismatched declared type is a realistic attack vector.
    private static bool SignatureMatches(string contentType, ReadOnlySpan<byte> head) => contentType switch
    {
        "application/pdf" => head.StartsWith("%PDF"u8),
        "image/png" => head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
        "image/jpeg" => head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
        "image/webp" => head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8),
        _ when contentType.StartsWith("application/vnd.openxmlformats") => head.StartsWith("PK"u8),
        _ => true,
    };

    public async Task<FileDto> UploadAsync(Stream content, string fileName, string contentType, long length, FilePurpose purpose, CancellationToken ct)
    {
        if (tenant.TenantId is not { } tenantId) throw new ForbiddenException("Files belong to an institution.");
        if (!Rules.TryGetValue(purpose, out var rule)) throw RequestValidationException.For("purpose", "Unsupported upload purpose.");
        contentType = contentType.Split(';')[0].Trim().ToLowerInvariant();
        if (contentType is "application/octet-stream" or "" && fileName.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase)) contentType = "text/vtt";
        if (contentType is "application/octet-stream" or "" && fileName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)) contentType = "application/x-subrip";
        // Mobile pickers often send videos as application/octet-stream; infer the type from the extension (still checked against the allow-list).
        if (contentType is "application/octet-stream" or "" && fileName.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)) contentType = "video/quicktime";
        if (contentType is "application/octet-stream" or "" && fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) contentType = "video/mp4";
        if (!rule.Types.Contains(contentType)) throw RequestValidationException.For("file", $"Files of type '{contentType}' are not allowed here.");
        if (length <= 0) throw RequestValidationException.For("file", "The file is empty.");
        if (length > rule.MaxBytes) throw RequestValidationException.For("file", $"The file is larger than {rule.MaxBytes / 1_000_000} MB.");
        // Plan storage limit (counts live files only).
        var plan = await db.Subscriptions.Where(x => x.TenantId == tenantId).Select(x => (int?)x.Plan!.MaxStorageMb).FirstOrDefaultAsync(ct);
        if (plan is { } maxMb)
        {
            var used = await db.Files.Where(f => f.DeletedAtUtc == null).SumAsync(f => (long?)f.SizeBytes, ct) ?? 0;
            if (used + length > maxMb * 1_000_000L)
                throw new Domain.Common.DomainException("Your institution has reached its storage limit. Remove old files or upgrade the plan.", "storage_limit");
        }
        var safeName = Path.GetFileName(fileName).Trim();
        if (safeName.Length is 0 or > 200) safeName = "file";

        // Buffer to a temp file so we can verify the signature and hash before storing.
        var temp = Path.GetTempFileName();
        try
        {
            string sha;
            long size;
            await using (var fs = File.Create(temp))
            {
                await content.CopyToAsync(fs, ct);
                size = fs.Length;
            }
            if (size > rule.MaxBytes) throw RequestValidationException.For("file", "The file is too large.");
            await using (var fs = File.OpenRead(temp))
            {
                var head = new byte[16];
                var read = await fs.ReadAsync(head, ct);
                if (!SignatureMatches(contentType, head.AsSpan(0, read))) throw RequestValidationException.For("file", "The file contents do not match its type.");
            }
            if (purpose == FilePurpose.Avatar)
            {
                // Privacy: phone photos carry GPS position and device details. Drop metadata before the file is stored.
                byte[] stripped;
                try { stripped = ImageMetadataStripper.Strip(await File.ReadAllBytesAsync(temp, ct), contentType); }
                catch (InvalidImageException) { throw RequestValidationException.For("file", "The image could not be read. Upload a valid JPEG or PNG photo."); }
                await File.WriteAllBytesAsync(temp, stripped, ct);
                size = stripped.Length;
            }
            await using (var fs = File.OpenRead(temp))
            {
                sha = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
            }
            var key = $"{tenantId:N}/{clock.UtcNow:yyyy/MM}/{Guid.NewGuid():N}";
            await using (var fs = File.OpenRead(temp)) await storage.SaveAsync(key, fs, ct);
            var file = new StoredFile
            {
                StorageKey = key,
                FileName = safeName,
                ContentType = contentType,
                SizeBytes = size,
                Sha256 = sha,
                Purpose = purpose,
                UploadedByUserId = user.UserId,
            };
            db.Files.Add(file);
            await db.SaveChangesAsync(ct);
            return new FileDto(file.Id, file.FileName, file.ContentType, file.SizeBytes);
        }
        finally { File.Delete(temp); }
    }

    /// <summary>Validates that files were uploaded by the caller for this purpose before they are attached to a record.</summary>
    public async Task<List<Guid>> ClaimAsync(IEnumerable<Guid>? fileIds, FilePurpose purpose, CancellationToken ct)
    {
        var ids = fileIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0) return ids;
        if (ids.Count > 10) throw RequestValidationException.For("fileIds", "Attach at most 10 files.");
        var owned = await db.Files.CountAsync(f => ids.Contains(f.Id) && f.UploadedByUserId == user.UserId && f.Purpose == purpose && f.DeletedAtUtc == null, ct);
        if (owned != ids.Count) throw RequestValidationException.For("fileIds", "One or more attachments are invalid.");
        return ids;
    }

    public async Task<FileLinkDto> CreateLinkAsync(Guid fileId, CancellationToken ct)
    {
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId && f.DeletedAtUtc == null, ct) ?? throw new NotFoundException("File");
        if (!await CanAccessAsync(file, ct)) throw new NotFoundException("File");
        if (file.Purpose is FilePurpose.Recording or FilePurpose.PrerecordedVideo) audit.Record("recording.access", "StoredFile", file.Id, null);
        await db.SaveChangesAsync(ct);
        var lifetime = TimeSpan.FromMinutes(file.Purpose is FilePurpose.Recording or FilePurpose.PrerecordedVideo ? 30 : 5);
        var token = tokens.Create(file.Id, user.UserId, file.TenantId, lifetime);
        return new FileLinkDto($"/api/v1/files/download?token={Uri.EscapeDataString(token)}", clock.UtcNow.Add(lifetime));
    }

    private async Task<bool> CanAccessAsync(StoredFile file, CancellationToken ct)
    {
        if (file.UploadedByUserId == user.UserId) return true;
        var id = file.Id;
        var sessions = await access.VisibleSessionsAsync(ct);
        var staff = access.IsAdmin || access.IsTeacher;

        switch (file.Purpose)
        {
            case FilePurpose.Material:
                {
                    var m = await db.Materials.FirstOrDefaultAsync(x => x.FileId == id, ct);
                    return m is not null && await ContentService.CanViewMaterialAsync(db, access, user, m, ct);
                }
            case FilePurpose.Recording:
                {
                    var r = await db.Recordings.FirstOrDefaultAsync(x => x.FileId == id && x.Status == RecordingStatus.Available, ct);
                    return r is not null && await sessions.AnyAsync(s => s.Id == r.SessionId, ct);
                }
            case FilePurpose.PrerecordedVideo:
                {
                    // Locked videos are refused here even if the client somehow knows the file id.
                    var v = await db.PrerecordedVideos.FirstOrDefaultAsync(x => x.FileId == id && x.DeletedAtUtc == null, ct);
                    var session = v is null ? null : await sessions.FirstOrDefaultAsync(s => s.Id == v.SessionId, ct);
                    return session is not null && (access.CanManageSession(session) || v!.IsUnlocked(session, clock.UtcNow));
                }
            case FilePurpose.QuestionImage:
                {
                    // Staff who may manage the question; students only while/after they take a published exam containing it.
                    var questionIds = db.Questions.Where(q => q.ImageFileId == id).Select(q => q.Id);
                    if (access.IsAdmin) return await questionIds.AnyAsync(ct);
                    if (access.IsTeacher)
                        return await db.Questions.AnyAsync(q => q.ImageFileId == id && db.TeacherSubjects.Any(t => t.SubjectId == q.SubjectId && t.TeacherId == user.UserId), ct)
                            || await db.ExamQuestions.AnyAsync(eq => questionIds.Contains(eq.QuestionId) && db.Exams.Any(e => e.Id == eq.ExamId && e.TeacherId == user.UserId), ct);
                    if (access.IsStudent)
                        return await db.ExamQuestions.AnyAsync(eq => questionIds.Contains(eq.QuestionId)
                            && db.Exams.Any(e => e.Id == eq.ExamId && e.Status != ExamStatus.Draft && e.Recipients.Any(r => r.StudentId == user.UserId)), ct);
                    return false;
                }
            case FilePurpose.Transcript:
                return staff && await db.Transcripts.AnyAsync(t => sessions.Any(s => s.Id == t.SessionId && (access.IsAdmin || s.TeacherId == user.UserId)), ct);
            case FilePurpose.AssignmentAttachment:
                {
                    var assignmentIds = db.AssignmentAttachments.Where(a => a.FileId == id).Select(a => a.AssignmentId);
                    if (access.IsAdmin) return await assignmentIds.AnyAsync(ct);
                    if (access.IsTeacher) return await db.Assignments.AnyAsync(a => assignmentIds.Contains(a.Id) && a.TeacherId == user.UserId, ct);
                    var students = access.IsParent ? await access.ChildIdsAsync(ct) : [user.UserId];
                    return await db.AssignmentRecipients.AnyAsync(r => assignmentIds.Contains(r.AssignmentId) && students.Contains(r.StudentId)
                        && db.Assignments.Any(a => a.Id == r.AssignmentId && a.Status != Domain.Assignments.AssignmentStatus.Draft), ct);
                }
            case FilePurpose.Submission:
                {
                    var sub = await db.SubmissionFiles.Where(f => f.FileId == id)
                        .Join(db.Submissions, f => f.SubmissionId, s => s.Id, (f, s) => new { s.StudentId, s.AssignmentId }).FirstOrDefaultAsync(ct);
                    if (sub is null) return false;
                    if (access.IsAdmin) return true;
                    if (access.IsTeacher) return await db.Assignments.AnyAsync(a => a.Id == sub.AssignmentId && a.TeacherId == user.UserId, ct);
                    if (access.IsParent) return (await access.ChildIdsAsync(ct)).Contains(sub.StudentId);
                    return false;
                }
            case FilePurpose.DoubtAttachment:
                {
                    var doubt = await db.DoubtAttachments.Where(a => a.FileId == id).Join(db.Doubts, a => a.DoubtId, d => d.Id, (a, d) => d).FirstOrDefaultAsync(ct);
                    if (doubt is null) return false;
                    if (access.IsAdmin || doubt.AssignedTeacherId == user.UserId) return true;
                    if (access.IsTeacher) return doubt.AssignedTeacherId is null && await access.TeacherTeachesStudentAsync(user.UserId, doubt.StudentId, ct);
                    return access.IsParent && (await access.ChildIdsAsync(ct)).Contains(doubt.StudentId);
                }
            case FilePurpose.MessageAttachment:
                return await db.Messages.AnyAsync(m => m.AttachmentFileId == id
                    && db.ConversationParticipants.Any(p => p.ConversationId == m.ConversationId && p.UserId == user.UserId), ct);
            case FilePurpose.Avatar:
                {
                    // Staff photos: anyone signed in at the institution. Student photos: the student, admins and
                    // teachers who teach them — never other students or parents.
                    var owner = await db.Users.Where(u => u.PhotoFileId == id && u.TenantId == file.TenantId).Select(u => new { u.Id, u.Role }).FirstOrDefaultAsync(ct);
                    if (owner is null || user.TenantId != file.TenantId) return false;
                    if (owner.Id == user.UserId) return true;
                    return owner.Role switch
                    {
                        UserRole.Teacher or UserRole.InstitutionAdmin => true,
                        UserRole.Student => access.IsAdmin || (access.IsTeacher && await access.TeacherTeachesStudentAsync(user.UserId, owner.Id, ct)),
                        _ => false,
                    };
                }
            case FilePurpose.NoteImage:
                {
                    // Note images are referenced from note Markdown; access follows the note.
                    var marker = id.ToString();
                    return await db.Notes.AnyAsync(n => n.BodyMarkdown.Contains(marker) && sessions.Any(s => s.Id == n.SessionId)
                        && (staff || (n.Status == PublicationStatus.Published && n.Visibility == NoteVisibility.Participants)), ct);
                }
            default:
                return false;
        }
    }
}
