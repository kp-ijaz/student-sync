using StudentSync.Application.Academics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Application.Platform;
using StudentSync.Contracts;
using StudentSync.Domain.Identity;
using StudentSync.Infrastructure.Persistence;

namespace StudentSync.Api.Hosting;

/// <summary>
/// <c>create-institution</c>: one-off set-up of the institute and its first administrator on an empty database.
/// It is refused as soon as any institution exists, so it cannot be used to add accounts to a running system.
/// The administrator always starts with a temporary password that must be changed at first sign-in: either one
/// generated here and printed once, or one supplied through <c>Bootstrap:AdminPassword</c> (environment variable
/// <c>Bootstrap__AdminPassword</c>, never a command-line argument, so it stays out of shell history and process lists).
/// </summary>
public static class CreateInstitutionCommand
{
    public const string Name = "create-institution";
    public const string AllowWeakFlag = "--allow-weak-temporary-password";

    public const string Usage =
        "create-institution --name <institute name> --code <sign-in code> --time-zone <IANA id> --country <ISO 3166 alpha-2> " +
        "--currency <ISO 4217> --admin-email <email> [--admin-name <full name>] [--plan <plan code>] [--language en|ar|ml] " +
        "[" + AllowWeakFlag + "]";

    /// <summary>Returns the process exit code: 0 created, 1 refused or invalid.</summary>
    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output, CancellationToken ct)
    {
        try
        {
            var options = Parse(args);
            string Required(string key) => options.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
                ? v
                : throw RequestValidationException.For(key, $"--{key} is required.");

            var db = services.GetRequiredService<AppDbContext>();
            var users = services.GetRequiredService<UserManager<AppUser>>();
            var planCode = options.GetValueOrDefault("plan", "enterprise");
            var plan = await db.Plans.FirstOrDefaultAsync(p => p.Code == planCode && p.IsActive, ct)
                ?? throw RequestValidationException.For("plan", $"No active plan with code '{planCode}'.");
            var request = new CreateTenantRequest(
                Required("name"), Required("code"), Required("country"), Required("time-zone"), Required("currency"),
                options.GetValueOrDefault("language", "en"), ContactEmail: null, plan.Id,
                options.GetValueOrDefault("admin-name", "Administrator"), Required("admin-email"));

            // Check a supplied password before anything is created: the set-up cannot be repeated once the institution exists.
            var supplied = services.GetRequiredService<IConfiguration>()["Bootstrap:AdminPassword"];
            if (string.IsNullOrEmpty(supplied)) supplied = null;
            var weaknesses = supplied is null ? [] : await PolicyViolationsAsync(users, supplied);
            if (weaknesses.Count > 0 && !options.ContainsKey(AllowWeakFlag[2..]))
            {
                output.WriteLine("The supplied temporary password does not meet the password policy:");
                foreach (var w in weaknesses) output.WriteLine($"  - {w}");
                output.WriteLine($"Choose a stronger one, omit it to have one generated, or pass {AllowWeakFlag}.");
                return 1;
            }

            var created = await services.GetRequiredService<PlatformService>().BootstrapFirstInstitutionAsync(request, ct);

            var passwordNote = "Temporary password (shown once): " + created.AdminTemporaryPassword;
            if (supplied is not null)
            {
                var admin = await users.FindByIdAsync(created.AdminUserId.ToString()) ?? throw new InvalidOperationException("The administrator account was not found after creation.");
                // Hashed directly because the policy check above may have been waived for this must-change password.
                admin.PasswordHash = users.PasswordHasher.HashPassword(admin, supplied);
                admin.MustChangePassword = true;
                var updated = await users.UpdateAsync(admin);
                if (updated.Succeeded)
                    passwordNote = "Temporary password: the one supplied in Bootstrap:AdminPassword" + (weaknesses.Count > 0 ? " (weaker than the password policy; change it straight away)" : "");
                else
                    output.WriteLine("The supplied password could not be set; use the generated one below.");
            }

            // Start the institute with the default syllabi (NCERT, Kerala State, ICSE), Classes 1–12, their subjects and
            // the current academic year; the admin can archive or add to them under Academics.
            using (services.GetRequiredService<ITenantContext>().BeginScope(created.TenantId))
            {
                var loaded = await services.GetRequiredService<DefaultCatalogue>().LoadForCurrentTenantAsync(ct);
                output.WriteLine($"Default catalogue: {loaded.Syllabi} syllabi, {loaded.Classes} classes, {loaded.Subjects} subjects, {loaded.AcademicYears} academic year.");
            }
            output.WriteLine($"Institution '{request.Name}' created with sign-in code '{request.Slug.ToLowerInvariant()}'.");
            output.WriteLine($"Administrator: {request.AdminEmail}");
            output.WriteLine(passwordNote);
            output.WriteLine("The administrator must choose a new password at first sign-in.");
            return 0;
        }
        catch (ConflictException ex)
        {
            output.WriteLine(ex.Message);
            return 1;
        }
        catch (RequestValidationException ex)
        {
            foreach (var (field, messages) in ex.Errors) output.WriteLine($"{field}: {string.Join(" ", messages)}");
            output.WriteLine("Usage: " + Usage);
            return 1;
        }
    }

    private static async Task<List<string>> PolicyViolationsAsync(UserManager<AppUser> users, string password)
    {
        var problems = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, new AppUser(), password);
            problems.AddRange(result.Errors.Select(e => e.Description));
        }
        return problems;
    }

    /// <summary>"--key value" pairs; a "--flag" followed by another option or nothing is stored with an empty value.</summary>
    private static Dictionary<string, string> Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw RequestValidationException.For(args[i], "Unexpected argument.");
            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            options[args[i][2..]] = hasValue ? args[++i] : "";
        }
        return options;
    }
}
