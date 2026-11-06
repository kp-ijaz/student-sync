using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;
using StudentSync.Domain.Platform;
using StudentSync.Domain.Tenancy;
using StudentSync.Infrastructure.Persistence;

namespace StudentSync.Infrastructure.Identity;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "StudentSync";
    public string Audience { get; set; } = "StudentSync";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;

    public SymmetricSecurityKey Key() => new(Encoding.UTF8.GetBytes(SigningKey));
}

public sealed class AuthException(string message, string code, int status = 401) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed class AuthService(AppDbContext db, UserManager<AppUser> users, IOptions<JwtOptions> options, IClock clock, ICurrentUser current, PhotoUrls photos)
{
    private readonly JwtOptions _jwt = options.Value;

    public async Task<AuthResponse> LoginAsync(LoginRequest r, string? ip, CancellationToken ct)
    {
        var email = (r.Email ?? "").Trim();
        if (email.Length == 0 || string.IsNullOrEmpty(r.Password)) throw new AuthException("Enter your email and password.", "invalid_credentials", 400);
        var normalized = users.NormalizeEmail(email);
        var candidates = await db.Users.Where(u => u.NormalizedEmail == normalized).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(r.InstitutionCode))
        {
            var slug = r.InstitutionCode.Trim().ToLowerInvariant();
            var tenantId = await db.Tenants.Where(t => t.Slug == slug).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
            candidates = candidates.Where(u => u.TenantId == tenantId).ToList();
        }
        if (candidates.Count > 1)
            throw new AuthException("This email is used at more than one institution. Enter your institution code.", "institution_code_required", 400);
        var user = candidates.SingleOrDefault();

        // Same message for unknown email, wrong password and inactive account to avoid account enumeration.
        const string invalid = "The email or password is incorrect.";
        if (user is null) throw new AuthException(invalid, "invalid_credentials");
        if (await users.IsLockedOutAsync(user)) throw new AuthException("Too many failed attempts. Try again in 15 minutes.", "locked_out", 423);
        if (!await users.CheckPasswordAsync(user, r.Password))
        {
            await users.AccessFailedAsync(user);
            await AuditAsync(user, "auth.login_failed", ip, ct);
            throw new AuthException(invalid, "invalid_credentials");
        }
        if (!user.IsActive) throw new AuthException(invalid, "invalid_credentials");
        EnsureRoleInUse(user);
        await EnsureTenantActiveAsync(user, ct);

        // Two devices signing in at the same moment must both succeed, so the sign-in bookkeeping (last login, failed-attempt
        // counter) is one direct column update rather than an Identity UpdateAsync, which checks the concurrency stamp and
        // would leave the tracked user dirty after a conflict.
        var loginAt = clock.UtcNow;
        await db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAtUtc, loginAt).SetProperty(u => u.AccessFailedCount, 0), ct);
        user.LastLoginAtUtc = loginAt;
        user.AccessFailedCount = 0;
        db.Entry(user).State = EntityState.Unchanged;
        var response = await IssueAsync(user, Guid.NewGuid(), ip, r.ClientName, ct);
        await AuditAsync(user, "auth.login", ip, ct);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest r, string? ip, CancellationToken ct)
    {
        var hash = Hash(r.RefreshToken ?? "");
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct) ?? throw new AuthException("Your session has expired. Please sign in again.", "invalid_refresh_token");
        var now = clock.UtcNow;
        if (token.RevokedAtUtc is not null)
        {
            if (token.ReplacedByTokenId is not null)
            {
                // A rotated token was presented again: treat as theft and revoke the whole family.
                await db.RefreshTokens.Where(t => t.FamilyId == token.FamilyId && t.RevokedAtUtc == null)
                    .ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAtUtc, now).SetProperty(t => t.RevokedReason, "reuse_detected"), ct);
            }
            throw new AuthException("Your session has expired. Please sign in again.", "invalid_refresh_token");
        }
        if (token.ExpiresAtUtc <= now) throw new AuthException("Your session has expired. Please sign in again.", "invalid_refresh_token");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (user is null || !user.IsActive) throw new AuthException("Your account is not active.", "invalid_refresh_token");
        EnsureRoleInUse(user);
        await EnsureTenantActiveAsync(user, ct);

        var response = await IssueAsync(user, token.FamilyId, ip, token.ClientName, ct, previous: token);
        return response;
    }

    public async Task LogoutAsync(LogoutRequest r, CancellationToken ct)
    {
        var hash = Hash(r.RefreshToken ?? "");
        await db.RefreshTokens.Where(t => t.TokenHash == hash && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAtUtc, clock.UtcNow).SetProperty(t => t.RevokedReason, "logout"), ct);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest r, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(current.UserId.ToString()) ?? throw new AuthException("Not signed in.", "unauthenticated");
        var result = await users.ChangePasswordAsync(user, r.CurrentPassword ?? "", r.NewPassword ?? "");
        if (!result.Succeeded)
            throw new RequestValidationException(new Dictionary<string, string[]> { ["newPassword"] = result.Errors.Select(e => e.Description).ToArray() });
        user.MustChangePassword = false;
        await users.UpdateAsync(user);
        // Sign out every other session.
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAtUtc, clock.UtcNow).SetProperty(t => t.RevokedReason, "password_changed"), ct);
        await AuditAsync(user, "auth.password_changed", current.IpAddress, ct);
    }

    public async Task<CurrentUserDto> MeAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new AuthException("Not signed in.", "unauthenticated");
        return await ToDtoAsync(user, ct);
    }

    public async Task<CurrentUserDto> UpdateProfileAsync(UpdateProfileRequest r, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == current.UserId, ct);
        var name = Text.Required(r.FullName, "fullName", 150);
        if (!Domain.Scheduling.ZonedTime.TryFind(r.TimeZoneId, out _)) throw RequestValidationException.For("timeZoneId", "Choose a valid time zone.");
        user.FullName = name;
        user.TimeZoneId = r.TimeZoneId;
        user.PreferredLanguage = r.PreferredLanguage is "ar" or "ml" ? r.PreferredLanguage : "en";
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(user, ct);
    }

    private async Task<CurrentUserDto> ToDtoAsync(AppUser user, CancellationToken ct)
    {
        string? tenantName = null;
        var features = new List<string>();
        if (user.TenantId is { } tid)
        {
            tenantName = await db.Tenants.Where(t => t.Id == tid).Select(t => t.Name).FirstOrDefaultAsync(ct);
            features = await db.Subscriptions.IgnoreQueryFilters().Where(s => s.TenantId == tid)
                .SelectMany(s => s.Plan!.Features.Where(f => f.Enabled).Select(f => f.FeatureKey)).ToListAsync(ct);
        }
        return new CurrentUserDto(user.Id, user.FullName, user.Email!, user.Role, user.TenantId, tenantName, user.TimeZoneId, user.PreferredLanguage,
            user.MustChangePassword, RolePermissions.For(user.Role).Order().ToList(), features,
            // Built for the account itself: this also runs while signing in, before the request is authenticated.
            photos.For(user.PhotoFileId, user.Id, user.TenantId));
    }

    private async Task EnsureTenantActiveAsync(AppUser user, CancellationToken ct)
    {
        if (user.TenantId is not { } tid) return;
        var status = await db.Tenants.Where(t => t.Id == tid).Select(t => t.Status).FirstAsync(ct);
        if (status != TenantStatus.Active) throw new AuthException("Your institution's account is not active. Contact your administrator.", "tenant_inactive", 403);
    }

    private async Task<AuthResponse> IssueAsync(AppUser user, Guid familyId, string? ip, string? clientName, CancellationToken ct, RefreshToken? previous = null)
    {
        var now = clock.UtcNow;
        var accessExpires = now.AddMinutes(_jwt.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimNames.Role, user.Role.ToString()),
            new(ClaimNames.TimeZone, user.TimeZoneId),
            new("name", user.FullName),
        };
        if (user.TenantId is { } tid) claims.Add(new Claim(ClaimNames.Tenant, tid.ToString()));
        if (user.MustChangePassword) claims.Add(new Claim(ClaimNames.MustChangePassword, "1"));

        var jwt = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, now, accessExpires, new SigningCredentials(_jwt.Key(), SecurityAlgorithms.HmacSha256));
        var access = new JwtSecurityTokenHandler().WriteToken(jwt);

        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var refresh = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(raw),
            FamilyId = familyId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(_jwt.RefreshTokenDays),
            CreatedByIp = ip,
            ClientName = clientName is { Length: > 100 } c ? c[..100] : clientName,
        };
        db.RefreshTokens.Add(refresh);
        if (previous is not null)
        {
            previous.RevokedAtUtc = now;
            previous.RevokedReason = "rotated";
            previous.ReplacedByTokenId = refresh.Id;
        }
        await db.SaveChangesAsync(ct);
        return new AuthResponse(access, accessExpires, raw, refresh.ExpiresAtUtc, await ToDtoAsync(user, ct));
    }

    // The institute runs without a parent portal (admin, teachers and students only). Parent data and code are kept
    // so the portal can be switched back on by removing this check.
    private static void EnsureRoleInUse(AppUser user)
    {
        if (user.Role == UserRole.Parent)
            throw new AuthException("Parent accounts are not used at this institute. Please contact the institute office.", "parent_access_disabled", 403);
    }

    private async Task AuditAsync(AppUser user, string action, string? ip, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog { TenantId = user.TenantId, UserId = user.Id, Action = action, EntityType = "User", EntityId = user.Id.ToString(), IpAddress = ip, CreatedAtUtc = clock.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed class UserAccountService(UserManager<AppUser> users, AppDbContext db, IClock clock) : IUserAccountService
{
    public async Task<CreatedAccount> CreateAsync(Guid? tenantId, string email, string fullName, UserRole role, string timeZoneId, string? password, bool isDemo, CancellationToken ct)
    {
        email = (email ?? "").Trim();
        if (!System.Net.Mail.MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || email.Length > 256)
            throw RequestValidationException.For("email", "Enter a valid email address.");
        var normalized = users.NormalizeEmail(email);
        if (await db.Users.AnyAsync(u => u.TenantId == tenantId && u.NormalizedEmail == normalized, ct))
            throw RequestValidationException.For("email", "An account with this email already exists in this institution.");

        var temporary = password is null;
        password ??= GeneratePassword();
        var user = new AppUser
        {
            UserName = $"{(tenantId is { } t ? t.ToString("N") : "platform")}.{email}",
            Email = email,
            EmailConfirmed = false,
            FullName = fullName,
            Role = role,
            TenantId = tenantId,
            TimeZoneId = timeZoneId,
            IsActive = true,
            MustChangePassword = temporary,
            IsDemo = isDemo,
            CreatedAtUtc = clock.UtcNow,
            LockoutEnabled = true,
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new RequestValidationException(new Dictionary<string, string[]> { ["account"] = result.Errors.Select(e => e.Description).ToArray() });
        return new CreatedAccount(user.Id, temporary ? password : null);
    }

    public async Task SetActiveAsync(Guid userId, bool active, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        user.IsActive = active;
        if (!active)
            await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAtUtc == null)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAtUtc, clock.UtcNow).SetProperty(t => t.RevokedReason, "deactivated"), ct);
    }

    public async Task DeleteAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        var result = await users.DeleteAsync(user);
        if (!result.Succeeded) throw new InvalidOperationException("The account could not be removed.");
    }

    public async Task<string> ResetPasswordAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("Account");
        var password = GeneratePassword();
        // Remove + add rather than a reset token: no token providers are registered, and the admin is already authorised.
        var removed = await users.RemovePasswordAsync(user);
        if (!removed.Succeeded) throw new InvalidOperationException("The password could not be reset.");
        var result = await users.AddPasswordAsync(user, password);
        if (!result.Succeeded) throw new InvalidOperationException("The password could not be reset.");
        user.MustChangePassword = true;
        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        await users.UpdateAsync(user);
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAtUtc, clock.UtcNow).SetProperty(t => t.RevokedReason, "password_reset"), ct);
        return password;
    }

    /// <summary>Random temporary password meeting the policy (upper, lower, digit, symbol, 16 chars).</summary>
    public static string GeneratePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", lower = "abcdefghijkmnpqrstuvwxyz", digits = "23456789", symbols = "!@#$%*?";
        var all = upper + lower + digits + symbols;
        var chars = new List<char>
        {
            upper[RandomNumberGenerator.GetInt32(upper.Length)], lower[RandomNumberGenerator.GetInt32(lower.Length)],
            digits[RandomNumberGenerator.GetInt32(digits.Length)], symbols[RandomNumberGenerator.GetInt32(symbols.Length)],
        };
        while (chars.Count < 16) chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);
        return new string(chars.OrderBy(_ => RandomNumberGenerator.GetInt32(1000)).ToArray());
    }
}
