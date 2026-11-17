using Microsoft.AspNetCore.Identity;
using StudentSync.Domain.Identity;

namespace StudentSync.Infrastructure.Identity;

/// <summary>
/// Owner decision 2026-11-17: passwords need only 6 characters (no upper/lower-case or number rules). To keep short
/// passwords from being trivially guessed, the most common ones, one character repeated, and the person's own email name
/// are refused. Account lockout (5 tries) and sign-in rate limits stay in place.
/// </summary>
public sealed class CommonPasswordValidator : IPasswordValidator<AppUser>
{
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456", "1234567", "12345678", "123456789", "1234567890", "654321", "111111", "000000", "123123", "112233", "121212",
        "password", "password1", "passw0rd", "qwerty", "qwerty123", "abc123", "abcdef", "abcd1234", "iloveyou", "welcome",
        "admin", "admin123", "letmein", "student", "student1", "teacher", "teacher1", "school", "tuition", "studentsync",
    };

    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        if (string.IsNullOrEmpty(password)) return Task.FromResult(IdentityResult.Success);
        var p = password.Trim();
        var emailName = user.Email?.Split('@')[0];
        var weak = Common.Contains(p) || p.Distinct().Count() == 1
                   || (!string.IsNullOrEmpty(emailName) && string.Equals(p, emailName, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(weak
            ? IdentityResult.Failed(new IdentityError { Code = "PasswordTooCommon", Description = "This password is too easy to guess. Choose another one." })
            : IdentityResult.Success);
    }
}
