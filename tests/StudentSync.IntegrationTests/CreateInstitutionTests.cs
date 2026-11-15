using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StudentSync.Api.Hosting;
using StudentSync.Contracts;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Tenancy;
using StudentSync.Infrastructure.Persistence;

namespace StudentSync.IntegrationTests;

/// <summary>
/// The <c>create-institution</c> maintenance command. Each test runs the API against its own empty, migrated
/// database on the fixture's PostgreSQL server (the shared test database already holds the demo institutions).
/// </summary>
public class CreateInstitutionTests(ApiFixture fx)
{
    private const string WeakPassword = "Short123"; // 8 characters; the policy requires 10
    private static readonly string[] Args =
    [
        "create-institution", "--name", "Bootstrap Test Institute", "--code", "Boot-Test", "--time-zone", "Asia/Dubai",
        "--country", "ae", "--currency", "aed", "--admin-email", "owner@bootstrap.example.test", "--admin-name", "Olive Owner",
    ];

    private sealed class EmptyDatabase(WebApplicationFactory<Program> factory) : IAsyncDisposable
    {
        public WebApplicationFactory<Program> Factory => factory;

        public async Task<(int Exit, string Output)> RunAsync(params string[] extraArgs)
        {
            using var scope = factory.Services.CreateScope();
            var output = new StringWriter();
            var exit = await CreateInstitutionCommand.RunAsync(scope.ServiceProvider, [.. Args, .. extraArgs], output, TestContext.Current.CancellationToken);
            return (exit, output.ToString());
        }

        public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
        {
            using var scope = factory.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        public async Task<HttpResponseMessage> LoginAsync(string password)
            => await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("owner@bootstrap.example.test", password, null, "tests"), ApiFixture.Json);

        public async ValueTask DisposeAsync()
        {
            try { await QueryAsync(db => db.Database.EnsureDeletedAsync()); } catch { /* best effort */ }
            await factory.DisposeAsync();
        }
    }

    private EmptyDatabase Start(string? suppliedPassword)
    {
        var connection = new NpgsqlConnectionStringBuilder(fx.ConnectionString) { Database = $"StudentSync_Test_Bootstrap_{Guid.NewGuid():N}" }.ConnectionString;
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Default", connection);
            b.UseSetting("Jobs:Mode", "Inline");
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Jwt:SigningKey", "bootstrap-tests-signing-key-0123456789-abcdefghijklmn");
            b.UseSetting("RateLimits:AuthPerMinute", "10000");
            b.UseSetting("Email:Smtp:Host", "");
            if (suppliedPassword is not null) b.UseSetting("Bootstrap:AdminPassword", suppliedPassword);
        });
        _ = factory.CreateClient(); // starts the host, migrates and seeds reference data
        return new EmptyDatabase(factory);
    }

    [Fact]
    public async Task Supplied_weak_password_needs_the_flag_then_is_temporary_and_setup_cannot_be_repeated()
    {
        await using var db = Start(WeakPassword);

        // Refused without the explicit flag, and nothing is created.
        var refused = await db.RunAsync();
        Assert.Equal(1, refused.Exit);
        Assert.Contains("does not meet the password policy", refused.Output);
        Assert.Equal(0, await db.QueryAsync(d => d.Tenants.CountAsync()));
        Assert.Equal(0, await db.QueryAsync(d => d.Users.CountAsync()));

        // With the flag: one active institution and one administrator; the password is never echoed.
        var created = await db.RunAsync(CreateInstitutionCommand.AllowWeakFlag);
        Assert.True(created.Exit == 0, created.Output);
        Assert.DoesNotContain(WeakPassword, created.Output);
        var tenant = await db.QueryAsync(d => d.Tenants.SingleAsync());
        Assert.Equal(("Bootstrap Test Institute", "boot-test", "AE", "AED", "Asia/Dubai", false), (tenant.Name, tenant.Slug, tenant.CountryCode, tenant.CurrencyCode, tenant.DefaultTimeZoneId, tenant.IsDemo));
        var subscription = await db.QueryAsync(d => d.Subscriptions.IgnoreQueryFilters().Include(s => s.Plan).SingleAsync());
        Assert.Equal((SubscriptionStatus.Active, "enterprise", null), (subscription.Status, subscription.Plan!.Code, subscription.CurrentPeriodEndUtc));
        Assert.Equal(1, await db.QueryAsync(d => d.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.Action == "tenant.bootstrapped")));

        // The supplied password signs in, but only to change it.
        var login = await db.LoginAsync(WeakPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(ApiFixture.Json))!;
        Assert.Equal((UserRole.InstitutionAdmin, true, tenant.Id), (auth.User.Role, auth.User.MustChangePassword, auth.User.TenantId));
        var http = db.Factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var blocked = await http.GetAsync("/api/v1/students");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("password_change_required", await Client.Code(blocked));

        // The real password must meet the policy; afterwards the account works normally and the old one is gone.
        var weakAgain = await http.PostAsJsonAsync("/api/v1/me/password", new ChangePasswordRequest(WeakPassword, "Weak1234"), ApiFixture.Json);
        Assert.Equal(HttpStatusCode.BadRequest, weakAgain.StatusCode);
        const string strong = "A-Much-Longer-Passw0rd";
        var changed = await http.PostAsJsonAsync("/api/v1/me/password", new ChangePasswordRequest(WeakPassword, strong), ApiFixture.Json);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await db.LoginAsync(WeakPassword)).StatusCode);
        var relogin = (await (await db.LoginAsync(strong)).Content.ReadFromJsonAsync<AuthResponse>(ApiFixture.Json))!;
        Assert.False(relogin.User.MustChangePassword);

        // A second run is refused and changes nothing.
        var repeat = await db.RunAsync(CreateInstitutionCommand.AllowWeakFlag);
        Assert.Equal(1, repeat.Exit);
        Assert.Contains("already exists", repeat.Output);
        Assert.Equal(1, await db.QueryAsync(d => d.Tenants.CountAsync()));
        Assert.Equal(1, await db.QueryAsync(d => d.Users.CountAsync()));
    }

    [Fact]
    public async Task Without_a_supplied_password_a_temporary_one_is_generated_and_missing_options_are_reported()
    {
        await using var db = Start(suppliedPassword: null);

        using (var scope = db.Factory.Services.CreateScope())
        {
            var output = new StringWriter();
            var exit = await CreateInstitutionCommand.RunAsync(scope.ServiceProvider, ["create-institution", "--name", "No Code Institute"], output, TestContext.Current.CancellationToken);
            Assert.Equal(1, exit);
            Assert.Contains("--code is required", output.ToString());
        }
        var badZone = await db.RunAsync("--time-zone", "Mars/Olympus");
        Assert.Equal(1, badZone.Exit);
        var stray = await db.RunAsync("unexpected");
        Assert.Equal(1, stray.Exit);
        Assert.Contains("Usage:", stray.Output);
        Assert.Equal(0, await db.QueryAsync(d => d.Tenants.CountAsync()));

        var created = await db.RunAsync();
        Assert.True(created.Exit == 0, created.Output);
        const string marker = "Temporary password (shown once): ";
        var line = created.Output.Split('\n').Single(l => l.StartsWith(marker, StringComparison.Ordinal));
        var generated = line[marker.Length..].Trim();
        var auth = (await (await db.LoginAsync(generated)).Content.ReadFromJsonAsync<AuthResponse>(ApiFixture.Json))!;
        Assert.True(auth.User.MustChangePassword);
        Assert.Equal("Olive Owner", auth.User.FullName);
    }

    [Fact]
    public async Task Is_refused_on_a_database_that_already_has_institutions()
    {
        using var scope = fx.Factory.Services.CreateScope();
        var before = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tenants.CountAsync(TestContext.Current.CancellationToken);
        var output = new StringWriter();
        var exit = await CreateInstitutionCommand.RunAsync(scope.ServiceProvider, Args, output, TestContext.Current.CancellationToken);
        Assert.Equal(1, exit);
        Assert.Contains("already exists", output.ToString());
        Assert.Equal(before, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tenants.CountAsync(TestContext.Current.CancellationToken));
    }
}
