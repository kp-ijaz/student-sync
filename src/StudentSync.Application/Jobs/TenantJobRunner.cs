using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StudentSync.Application.Common;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Platform;
using StudentSync.Domain.Tenancy;

namespace StudentSync.Application.Jobs;

/// <summary>
/// Runs a recurring job once per active institute, each in its own DI and tenant scope. One institute's failure never
/// stops the others; it is logged and — when "Job failure alerts" is on — the institute's admins are told once a day per
/// job (in-app and email), without technical details.
/// </summary>
public sealed class TenantJobRunner(IServiceScopeFactory scopes, ILogger<TenantJobRunner> logger)
{
    public async Task ForEachTenantAsync(string name, Func<IServiceProvider, CancellationToken, Task<int>> work, CancellationToken ct)
    {
        List<Guid> tenantIds;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            tenantIds = await db.Tenants.Where(t => t.Status == TenantStatus.Active).Select(t => t.Id).ToListAsync(ct);
        }
        foreach (var tenantId in tenantIds)
        {
            try
            {
                using var scope = scopes.CreateScope();
                using var _ = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(tenantId);
                var count = await work(scope.ServiceProvider, ct);
                if (count > 0) logger.LogInformation("{Job} processed {Count} item(s) for tenant {TenantId}", name, count, tenantId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "{Job} failed for tenant {TenantId}", name, tenantId);
                await AlertAsync(name, tenantId, ct);
            }
        }
    }

    private async Task AlertAsync(string job, Guid tenantId, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            using var _ = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var settings = await db.AutomationSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new AutomationSettings();
            if (!settings.JobFailureAlerts) return;
            // Users are not tenant-filtered: only this institute's admins.
            var admins = await db.Users.Where(u => u.TenantId == tenantId && u.Role == UserRole.InstitutionAdmin && u.IsActive).Select(u => u.Id).ToListAsync(ct);
            var today = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            await scope.ServiceProvider.GetRequiredService<NotificationService>().NotifyAsync(admins, NotificationType.AutomationFailed,
                $"An automatic task did not finish: {JobLabel(job)}",
                "Student Sync will try again automatically. If this keeps happening, check Settings ▸ Integrations (email, SMS, payments) or contact support.",
                "/admin/settings?tab=automations", $"job-failed:{job}:{today}", ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not record the failure alert for {Job}", job);
        }
    }

    public static string JobLabel(string job) => job switch
    {
        "sms-outbox" => "sending SMS",
        "attendance-nudge" => "attendance reminders",
        "daily-automations" => "daily reminders and summaries",
        "payment-links" => "checking online payments",
        "session-reminders" => "class reminders",
        "assignment-reminders" => "assignment reminders",
        "exam-finalise" => "closing finished exams",
        "recording-retention" => "removing old recordings",
        "weekly-digest" => "weekly summary",
        "doubt-follow-up" => "doubt follow-ups",
        _ => job,
    };
}
