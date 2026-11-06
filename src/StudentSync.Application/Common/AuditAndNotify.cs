using System.Text;
using Microsoft.EntityFrameworkCore;
using StudentSync.Domain.Common;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Platform;

namespace StudentSync.Application.Common;

/// <summary>Records semantic audit events. Entries are saved in the same transaction as the change they describe.</summary>
public sealed class AuditService(IAppDbContext db, ICurrentUser user, ITenantContext tenant, IClock clock)
{
    public void Record(string action, string entityType, Guid? entityId, string? summary)
    {
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenant.TenantId,
            UserId = user.IsAuthenticated ? user.UserId : null,
            Action = action,
            EntityType = entityType,
            EntityId = entityId?.ToString(),
            Summary = summary is { Length: > 1000 } ? summary[..1000] : summary,
            IpAddress = user.IpAddress,
            CreatedAtUtc = clock.UtcNow,
        });
    }
}

/// <summary>
/// Creates in-app notifications. (UserId, DedupeKey) is unique so a retried event never notifies twice.
/// Real-time push and external delivery happen after the surrounding transaction commits.
/// </summary>
public sealed class NotificationService(IAppDbContext db, ITenantContext tenant, IClock clock)
{
    public async Task NotifyAsync(IEnumerable<Guid> userIds, NotificationType type, string title, string body, string? link, string dedupeKey, CancellationToken ct)
    {
        var targets = userIds.Distinct().ToList();
        if (targets.Count == 0) return;
        var key = dedupeKey.Length > 200 ? dedupeKey[..200] : dedupeKey;
        var existing = await db.Notifications.Where(n => targets.Contains(n.UserId) && n.DedupeKey == key).Select(n => n.UserId).ToListAsync(ct);
        var pending = db.Notifications.Local.Where(n => n.DedupeKey == key).Select(n => n.UserId);
        foreach (var userId in targets.Except(existing).Except(pending))
        {
            db.Notifications.Add(new Notification
            {
                TenantId = tenant.TenantId,
                UserId = userId,
                Type = type,
                Title = Truncate(title, 200),
                Body = Truncate(body, 1000),
                Link = link,
                DedupeKey = key,
                CreatedAtUtc = clock.UtcNow,
            });
        }
    }

    /// <summary>Students plus their verified parents/guardians.</summary>
    public async Task<List<Guid>> WithGuardiansAsync(IEnumerable<Guid> studentIds, CancellationToken ct)
    {
        var ids = studentIds.Distinct().ToList();
        var parents = await db.ParentLinks.Where(l => ids.Contains(l.StudentId) && l.IsVerified).Select(l => l.ParentId).ToListAsync(ct);
        return ids.Concat(parents).Distinct().ToList();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

public static class RowVersions
{
    public static string Encode(byte[] v) => Convert.ToBase64String(v);

    /// <summary>Applies the client's row version so EF detects concurrent edits (409 on mismatch).</summary>
    public static void Apply(IAppDbContext db, AuditableEntity entity, string? rowVersion)
    {
        if (string.IsNullOrEmpty(rowVersion)) return;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw RequestValidationException.For("rowVersion", "Invalid version token."); }
        if (!bytes.AsSpan().SequenceEqual(entity.RowVersion))
            throw new ConflictException("This record was changed by someone else. Reload and try again.", "concurrency_conflict");
        ((DbContext)db).Entry(entity).Property(e => e.RowVersion).OriginalValue = bytes;
    }
}

public static class Text
{
    public static string Required(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw RequestValidationException.For(field, $"{Humanise(field)} is required.");
        if (v.Length > max) throw RequestValidationException.For(field, $"{Humanise(field)} must be at most {max} characters.");
        return v;
    }

    public static string? Optional(string? value, string field, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max) throw RequestValidationException.For(field, $"{Humanise(field)} must be at most {max} characters.");
        return v;
    }

    public static string? OptionalUrl(string? value, string field)
    {
        var v = Optional(value, field, 1000);
        if (v is null) return null;
        if (!Uri.TryCreate(v, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw RequestValidationException.For(field, "Enter a valid http(s) link.");
        return uri.ToString();
    }

    public static string Humanise(string field)
    {
        var sb = new StringBuilder();
        foreach (var c in field)
        {
            if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ');
            sb.Append(sb.Length == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
