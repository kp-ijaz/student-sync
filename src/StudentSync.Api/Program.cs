using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using StudentSync.Api.Endpoints;
using StudentSync.Api.Hosting;
using StudentSync.Application.Common;
using StudentSync.Infrastructure;
using StudentSync.Infrastructure.Identity;
using StudentSync.Infrastructure.Persistence;
using StudentSync.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    DevEnvironment.Load(builder.Environment.ContentRootPath);
    builder.Configuration.AddEnvironmentVariables();
}

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext().WriteTo.Console());

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
builder.Services.AddScoped<IRealtimeNotifier, SignalRNotifier>();

var jobsMode = builder.Configuration["Jobs:Mode"] ?? "Hangfire";
if (jobsMode == "Inline")
{
    builder.Services.AddSingleton<InlineBackgroundJobs>();
    builder.Services.AddScoped<IBackgroundJobs>(sp => sp.GetRequiredService<InlineBackgroundJobs>());
}
else
{
    builder.Services.AddHangfire(cfg => cfg
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseSqlServerStorage(builder.Configuration.GetConnectionString("Default"), new SqlServerStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));
    builder.Services.AddHangfireServer(o => o.WorkerCount = Math.Max(2, Environment.ProcessorCount));
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<IOptions<JwtOptions>>((o, jwt) =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Value.Issuer,
        ValidAudience = jwt.Value.Audience,
        IssuerSigningKey = jwt.Value.Key(),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "name",
        RoleClaimType = ClaimNames.Role,
    };
    // SignalR sends the token on the query string for WebSocket connections.
    o.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs")) ctx.Token = token;
            return Task.CompletedTask;
        },
    };
});
builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:AuthPerMinute", 20), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("ai", http => RateLimitPartition.GetFixedWindowLimiter(http.User.FindFirst("sub")?.Value ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 2_000_000_000);

var app = builder.Build();

// ---------- Command-line maintenance commands ----------
if (args.Length > 0 && args[0] is "migrate" or "seed-demo" or "reset-demo")
{
    await RunCommandAsync(app, args);
    return;
}

if (app.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<ReferenceDataSeeder>().SeedAsync(CancellationToken.None);
}

app.UseExceptionHandler();
app.UseMiddleware<SecurityHeaders>();
app.UseSerilogRequestLogging();
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<PasswordChangeGate>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi().AllowAnonymous();
app.UseSwaggerUI(o => { o.SwaggerEndpoint("/openapi/v1.json", "Student Sync API v1"); o.DocumentTitle = "Student Sync API"; });
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.MapHub<NotificationHub>("/hubs/notifications");

if (jobsMode != "Inline")
{
    if (app.Environment.IsDevelopment())
        app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [new LocalOnlyDashboardFilter()] });
    RecurringJobs.Register(app.Services.GetRequiredService<IRecurringJobManager>());
}

var api = app.MapGroup("/api/v1");
api.MapAuth();
api.MapPlatform();
api.MapInstitution();
api.MapPeople();
api.MapAcademics();
api.MapSessions();
api.MapContent();
api.MapMySubjects();
api.MapAssessments();
api.MapDoubts();
api.MapCommunication();
api.MapAi();
api.MapReporting();

app.Run();

static async Task RunCommandAsync(WebApplication app, string[] args)
{
    using var scope = app.Services.CreateScope();
    var sp = scope.ServiceProvider;
    var db = sp.GetRequiredService<AppDbContext>();
    var log = sp.GetRequiredService<ILogger<Program>>();
    switch (args[0])
    {
        case "migrate":
            await db.Database.MigrateAsync();
            await sp.GetRequiredService<ReferenceDataSeeder>().SeedAsync(CancellationToken.None);
            log.LogInformation("Database migrated.");
            break;
        case "seed-demo":
            if (!app.Environment.IsDevelopment()) throw new InvalidOperationException("Demo data can only be seeded in the Development environment.");
            await db.Database.MigrateAsync();
            await sp.GetRequiredService<ReferenceDataSeeder>().SeedAsync(CancellationToken.None);
            var created = await sp.GetRequiredService<DemoDataSeeder>().SeedAsync(sp.GetRequiredService<IOptions<DemoOptions>>().Value, CancellationToken.None);
            log.LogInformation(created ? "Demo data seeded. Sign in with the demo accounts listed in DEVELOPMENT.md (password from DEMO_PASSWORD in .env)." : "Demo data already present.");
            break;
        case "reset-demo":
            var database = db.Database.GetDbConnection().Database;
            if (!app.Environment.IsDevelopment() || !args.Contains("--confirm-development") || !database.StartsWith("StudentSync", StringComparison.Ordinal))
                throw new InvalidOperationException("reset-demo only runs in Development, against a StudentSync* database, with --confirm-development.");
            log.LogWarning("Dropping development database {Database}", database);
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
            await sp.GetRequiredService<ReferenceDataSeeder>().SeedAsync(CancellationToken.None);
            await sp.GetRequiredService<DemoDataSeeder>().SeedAsync(sp.GetRequiredService<IOptions<DemoOptions>>().Value, CancellationToken.None);
            log.LogInformation("Development database reset and demo data seeded.");
            break;
    }
}

public partial class Program;
