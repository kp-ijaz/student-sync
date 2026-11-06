using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using StudentSync.Application.Common;
using StudentSync.Domain.Identity;

namespace StudentSync.Infrastructure.Identity;

public static class ClaimNames
{
    public const string Tenant = "tid";
    public const string Role = "role";
    public const string TimeZone = "tz";
    public const string MustChangePassword = "pwd_change";
}

/// <summary>The authenticated principal. Identity, tenant and role come only from the validated token, never from request data.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid UserId => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal?.FindFirstValue("sub"), out var id) ? id : Guid.Empty;

    public Guid? TenantId => Guid.TryParse(Principal?.FindFirstValue(ClaimNames.Tenant), out var id) ? id : null;

    public UserRole Role => Enum.TryParse<UserRole>(Principal?.FindFirstValue(ClaimNames.Role), out var r) ? r : UserRole.Student;

    public string TimeZoneId => Principal?.FindFirstValue(ClaimNames.TimeZone) ?? "UTC";

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool Has(string permission) => IsAuthenticated && RolePermissions.Has(Role, permission);
}

/// <summary>Tenant scope: the caller's tenant, or an explicit scope opened by a background job.</summary>
public sealed class TenantContext(ICurrentUser user) : ITenantContext
{
    private readonly Stack<Guid> _scopes = new();

    public Guid? TenantId => _scopes.Count > 0 ? _scopes.Peek() : user.TenantId;

    public IDisposable BeginScope(Guid tenantId)
    {
        _scopes.Push(tenantId);
        return new Pop(_scopes);
    }

    private sealed class Pop(Stack<Guid> scopes) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            scopes.Pop();
        }
    }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
