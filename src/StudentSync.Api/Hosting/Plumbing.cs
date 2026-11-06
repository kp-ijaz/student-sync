using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Application.Jobs;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Platform;
using StudentSync.Infrastructure.Identity;
using StudentSync.Infrastructure.Persistence;

namespace StudentSync.Api.Hosting;

// ---------------- Authorization ----------------

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionHandler(ICurrentUser user) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (user.Has(requirement.Permission)) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Creates "perm:{name}" policies on demand so endpoints can declare fine-grained permissions.</summary>
public sealed class PermissionPolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith("perm:", StringComparison.Ordinal)) return await base.GetPolicyAsync(policyName);
        return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(policyName[5..])).Build();
    }
}

public static class EndpointExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission) where TBuilder : IEndpointConventionBuilder
        => builder.RequireAuthorization("perm:" + permission);
}

/// <summary>Accounts with a temporary password may only use authentication endpoints until it is changed.</summary>
public sealed class PasswordChangeGate(RequestDelegate next)
{
    private static readonly string[] Allowed = ["/api/v1/auth", "/api/v1/me", "/hubs"];

    public Task InvokeAsync(HttpContext http)
    {
        if (http.User.HasClaim(ClaimNames.MustChangePassword, "1") && !Allowed.Any(p => http.Request.Path.StartsWithSegments(p)))
        {
            http.Response.StatusCode = 403;
            return http.Response.WriteAsJsonAsync(new { title = "Change your temporary password to continue.", status = 403, code = "password_change_required" });
        }
        return next(http);
    }
}

public sealed class SecurityHeaders(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http)
    {
        var h = http.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        if (!http.Request.Path.StartsWithSegments("/swagger") && !http.Request.Path.StartsWithSegments("/hangfire"))
            h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; media-src 'self'";
        return next(http);
    }
}

// ---------------- Real time ----------------

/// <summary>Per-user real-time channel. Connections join a group keyed by their own user id only.</summary>
[Authorize]
public sealed class NotificationHub(ICurrentUser user) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(user.UserId));
        await base.OnConnectedAsync();
    }

    public static string GroupFor(Guid userId) => $"user:{userId:N}";
}

public sealed class SignalRNotifier(IHubContext<NotificationHub> hub) : IRealtimeNotifier
{
    public async Task SendToUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload, CancellationToken ct = default)
    {
        foreach (var id in userIds.Distinct())
            await hub.Clients.Group(NotificationHub.GroupFor(id)).SendAsync(eventName, payload, ct);
    }
}

/// <summary>After commit: push each notification in real time and schedule external delivery.</summary>
public sealed class NotificationDispatcher(IHubContext<NotificationHub> hub, IBackgroundJobs jobs, ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    public void Dispatch(IReadOnlyList<Notification> committed)
    {
        foreach (var n in committed)
        {
            _ = hub.Clients.Group(NotificationHub.GroupFor(n.UserId)).SendAsync("notification", new { n.Id, n.Title, n.Body, n.Link, Type = n.Type.ToString(), n.CreatedAtUtc });
            try { jobs.Enqueue<NotificationDeliveryJob>(j => j.DeliverAsync(n.Id, CancellationToken.None)); }
            catch (Exception ex) { logger.LogError(ex, "Could not schedule delivery for notification {Id}", n.Id); }
        }
    }
}

/// <summary>Runs jobs in-process immediately (tests / environments without Hangfire). Delayed jobs are skipped and logged.</summary>
public sealed class InlineBackgroundJobs(IServiceScopeFactory scopes, ILogger<InlineBackgroundJobs> logger) : IBackgroundJobs
{
    public string Enqueue<T>(Expression<Func<T, Task>> job)
    {
        var compiled = job.Compile();
        // Run after the current request's transaction has committed, in its own scope.
        _ = Task.Run(async () =>
        {
            using var scope = scopes.CreateScope();
            try { await compiled((T)scope.ServiceProvider.GetRequiredService(typeof(T))); }
            catch (Exception ex) { logger.LogError(ex, "Inline job {Job} failed", typeof(T).Name); }
        });
        return Guid.NewGuid().ToString("N");
    }

    public string Schedule<T>(Expression<Func<T, Task>> job, TimeSpan delay)
    {
        logger.LogInformation("Inline job mode: delayed job {Job} ({Delay}) not scheduled", typeof(T).Name, delay);
        return "";
    }
}

// ---------------- Idempotency ----------------

/// <summary>
/// Honours an "Idempotency-Key" header on retried commands: the first result is stored per (user, key) and replayed.
/// Reusing a key with a different request body is rejected.
/// </summary>
public sealed class IdempotencyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var key = http.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key)) return await next(ctx);
        if (key.Length > 100) return Results.Problem("Idempotency-Key is too long.", statusCode: 400);

        var user = http.RequestServices.GetRequiredService<ICurrentUser>();
        var db = http.RequestServices.GetRequiredService<AppDbContext>();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(http.Request.Path + "|" + JsonSerializer.Serialize(ctx.Arguments.Where(a => a is not CancellationToken)))));
        var existing = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == user.UserId && r.Key == key);
        if (existing is not null)
        {
            if (existing.RequestHash != hash) return Results.Problem("This Idempotency-Key was already used for a different request.", statusCode: 422);
            return Results.Content(existing.ResponseBody, "application/json", Encoding.UTF8, existing.StatusCode);
        }

        var result = await next(ctx);
        var (status, body) = result switch
        {
            IStatusCodeHttpResult { StatusCode: { } sc } r and IValueHttpResult v => (sc, JsonSerializer.Serialize(v.Value, JsonSerializerOptions.Web)),
            IStatusCodeHttpResult { StatusCode: { } sc } => (sc, "null"),
            null => (204, "null"),
            _ => (200, JsonSerializer.Serialize(result, JsonSerializerOptions.Web)),
        };
        if (status < 300)
        {
            db.ChangeTracker.Clear();
            db.IdempotencyRecords.Add(new IdempotencyRecord { UserId = user.UserId, Key = key, RequestHash = hash, StatusCode = status, ResponseBody = body, CreatedAtUtc = DateTime.UtcNow });
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { /* concurrent duplicate: first writer wins */ }
        }
        return result;
    }
}

// ---------------- Hangfire ----------------

/// <summary>Hangfire dashboard is only reachable from the local machine (development diagnostics).</summary>
public sealed class LocalOnlyDashboardFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        var http = context.GetHttpContext();
        var ip = http.Connection.RemoteIpAddress;
        return ip is not null && System.Net.IPAddress.IsLoopback(ip);
    }
}

public static class RecurringJobs
{
    public static void Register(IRecurringJobManager jobs)
    {
        jobs.AddOrUpdate<MaintenanceJobs>("session-reminders", j => j.SessionRemindersAsync(CancellationToken.None), "*/5 * * * *");
        jobs.AddOrUpdate<MaintenanceJobs>("doubt-follow-up", j => j.DoubtFollowUpAsync(CancellationToken.None), "15 * * * *");
        jobs.AddOrUpdate<MaintenanceJobs>("assignment-reminders", j => j.AssignmentRemindersAsync(CancellationToken.None), "0 * * * *");
        jobs.AddOrUpdate<MaintenanceJobs>("exam-finalise", j => j.FinaliseExpiredExamsAsync(CancellationToken.None), "*/5 * * * *");
        jobs.AddOrUpdate<MaintenanceJobs>("recording-retention", j => j.RecordingRetentionAsync(CancellationToken.None), "30 2 * * *");
        jobs.AddOrUpdate<MaintenanceJobs>("weekly-digest", j => j.WeeklyDigestAsync(CancellationToken.None), "0 16 * * 5");
    }
}

// ---------------- Development environment ----------------

/// <summary>Development only: loads the git-ignored repository .env so `dotnet run` works without exporting variables.</summary>
public static class DevEnvironment
{
    public static void Load(string contentRoot)
    {
        var dir = new DirectoryInfo(contentRoot);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, ".env"))) dir = dir.Parent;
        if (dir is null) return;
        var values = File.ReadAllLines(Path.Combine(dir.FullName, ".env"))
            .Where(l => l.Contains('=') && !l.TrimStart().StartsWith('#'))
            .Select(l => l.Split('=', 2)).ToDictionary(p => p[0].Trim(), p => p[1].Trim());
        void Set(string name, string? value) { if (value is not null && Environment.GetEnvironmentVariable(name) is null) Environment.SetEnvironmentVariable(name, value); }
        if (values.TryGetValue("MSSQL_SA_PASSWORD", out var pw))
            Set("ConnectionStrings__Default", $"Server=localhost,{values.GetValueOrDefault("SQL_PORT", "14350")};Database=StudentSync;User Id=sa;Password={pw};TrustServerCertificate=True");
        Set("Jwt__SigningKey", values.GetValueOrDefault("JWT_SIGNING_KEY"));
        Set("Demo__Password", values.GetValueOrDefault("DEMO_PASSWORD"));
        Set("Demo__ForcePasswordChange", values.GetValueOrDefault("DEMO_FORCE_PASSWORD_CHANGE"));
        Set("Ai__Anthropic__ApiKey", values.GetValueOrDefault("ANTHROPIC_API_KEY"));
    }
}
