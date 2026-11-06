using Microsoft.EntityFrameworkCore;
using StudentSync.Contracts;

namespace StudentSync.Application.Common;

/// <summary>
/// Builds profile-photo URLs for the current viewer: a download link carrying a user-bound, time-limited token
/// (<see cref="Lifetime"/>), plus a <c>v</c> parameter (the file id) that clients use as a cache key; the server ignores it.
/// Callers decide whether the viewer may see a given photo before asking for its URL.
/// </summary>
public sealed class PhotoUrls(IAppDbContext db, ICurrentUser user, IFileAccessTokens tokens)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);
    private readonly Dictionary<Guid, string> _cache = [];

    /// <summary>URL for the signed-in viewer, or null when there is no photo or no institution context.</summary>
    public string? For(Guid? fileId) => user.IsAuthenticated ? For(fileId, user.UserId, user.TenantId) : null;

    /// <summary>URL for an explicit viewer (used while signing in, before the request is authenticated).</summary>
    public string? For(Guid? fileId, Guid viewerId, Guid? tenantId)
    {
        if (fileId is not { } id || tenantId is not { } tid) return null;
        if (viewerId == user.UserId && _cache.TryGetValue(id, out var cached)) return cached;
        var token = tokens.Create(id, viewerId, tid, Lifetime);
        var url = $"/api/v1/files/download?token={Uri.EscapeDataString(token)}&v={id:N}";
        if (viewerId == user.UserId) _cache[id] = url;
        return url;
    }

    /// <summary>Fills <see cref="SessionListItemDto.TeacherPhotoUrl"/>. Staff photos may be shown to anyone in the institution.</summary>
    public async Task<List<SessionListItemDto>> WithTeacherPhotosAsync(IEnumerable<SessionListItemDto> sessions, CancellationToken ct)
    {
        var list = sessions.ToList();
        var teacherIds = list.Select(s => s.TeacherId).Distinct().ToList();
        if (teacherIds.Count == 0) return list;
        var photos = await db.Teachers.Where(t => teacherIds.Contains(t.Id) && t.User!.PhotoFileId != null)
            .Select(t => new { t.Id, t.User!.PhotoFileId }).ToDictionaryAsync(t => t.Id, t => t.PhotoFileId, ct);
        return list.Select(s => s with { TeacherPhotoUrl = For(photos.GetValueOrDefault(s.TeacherId)) }).ToList();
    }

    public async Task<SessionListItemDto?> WithTeacherPhotoAsync(SessionListItemDto? session, CancellationToken ct)
        => session is null ? null : (await WithTeacherPhotosAsync([session], ct))[0];
}
