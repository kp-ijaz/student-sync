using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StudentSync.Application.Assessments;
using StudentSync.Application.Assignments;
using StudentSync.Application.Common;
using StudentSync.Application.Communication;
using StudentSync.Application.Content;
using StudentSync.Application.Doubts;
using StudentSync.Application.Scheduling;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Scheduling;
using StudentSync.Domain.Tenancy;

namespace StudentSync.Application.Jobs;

/// <summary>Delivers a notification through external channels after it has been committed.</summary>
public sealed class NotificationDeliveryJob(IAppDbContext db, IEmailSender email, IPushSender push, IClock clock, IBackgroundJobs jobs, ILogger<NotificationDeliveryJob> logger)
{
    private static readonly HashSet<NotificationType> EmailTypes =
    [
        NotificationType.SessionScheduled, NotificationType.SessionRescheduled, NotificationType.SessionCancelled,
        NotificationType.AssignmentPublished, NotificationType.ExamPublished, NotificationType.ExamResultsReleased,
        NotificationType.DoubtReplied, NotificationType.DoubtConfirmationRequested, NotificationType.WeeklyDigest,
        NotificationType.AttendanceMarked, NotificationType.Announcement, NotificationType.AiDraftReady,
    ];

    public async Task DeliverAsync(Guid notificationId, CancellationToken ct)
    {
        // Notifications are not tenant-filtered, so this job needs no tenant scope.
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == notificationId, ct);
        if (n is null) return;
        var u = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == n.UserId, ct);
        if (u is null || !u.IsActive) return;
        var prefs = await db.NotificationPreferences.FirstOrDefaultAsync(p => p.UserId == u.Id, ct) ?? new NotificationPreference();
        var muted = NotificationQueryService.ParseMuted(prefs.MutedTypes).Contains(n.Type);

        if (InQuietHours(prefs, u.TimeZoneId, clock.UtcNow, out var resumeIn) && !muted)
        {
            jobs.Schedule<NotificationDeliveryJob>(j => j.DeliverAsync(notificationId, CancellationToken.None), resumeIn);
            return;
        }

        if (EmailTypes.Contains(n.Type))
            await DeliverEmailAsync(n, u.Email!, u.FullName, !email.IsConfigured ? "Email provider not configured" : muted || !prefs.EmailEnabled ? "Disabled by user preference" : u.IsDemo ? "Demo account" : null, ct);

        var devices = await db.Devices.Where(d => d.UserId == u.Id).ToListAsync(ct);
        if (devices.Count > 0)
        {
            var skip = !push.IsConfigured ? "Push provider not configured" : muted || !prefs.PushEnabled ? "Disabled by user preference" : null;
            var delivery = await GetDeliveryAsync(n.Id, DeliveryChannel.Push, ct);
            if (delivery.Status is DeliveryStatus.Pending)
            {
                if (skip is not null) Complete(delivery, DeliveryStatus.Skipped, null, skip);
                else
                {
                    DeliveryResult? last = null;
                    foreach (var d in devices) last = await push.SendAsync(d.Token, n.Title, n.Body, n.Link, ct);
                    Complete(delivery, last?.Accepted == true ? DeliveryStatus.Accepted : DeliveryStatus.Failed, last?.ProviderMessageId, last?.Error);
                }
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task DeliverEmailAsync(Notification n, string address, string name, string? skipReason, CancellationToken ct)
    {
        var delivery = await GetDeliveryAsync(n.Id, DeliveryChannel.Email, ct);
        if (delivery.Status is not DeliveryStatus.Pending) return; // idempotent
        if (skipReason is not null) { Complete(delivery, DeliveryStatus.Skipped, null, skipReason); return; }
        delivery.Attempts++;
        var body = $"{n.Body}\n\n{(n.Link is null ? "" : $"Open Student Sync: {n.Link}\n\n")}You can change email preferences in your Student Sync profile.";
        var result = await email.SendAsync(new EmailMessage(address, name, $"Student Sync: {n.Title}", body), ct);
        if (result.Accepted) Complete(delivery, DeliveryStatus.Accepted, result.ProviderMessageId, null);
        else if (delivery.Attempts >= 3) Complete(delivery, DeliveryStatus.Failed, null, result.Error);
        else
        {
            delivery.LastError = result.Error;
            logger.LogWarning("Email delivery attempt {Attempt} failed for notification {Id}", delivery.Attempts, n.Id);
            jobs.Schedule<NotificationDeliveryJob>(j => j.DeliverAsync(n.Id, CancellationToken.None), TimeSpan.FromMinutes(5 * delivery.Attempts));
        }
    }

    private async Task<NotificationDelivery> GetDeliveryAsync(Guid notificationId, DeliveryChannel channel, CancellationToken ct)
    {
        var d = await db.NotificationDeliveries.FirstOrDefaultAsync(x => x.NotificationId == notificationId && x.Channel == channel, ct);
        if (d is null) db.NotificationDeliveries.Add(d = new NotificationDelivery { NotificationId = notificationId, Channel = channel, Status = DeliveryStatus.Pending, CreatedAtUtc = clock.UtcNow });
        return d;
    }

    private void Complete(NotificationDelivery d, DeliveryStatus status, string? providerId, string? error)
    {
        d.Status = status;
        d.ProviderMessageId = providerId;
        d.LastError = error;
        d.CompletedAtUtc = clock.UtcNow;
    }

    public static bool InQuietHours(NotificationPreference p, string timeZoneId, DateTime utcNow, out TimeSpan resumeIn)
    {
        resumeIn = TimeSpan.Zero;
        if (p.QuietHoursStart is not { } start || p.QuietHoursEnd is not { } end || start == end) return false;
        var local = ZonedTime.TryFind(timeZoneId, out _) ? ZonedTime.ToLocal(utcNow, timeZoneId) : utcNow;
        var now = TimeOnly.FromDateTime(local);
        var inside = start < end ? now >= start && now < end : now >= start || now < end;
        if (!inside) return false;
        var wait = end - now; // TimeOnly subtraction wraps around midnight
        resumeIn = wait <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : wait;
        return true;
    }
}

/// <summary>Recurring maintenance jobs. Each runs once per active tenant inside its own DI scope and tenant scope.</summary>
public sealed class MaintenanceJobs(IServiceScopeFactory scopes, ILogger<MaintenanceJobs> logger)
{
    private async Task ForEachTenantAsync(string name, Func<IServiceProvider, CancellationToken, Task<int>> work, CancellationToken ct)
    {
        List<Guid> tenantIds;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            tenantIds = await db.Tenants.Where(t => t.Status == TenantStatus.Active).Select(t => t.Id).ToListAsync(ct);
        }
        foreach (var tenantId in tenantIds)
        {
            using var scope = scopes.CreateScope();
            using var _ = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(tenantId);
            try
            {
                var count = await work(scope.ServiceProvider, ct);
                if (count > 0) logger.LogInformation("{Job} processed {Count} item(s) for tenant {TenantId}", name, count, tenantId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One tenant's failure must not stop the others.
                logger.LogError(ex, "{Job} failed for tenant {TenantId}", name, tenantId);
            }
        }
    }

    public Task SessionRemindersAsync(CancellationToken ct) => ForEachTenantAsync("session-reminders", async (sp, c) =>
    {
        var db = sp.GetRequiredService<IAppDbContext>();
        var notify = sp.GetRequiredService<NotificationService>();
        var now = sp.GetRequiredService<IClock>().UtcNow;
        var sessions = await db.Sessions.Where(s => s.Status == SessionStatus.Scheduled && s.StartUtc > now && s.StartUtc <= now.AddMinutes(60))
            .Select(s => new { s.Id, s.Title, s.TeacherId, s.StartUtc, s.TimeZoneId, Students = s.Participants.Where(p => p.Status == ParticipantStatus.Active).Select(p => p.StudentId).ToList() })
            .ToListAsync(c);
        foreach (var s in sessions)
            await notify.NotifyAsync(s.Students.Append(s.TeacherId), NotificationType.SessionReminder, $"Starting soon: {s.Title}",
                SchedulingService.DescribeLocal(s.StartUtc, s.TimeZoneId), $"/sessions/{s.Id}", $"session-reminder:{s.Id}", c);
        await db.SaveChangesAsync(c);
        return sessions.Count;
    }, ct);

    public Task DoubtFollowUpAsync(CancellationToken ct) => ForEachTenantAsync("doubt-follow-up", async (sp, c) =>
    {
        var db = sp.GetRequiredService<IAppDbContext>();
        var tenantId = sp.GetRequiredService<ITenantContext>().TenantId;
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId && t.DoubtFollowUpEnabled, c)) return 0;
        return await sp.GetRequiredService<DoubtService>().RunFollowUpAsync(c);
    }, ct);

    public Task AssignmentRemindersAsync(CancellationToken ct) =>
        ForEachTenantAsync("assignment-reminders", (sp, c) => sp.GetRequiredService<AssignmentService>().SendDueRemindersAsync(c), ct);

    public Task FinaliseExpiredExamsAsync(CancellationToken ct) =>
        ForEachTenantAsync("exam-finalise", (sp, c) => sp.GetRequiredService<ExamService>().FinaliseExpiredAsync(c), ct);

    public Task RecordingRetentionAsync(CancellationToken ct) =>
        ForEachTenantAsync("recording-retention", (sp, c) => sp.GetRequiredService<ContentService>().ApplyRetentionAsync(sp.GetRequiredService<IFileStorage>(), c), ct);

    /// <summary>Weekly parent digest built only from real records for each verified child.</summary>
    public Task WeeklyDigestAsync(CancellationToken ct) => ForEachTenantAsync("weekly-digest", async (sp, c) =>
    {
        var db = sp.GetRequiredService<IAppDbContext>();
        var notify = sp.GetRequiredService<NotificationService>();
        var now = sp.GetRequiredService<IClock>().UtcNow;
        var weekAgo = now.AddDays(-7);
        var week = $"{ISOWeek.GetYear(now)}-W{ISOWeek.GetWeekOfYear(now):00}";
        var links = await db.ParentLinks.Where(l => l.IsVerified).Select(l => new { l.ParentId, l.StudentId, Name = l.Student!.User!.FullName }).ToListAsync(c);
        foreach (var link in links)
        {
            var att = await db.Attendance.Where(a => a.StudentId == link.StudentId && a.Session!.StartUtc >= weekAgo).GroupBy(a => a.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(c);
            var present = att.Where(a => a.Key is AttendanceStatus.Present or AttendanceStatus.Late).Sum(a => a.Count);
            var total = att.Sum(a => a.Count);
            var graded = await db.Submissions.CountAsync(s => s.StudentId == link.StudentId && s.GradedAtUtc >= weekAgo, c);
            var upcoming = await db.Sessions.CountAsync(s => s.Status == SessionStatus.Scheduled && s.StartUtc >= now && s.StartUtc < now.AddDays(7)
                && s.Participants.Any(p => p.StudentId == link.StudentId && p.Status == ParticipantStatus.Active), c);
            var body = $"{link.Name}: attended {present} of {total} marked session(s) this week, {graded} assignment(s) graded, {upcoming} class(es) scheduled next week.";
            await notify.NotifyAsync([link.ParentId], NotificationType.WeeklyDigest, $"Weekly summary for {link.Name}", body, "/parent", $"digest:{link.StudentId}:{week}", c);
        }
        await db.SaveChangesAsync(c);
        return links.Count;
    }, ct);
}
