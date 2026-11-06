using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StudentSync.Domain.Platform;
using StudentSync.Domain.Tenancy;
using StudentSync.Infrastructure.Persistence;

namespace StudentSync.Infrastructure.Seeding;

/// <summary>Idempotent platform reference data (plans, feature flags). Safe in every environment.</summary>
public sealed class ReferenceDataSeeder(AppDbContext db, ILogger<ReferenceDataSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        var plans = new[]
        {
            new { Code = "starter", Name = "Starter", Price = 49m, Students = 100, Teachers = 10, Storage = 10_000, Ai = 50, Features = new[] { FeatureKeys.Messaging, FeatureKeys.ParentPortal, FeatureKeys.Exams } },
            new { Code = "growth", Name = "Growth", Price = 149m, Students = 500, Teachers = 40, Storage = 100_000, Ai = 400, Features = new[] { FeatureKeys.Messaging, FeatureKeys.ParentPortal, FeatureKeys.Exams, FeatureKeys.Recordings, FeatureKeys.AiAssistant, FeatureKeys.Reports } },
            new { Code = "enterprise", Name = "Enterprise", Price = 499m, Students = 5000, Teachers = 300, Storage = 1_000_000, Ai = 5000, Features = FeatureKeys.All },
        };
        foreach (var p in plans)
        {
            if (await db.Plans.AnyAsync(x => x.Code == p.Code, ct)) continue;
            var plan = new SubscriptionPlan
            {
                Code = p.Code,
                Name = p.Name,
                MonthlyPrice = p.Price,
                CurrencyCode = "USD",
                MaxStudents = p.Students,
                MaxTeachers = p.Teachers,
                MaxStorageMb = p.Storage,
                MonthlyAiJobs = p.Ai,
                IsActive = true,
                Description = $"{p.Name} plan",
            };
            foreach (var key in FeatureKeys.All) plan.Features.Add(new PlanFeature { FeatureKey = key, Enabled = p.Features.Contains(key) });
            db.Plans.Add(plan);
            logger.LogInformation("Seeded plan {Code}", p.Code);
        }

        var flags = new[]
        {
            ("google_meet_integration", "Google Meet/Calendar OAuth integration (requires a Google Cloud OAuth client)."),
            ("web_push", "Browser push notifications (requires VAPID keys)."),
            ("fees_module", "Institution fee management (not implemented; reserved)."),
        };
        foreach (var (key, description) in flags)
            if (!await db.FeatureFlags.AnyAsync(f => f.Key == key, ct))
                db.FeatureFlags.Add(new FeatureFlag { Key = key, Description = description, Enabled = false });

        await db.SaveChangesAsync(ct);
    }
}
