using StudentSync.Domain.Identity;

namespace StudentSync.Contracts;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public record PageQuery
{
    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public string? Search { get; init; }

    public int SafePage => Math.Max(1, Page ?? 1);
    public int SafePageSize => Math.Clamp(PageSize ?? 25, 1, 100);
}

public sealed record IdResponse(Guid Id);
public sealed record LookupItem(Guid Id, string Name, string? Detail = null);

// ---------- Authentication ----------

public sealed record LoginRequest(string Email, string Password, string? InstitutionCode, string? ClientName);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    CurrentUserDto User);

public sealed record CurrentUserDto(
    Guid Id,
    string FullName,
    string Email,
    UserRole Role,
    Guid? TenantId,
    string? TenantName,
    string TimeZoneId,
    string PreferredLanguage,
    bool MustChangePassword,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<string> EnabledFeatures,
    string? PhotoUrl = null);

public sealed record UpdateProfileRequest(string FullName, string TimeZoneId, string PreferredLanguage);
