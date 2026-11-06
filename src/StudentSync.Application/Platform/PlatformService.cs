using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.AI;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;
using StudentSync.Domain.Scheduling;
using StudentSync.Domain.Tenancy;

namespace StudentSync.Application.Platform;

public sealed record IntegrationFlags(bool Email, bool Push, bool LanguageModel, bool Transcription, bool GoogleWorkspace, bool BillingProvider, string? LlmDetail);

/// <summary>SaaS administration. Super admins see institution metadata and aggregate counts, never educational content.</summary>
public sealed partial class PlatformService(IAppDbContext db, AccessPolicy access, IUserAccountService accounts, IClock clock, AuditService audit, IntegrationFlags integrations)
{
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,38}[a-z0-9])$")]
    private static partial Regex SlugPattern();

    public async Task<PlatformOverviewDto> OverviewAsync(CancellationToken ct)
    {
        access.Require(Permissions.PlatformMonitor);
        var now = clock.UtcNow;
        var since = now.AddDays(-30);
        return new PlatformOverviewDto(
            await db.Tenants.CountAsync(ct),
            await db.Tenants.CountAsync(t => t.Status == TenantStatus.Active, ct),
            await db.Users.CountAsync(u => u.IsActive, ct),
            await db.Students.IgnoreQueryFilters().CountAsync(s => s.Status == PersonStatus.Active, ct),
            await db.Teachers.IgnoreQueryFilters().CountAsync(s => s.Status == PersonStatus.Active, ct),
            await db.Sessions.IgnoreQueryFilters().CountAsync(s => s.Status != SessionStatus.Cancelled && s.StartUtc >= now && s.StartUtc < now.AddDays(7), ct),
            await db.AiJobs.IgnoreQueryFilters().CountAsync(j => j.CreatedAtUtc >= since, ct),
            await db.AiJobs.IgnoreQueryFilters().CountAsync(j => j.CreatedAtUtc >= since && j.Status == AiJobStatus.Failed, ct),
            await db.NotificationDeliveries.CountAsync(d => d.Status == DeliveryStatus.Pending, ct),
            await db.NotificationDeliveries.CountAsync(d => d.Status == DeliveryStatus.Failed && d.CreatedAtUtc >= since, ct),
            IntegrationStatuses());
    }

    public IReadOnlyList<IntegrationStatusDto> IntegrationStatuses() =>
    [
        new("Email (SMTP)", integrations.Email, integrations.Email ? "Messages are handed to the configured SMTP server." : "Not configured: email notifications are recorded as skipped."),
        new("Push notifications", integrations.Push, integrations.Push ? "Configured." : "Not configured: no push provider credentials (e.g. Firebase Cloud Messaging)."),
        new("AI language model", integrations.LanguageModel, integrations.LlmDetail ?? (integrations.LanguageModel ? "Configured." : "Not configured: AI drafts and the student assistant are unavailable; manual notes still work.")),
        new("Transcription provider", integrations.Transcription, integrations.Transcription ? "Configured." : "Not configured: teachers upload transcripts (VTT/SRT/TXT) instead."),
        new("Google Workspace (Meet/Calendar)", integrations.GoogleWorkspace, integrations.GoogleWorkspace ? "OAuth client configured." : "Not configured: teachers paste meeting links manually; ICS export is available."),
        new("Billing provider", integrations.BillingProvider, "Not configured: subscriptions are managed manually by platform administrators; no payments are taken."),
    ];

    public async Task<IReadOnlyList<TenantSummaryDto>> TenantsAsync(CancellationToken ct)
    {
        access.Require(Permissions.PlatformTenantsManage);
        return await db.Tenants.OrderBy(t => t.Name).Select(t => new TenantSummaryDto(t.Id, t.Name, t.Slug, t.CountryCode, t.DefaultTimeZoneId, t.Status,
            db.Subscriptions.IgnoreQueryFilters().Where(s => s.TenantId == t.Id).Select(s => s.Plan!.Name).FirstOrDefault(),
            db.Subscriptions.IgnoreQueryFilters().Where(s => s.TenantId == t.Id).Select(s => (SubscriptionStatus?)s.Status).FirstOrDefault(),
            db.Students.IgnoreQueryFilters().Count(s => s.TenantId == t.Id && s.Status == PersonStatus.Active),
            db.Teachers.IgnoreQueryFilters().Count(s => s.TenantId == t.Id && s.Status == PersonStatus.Active), t.CreatedAtUtc, t.IsDemo)).ToListAsync(ct);
    }

    public async Task<CreateTenantResponse> CreateTenantAsync(CreateTenantRequest r, CancellationToken ct)
    {
        access.Require(Permissions.PlatformTenantsManage);
        var slug = Text.Required(r.Slug, "slug", 40).ToLowerInvariant();
        if (!SlugPattern().IsMatch(slug)) throw RequestValidationException.For("slug", "Use 3–40 lowercase letters, digits or hyphens.");
        if (await db.Tenants.AnyAsync(t => t.Slug == slug, ct)) throw RequestValidationException.For("slug", "This institution code is already taken.");
        if (!ZonedTime.TryFind(r.DefaultTimeZoneId, out _)) throw RequestValidationException.For("defaultTimeZoneId", "Choose a valid time zone.");
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == r.PlanId && p.IsActive, ct) ?? throw RequestValidationException.For("planId", "Choose an active plan.");
        var country = Text.Required(r.CountryCode, "countryCode", 2).ToUpperInvariant();
        var currency = Text.Required(r.CurrencyCode, "currencyCode", 3).ToUpperInvariant();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var tenant = new Tenant
        {
            Name = Text.Required(r.Name, "name", 150),
            Slug = slug,
            CountryCode = country,
            DefaultTimeZoneId = r.DefaultTimeZoneId,
            CurrencyCode = currency,
            DefaultLanguage = r.DefaultLanguage is "ar" or "ml" ? r.DefaultLanguage : "en",
            ContactEmail = Text.Optional(r.ContactEmail, "contactEmail", 256),
        };
        db.Tenants.Add(tenant);
        var sub = new TenantSubscription
        {
            TenantId = tenant.Id,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Trialing,
            StartedAtUtc = clock.UtcNow,
            TrialEndsAtUtc = clock.UtcNow.AddDays(30),
            CurrentPeriodEndUtc = clock.UtcNow.AddDays(30),
            BillingEmail = tenant.ContactEmail,
        };
        db.Subscriptions.Add(sub);
        db.SubscriptionEvents.Add(new SubscriptionEvent { TenantId = tenant.Id, SubscriptionId = sub.Id, EventType = "created", ToPlanId = plan.Id, ToStatus = SubscriptionStatus.Trialing });
        await db.SaveChangesAsync(ct);
        var admin = await accounts.CreateAsync(tenant.Id, r.AdminEmail, Text.Required(r.AdminFullName, "adminFullName", 150), UserRole.InstitutionAdmin, r.DefaultTimeZoneId, null, false, ct);
        audit.Record("tenant.created", "Tenant", tenant.Id, $"{tenant.Name} ({slug}) on {plan.Code}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new CreateTenantResponse(tenant.Id, admin.UserId, admin.TemporaryPassword!);
    }

    public async Task UpdateTenantStatusAsync(Guid tenantId, UpdateTenantStatusRequest r, CancellationToken ct)
    {
        access.Require(Permissions.PlatformTenantsManage);
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, ct) ?? throw new NotFoundException("Institution");
        var reason = Text.Required(r.Reason, "reason", 500);
        t.Status = r.Status;
        if (r.Status != TenantStatus.Active)
        {
            // Suspension takes effect immediately for refresh tokens; access tokens expire within minutes.
            var userIds = db.Users.Where(u => u.TenantId == t.Id).Select(u => u.Id);
            await db.RefreshTokens.Where(x => userIds.Contains(x.UserId) && x.RevokedAtUtc == null)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.RevokedAtUtc, clock.UtcNow).SetProperty(x => x.RevokedReason, "tenant_" + r.Status.ToString().ToLowerInvariant()), ct);
        }
        audit.Record("tenant.status", "Tenant", t.Id, $"{r.Status}: {reason}");
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Plans & subscriptions ----------------

    public async Task<IReadOnlyList<PlanDto>> PlansAsync(CancellationToken ct) =>
        await db.Plans.OrderBy(p => p.MonthlyPrice).Select(p => new PlanDto(p.Id, p.Code, p.Name, p.Description, p.MonthlyPrice, p.CurrencyCode, p.MaxStudents, p.MaxTeachers,
            p.MaxStorageMb, p.MonthlyAiJobs, p.IsActive, p.Features.Where(f => f.Enabled).Select(f => f.FeatureKey).ToList())).ToListAsync(ct);

    public async Task<Guid> SavePlanAsync(Guid? id, SavePlanRequest r, CancellationToken ct)
    {
        access.Require(Permissions.PlatformPlansManage);
        var code = Text.Required(r.Code, "code", 30).ToLowerInvariant();
        if (await db.Plans.AnyAsync(p => p.Code == code && p.Id != id, ct)) throw RequestValidationException.For("code", "This plan code exists.");
        if (r.MonthlyPrice < 0 || r.MaxStudents < 1 || r.MaxTeachers < 1 || r.MaxStorageMb < 1 || r.MonthlyAiJobs < 0) throw RequestValidationException.For("limits", "Limits and price must be positive.");
        if (r.Features.Any(f => !FeatureKeys.All.Contains(f))) throw RequestValidationException.For("features", "Unknown feature.");
        var p = id is { } pid ? await db.Plans.Include(x => x.Features).FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw new NotFoundException("Plan") : db.Plans.Add(new SubscriptionPlan()).Entity;
        p.Code = code; p.Name = Text.Required(r.Name, "name", 100); p.Description = Text.Optional(r.Description, "description", 1000);
        p.MonthlyPrice = decimal.Round(r.MonthlyPrice, 2); p.CurrencyCode = Text.Required(r.CurrencyCode, "currencyCode", 3).ToUpperInvariant();
        p.MaxStudents = r.MaxStudents; p.MaxTeachers = r.MaxTeachers; p.MaxStorageMb = r.MaxStorageMb; p.MonthlyAiJobs = r.MonthlyAiJobs; p.IsActive = r.IsActive;
        foreach (var key in FeatureKeys.All)
        {
            var f = p.Features.FirstOrDefault(x => x.FeatureKey == key);
            if (f is null) p.Features.Add(f = new PlanFeature { FeatureKey = key });
            f.Enabled = r.Features.Contains(key);
        }
        audit.Record("plan.saved", "SubscriptionPlan", p.Id, code);
        await db.SaveChangesAsync(ct);
        return p.Id;
    }

    public async Task<IReadOnlyList<SubscriptionDto>> SubscriptionsAsync(CancellationToken ct)
    {
        access.Require(Permissions.PlatformTenantsManage);
        var subs = await db.Subscriptions.IgnoreQueryFilters().Include(s => s.Plan).ToListAsync(ct);
        var names = await db.Tenants.ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var list = new List<SubscriptionDto>();
        foreach (var s in subs) list.Add(await ToDtoAsync(s, names.GetValueOrDefault(s.TenantId, "?"), ct));
        return list.OrderBy(s => s.TenantName).ToList();
    }

    public async Task<SubscriptionDto> ToDtoAsync(TenantSubscription s, string tenantName, CancellationToken ct)
    {
        var monthStart = new DateTime(clock.UtcNow.Year, clock.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var usage = new UsageDto(
            await db.Students.IgnoreQueryFilters().CountAsync(x => x.TenantId == s.TenantId && x.Status == PersonStatus.Active, ct), s.Plan!.MaxStudents,
            await db.Teachers.IgnoreQueryFilters().CountAsync(x => x.TenantId == s.TenantId && x.Status == PersonStatus.Active, ct), s.Plan.MaxTeachers,
            await db.Files.IgnoreQueryFilters().Where(f => f.TenantId == s.TenantId && f.DeletedAtUtc == null).SumAsync(f => (long?)f.SizeBytes, ct) ?? 0, s.Plan.MaxStorageMb,
            await db.AiJobs.IgnoreQueryFilters().CountAsync(j => j.TenantId == s.TenantId && j.CreatedAtUtc >= monthStart && j.Status != AiJobStatus.Blocked, ct), s.Plan.MonthlyAiJobs);
        return new SubscriptionDto(s.Id, s.TenantId, tenantName, s.PlanId, s.Plan.Name, s.Status, s.StartedAtUtc, s.TrialEndsAtUtc, s.CurrentPeriodEndUtc, s.BillingEmail,
            integrations.BillingProvider, usage);
    }

    public async Task ChangeSubscriptionAsync(Guid tenantId, ChangeSubscriptionRequest r, CancellationToken ct)
    {
        access.Require(Permissions.PlatformTenantsManage);
        var s = await db.Subscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct) ?? throw new NotFoundException("Subscription");
        if (!await db.Plans.AnyAsync(p => p.Id == r.PlanId, ct)) throw RequestValidationException.For("planId", "Plan not found.");
        db.SubscriptionEvents.Add(new SubscriptionEvent
        {
            TenantId = tenantId,
            SubscriptionId = s.Id,
            EventType = "changed",
            FromPlanId = s.PlanId,
            ToPlanId = r.PlanId,
            FromStatus = s.Status,
            ToStatus = r.Status,
            Note = Text.Optional(r.Note, "note", 500),
        });
        s.PlanId = r.PlanId;
        s.Status = r.Status;
        s.CurrentPeriodEndUtc = r.CurrentPeriodEndUtc is { } end ? DateTime.SpecifyKind(end, DateTimeKind.Utc) : s.CurrentPeriodEndUtc;
        audit.Record("subscription.changed", "TenantSubscription", s.Id, $"{r.Status} plan={r.PlanId}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SubscriptionEventDto>> SubscriptionHistoryAsync(Guid tenantId, CancellationToken ct)
    {
        access.Require(Permissions.PlatformTenantsManage);
        var plans = await db.Plans.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var events = await db.SubscriptionEvents.IgnoreQueryFilters().Where(e => e.TenantId == tenantId).OrderByDescending(e => e.CreatedAtUtc).ToListAsync(ct);
        return events.Select(e => new SubscriptionEventDto(e.CreatedAtUtc, e.EventType, e.FromPlanId is { } f ? plans.GetValueOrDefault(f) : null,
            e.ToPlanId is { } t ? plans.GetValueOrDefault(t) : null, e.FromStatus, e.ToStatus, e.Note)).ToList();
    }

    // ---------------- Audit & flags ----------------

    public async Task<PagedResult<AuditEntryDto>> AuditAsync(AuditQuery q, Guid? tenantId, CancellationToken ct)
    {
        var logs = db.AuditLogs.AsQueryable();
        logs = tenantId is { } t ? logs.Where(l => l.TenantId == t) : logs.Where(l => l.TenantId == null);
        if (!string.IsNullOrWhiteSpace(q.Action)) logs = logs.Where(l => l.Action.StartsWith(q.Action));
        if (!string.IsNullOrWhiteSpace(q.EntityType)) logs = logs.Where(l => l.EntityType == q.EntityType);
        if (q.FromUtc is { } from) logs = logs.Where(l => l.CreatedAtUtc >= from);
        if (q.ToUtc is { } to) logs = logs.Where(l => l.CreatedAtUtc <= to);
        var total = await logs.CountAsync(ct);
        var items = await logs.OrderByDescending(l => l.CreatedAtUtc).Skip((q.SafePage - 1) * q.SafePageSize).Take(q.SafePageSize)
            .Select(l => new AuditEntryDto(l.Id, l.CreatedAtUtc, db.Users.Where(u => u.Id == l.UserId).Select(u => u.FullName).FirstOrDefault(), l.Action, l.EntityType, l.EntityId, l.Summary, l.IpAddress))
            .ToListAsync(ct);
        return new PagedResult<AuditEntryDto>(items, q.SafePage, q.SafePageSize, total);
    }

    public async Task<IReadOnlyList<FeatureFlagDto>> FlagsAsync(CancellationToken ct) =>
        await db.FeatureFlags.OrderBy(f => f.Key).Select(f => new FeatureFlagDto(f.Id, f.Key, f.Description, f.Enabled)).ToListAsync(ct);

    public async Task SetFlagAsync(Guid id, SetFeatureFlagRequest r, CancellationToken ct)
    {
        access.Require(Permissions.PlatformTenantsManage);
        var f = await db.FeatureFlags.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Feature flag");
        f.Enabled = r.Enabled;
        audit.Record("flag.changed", "FeatureFlag", f.Id, $"{f.Key}={r.Enabled}");
        await db.SaveChangesAsync(ct);
    }
}
