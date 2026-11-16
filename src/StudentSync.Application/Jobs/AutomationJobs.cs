using Microsoft.Extensions.DependencyInjection;
using StudentSync.Application.Automation;
using StudentSync.Application.Fees;

namespace StudentSync.Application.Jobs;

/// <summary>Recurring entry points for Settings ▸ Automations (each runs once per institute via <see cref="TenantJobRunner"/>).</summary>
public sealed class AutomationJobs(TenantJobRunner runner)
{
    /// <summary>Every minute: send queued SMS that are due.</summary>
    public Task SendSmsAsync(CancellationToken ct) =>
        runner.ForEachTenantAsync("sms-outbox", (sp, c) => sp.GetRequiredService<SmsOutbox>().SendDueAsync(c), ct);

    /// <summary>Every 10 minutes: remind teachers about classes without attendance.</summary>
    public Task AttendanceNudgeAsync(CancellationToken ct) =>
        runner.ForEachTenantAsync("attendance-nudge", (sp, c) => sp.GetRequiredService<DailyAutomations>().AttendanceNudgeAsync(c), ct);

    /// <summary>Every 15 minutes: daily and monthly tasks whose local time has come (each once per period).</summary>
    public Task DailyAsync(CancellationToken ct) =>
        runner.ForEachTenantAsync("daily-automations", (sp, c) => sp.GetRequiredService<DailyAutomations>().RunDueAsync(c), ct);

    /// <summary>Every 15 minutes: ask the gateway about open payment links (works even without a webhook).</summary>
    public Task PaymentLinksAsync(CancellationToken ct) =>
        runner.ForEachTenantAsync("payment-links", (sp, c) => sp.GetRequiredService<PaymentLinkService>().CheckOpenLinksAsync(c), ct);
}
