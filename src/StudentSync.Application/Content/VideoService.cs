using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Content;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Content;

/// <summary>
/// Pre-recorded class videos. Lock state is decided here and in <see cref="FileService"/> (which refuses playback
/// links for locked videos), never by the client.
/// </summary>
public sealed class VideoService(IAppDbContext db, AccessPolicy access, IClock clock, AuditService audit, FileService files, ICurrentUser user)
{
    public async Task<IReadOnlyList<PrerecordedVideoDto>> ForSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var s = await access.GetVisibleSessionAsync(sessionId, ct);
        var videos = await db.PrerecordedVideos.Where(v => v.SessionId == s.Id && v.DeletedAtUtc == null).OrderBy(v => v.CreatedAtUtc).ToListAsync(ct);
        return videos.Select(v => ToDto(v, s)).ToList();
    }

    /// <summary>Videos across every class the user can see, newest class first.</summary>
    public async Task<PagedResult<PrerecordedVideoDto>> ListAsync(VideoQuery q, CancellationToken ct)
    {
        var sessions = await access.VisibleSessionsAsync(ct);
        if (q.SessionId is { } sid) sessions = sessions.Where(s => s.Id == sid);
        var rows = db.PrerecordedVideos.Where(v => v.DeletedAtUtc == null)
            .Join(sessions, v => v.SessionId, s => s.Id, (v, s) => new { Video = v, Session = s });
        if (!string.IsNullOrWhiteSpace(q.Search)) rows = rows.Where(x => x.Video.Title.Contains(q.Search) || x.Session.Title.Contains(q.Search));
        var total = await rows.CountAsync(ct);
        var page = await rows.OrderByDescending(x => x.Session.StartUtc).ThenBy(x => x.Video.CreatedAtUtc)
            .Skip((q.SafePage - 1) * q.SafePageSize).Take(q.SafePageSize).ToListAsync(ct);
        return new PagedResult<PrerecordedVideoDto>(page.Select(x => ToDto(x.Video, x.Session)).ToList(), q.SafePage, q.SafePageSize, total);
    }

    /// <summary>
    /// Videos of the visible classes of one subject (or of every subject when <paramref name="subjectId"/> is null), each paired with
    /// its class's subject and chapter. Same visibility, lock and FileId rules as <see cref="ListAsync"/>; chronological by class.
    /// </summary>
    public async Task<IReadOnlyList<(PrerecordedVideoDto Video, Guid SubjectId, Guid? ChapterId)>> ForSubjectsAsync(Guid? subjectId, CancellationToken ct)
    {
        var sessions = (await access.VisibleSessionsAsync(ct)).Where(s => s.SubjectId != null);
        if (subjectId is { } id) sessions = sessions.Where(s => s.SubjectId == id);
        var rows = await db.PrerecordedVideos.Where(v => v.DeletedAtUtc == null)
            .Join(sessions, v => v.SessionId, s => s.Id, (v, s) => new { Video = v, Session = s })
            .OrderBy(x => x.Session.StartUtc).ThenBy(x => x.Video.CreatedAtUtc)
            .AsNoTracking().ToListAsync(ct);
        return rows.Select(x => (ToDto(x.Video, x.Session), x.Session.SubjectId!.Value, x.Session.ChapterId)).ToList();
    }

    public async Task<Guid> UploadAsync(Guid sessionId, Stream content, string fileName, string contentType, long length,
        string title, string? description, VideoUnlockRule rule, int offsetMinutes, int? durationSeconds, CancellationToken ct)
    {
        access.Require(Permissions.ContentManage);
        var s = await access.GetManageableSessionAsync(sessionId, ct);
        if (!Enum.IsDefined(rule)) throw RequestValidationException.For("unlockRule", "Choose when the video unlocks.");
        if (durationSeconds is < 0 or > 86_400) throw RequestValidationException.For("durationSeconds", "Invalid duration.");
        PrerecordedVideo.ValidateOffset(offsetMinutes);
        var video = new PrerecordedVideo
        {
            SessionId = s.Id,
            Title = Text.Required(title, "title", 200),
            Description = Text.Optional(description, "description", 2000),
            UnlockRule = rule,
            UnlockOffsetMinutes = offsetMinutes,
            DurationSeconds = durationSeconds,
            UploadedByUserId = user.UserId,
        };
        var file = await files.UploadAsync(content, fileName, contentType, length, FilePurpose.PrerecordedVideo, ct);
        video.FileId = file.Id;
        db.PrerecordedVideos.Add(video);
        audit.Record("video.uploaded", "PrerecordedVideo", video.Id, $"{video.Title} ({rule}, {offsetMinutes} min)");
        await db.SaveChangesAsync(ct);
        return video.Id;
    }

    public async Task UpdateAsync(Guid id, UpdatePrerecordedVideoRequest r, CancellationToken ct)
    {
        var (video, _) = await LoadManageableAsync(id, ct);
        RowVersions.Apply(db, video, r.RowVersion);
        if (!Enum.IsDefined(r.UnlockRule)) throw RequestValidationException.For("unlockRule", "Choose when the video unlocks.");
        PrerecordedVideo.ValidateOffset(r.UnlockOffsetMinutes);
        video.Title = Text.Required(r.Title, "title", 200);
        video.Description = Text.Optional(r.Description, "description", 2000);
        video.UnlockRule = r.UnlockRule;
        video.UnlockOffsetMinutes = r.UnlockOffsetMinutes;
        audit.Record("video.updated", "PrerecordedVideo", video.Id, $"{video.Title} ({r.UnlockRule}, {r.UnlockOffsetMinutes} min)");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Manual control by the class teacher or an admin: force unlocked, force locked, or back to the automatic rule.</summary>
    public async Task SetAccessAsync(Guid id, SetVideoAccessRequest r, CancellationToken ct)
    {
        var (video, _) = await LoadManageableAsync(id, ct);
        if (!Enum.IsDefined(r.AccessOverride)) throw RequestValidationException.For("accessOverride", "Choose an access option.");
        video.AccessOverride = r.AccessOverride;
        // A manual lock is a deliberate re-lock: the automatic rule should not silently reopen it via an earlier unlock.
        if (r.AccessOverride == VideoAccessOverride.Locked) video.UnlockedAtUtc = null;
        audit.Record("video.access_changed", "PrerecordedVideo", video.Id, r.AccessOverride.ToString());
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var (video, _) = await LoadManageableAsync(id, ct);
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == video.FileId, ct);
        video.DeletedAtUtc = clock.UtcNow;
        if (file is not null) file.DeletedAtUtc = clock.UtcNow;
        audit.Record("video.deleted", "PrerecordedVideo", video.Id, video.Title);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Called before a class moves so videos students can already watch stay unlocked.</summary>
    public static async Task PinUnlockedAsync(IAppDbContext db, LearningSession session, DateTime nowUtc, CancellationToken ct)
    {
        var videos = await db.PrerecordedVideos
            .Where(v => v.SessionId == session.Id && v.DeletedAtUtc == null && v.UnlockedAtUtc == null && v.AccessOverride == VideoAccessOverride.Automatic)
            .ToListAsync(ct);
        foreach (var v in videos.Where(v => v.IsUnlocked(session, nowUtc))) v.UnlockedAtUtc = nowUtc;
    }

    private async Task<(PrerecordedVideo, LearningSession)> LoadManageableAsync(Guid id, CancellationToken ct)
    {
        var video = await db.PrerecordedVideos.FirstOrDefaultAsync(v => v.Id == id && v.DeletedAtUtc == null, ct) ?? throw new NotFoundException("Video");
        var session = await access.GetManageableSessionAsync(video.SessionId, ct);
        return (video, session);
    }

    private PrerecordedVideoDto ToDto(PrerecordedVideo v, LearningSession s)
    {
        var now = clock.UtcNow;
        var manage = access.CanManageSession(s);
        var unlocked = v.IsUnlocked(s, now);
        return new PrerecordedVideoDto(v.Id, s.Id, s.Title, s.StartUtc, s.EndUtc, s.Status == SessionStatus.Cancelled,
            v.Title, v.Description, v.DurationSeconds, v.UnlockRule, v.UnlockOffsetMinutes, v.AccessOverride,
            unlocked, v.NextUnlockAtUtc(s, now), unlocked || manage ? v.FileId : null, manage, v.CreatedAtUtc, RowVersions.Encode(v.RowVersion));
    }
}
