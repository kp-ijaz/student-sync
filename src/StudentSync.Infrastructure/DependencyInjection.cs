using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StudentSync.Application;
using StudentSync.Application.Common;
using StudentSync.Application.Platform;
using StudentSync.Domain.Identity;
using StudentSync.Infrastructure.Identity;
using StudentSync.Infrastructure.Persistence;
using StudentSync.Infrastructure.Seeding;
using StudentSync.Infrastructure.Services;

namespace StudentSync.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var connection = config.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Keys protect file-download tokens and integration secrets; persist them so restarts and multiple nodes share them.
        var dp = services.AddDataProtection().SetApplicationName("StudentSync");
        if (config["DataProtection:KeysPath"] is { Length: > 0 } keysPath) dp.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddSingleton<IClock, SystemClock>();

        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
                o.User.RequireUniqueEmail = false; // uniqueness is tenant-scoped (TenantId, NormalizedEmail)
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddOptions<JwtOptions>().Bind(config.GetSection("Jwt")).PostConfigure(o =>
        {
            if (string.IsNullOrWhiteSpace(o.SigningKey) && env.IsDevelopment()) o.SigningKey = DevelopmentKey(env);
            if (o.SigningKey.Length < 32) throw new InvalidOperationException("Jwt:SigningKey must be configured with at least 32 characters.");
        });
        services.AddScoped<AuthService>();
        services.AddScoped<IUserAccountService, UserAccountService>();

        services.Configure<StorageOptions>(config.GetSection("Storage:Local"));
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IFileAccessTokens, FileAccessTokens>();

        services.Configure<SmtpOptions>(config.GetSection("Email:Smtp"));
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<IPushSender, NotConfiguredPushSender>();
        services.AddSingleton<ITranscriptionProvider, NotConfiguredTranscriptionProvider>();
        services.Configure<AnthropicOptions>(config.GetSection("Ai:Anthropic"));
        services.AddSingleton<ILanguageModel, ClaudeLanguageModel>();
        services.AddScoped<IBackgroundJobs, HangfireBackgroundJobs>();

        services.AddSingleton(sp =>
        {
            var llm = sp.GetRequiredService<ILanguageModel>();
            return new IntegrationFlags(
                Email: sp.GetRequiredService<IEmailSender>().IsConfigured,
                Push: sp.GetRequiredService<IPushSender>().IsConfigured,
                LanguageModel: llm.IsConfigured,
                Transcription: sp.GetRequiredService<ITranscriptionProvider>().IsConfigured,
                GoogleWorkspace: !string.IsNullOrWhiteSpace(config["Integrations:Google:ClientId"]),
                BillingProvider: false,
                LlmDetail: llm.IsConfigured ? $"Configured: {llm.ProviderName} / {llm.ModelName}." : null);
        });

        services.Configure<DemoOptions>(config.GetSection("Demo"));
        services.AddScoped<ReferenceDataSeeder>();
        services.AddScoped<DemoDataSeeder>();

        services.AddApplication();
        return services;
    }

    /// <summary>Development only: a random signing key persisted outside source control (App_Data is git-ignored).</summary>
    private static string DevelopmentKey(IHostEnvironment env)
    {
        var path = Path.Combine(env.ContentRootPath, "App_Data", "dev-jwt.key");
        if (File.Exists(path)) return File.ReadAllText(path).Trim();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        File.WriteAllText(path, key);
        return key;
    }
}
