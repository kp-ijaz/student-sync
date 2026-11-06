using StudentSync.Domain.Tenancy;

namespace StudentSync.Contracts;

public sealed record TenantSummaryDto(
    Guid Id, string Name, string Slug, string CountryCode, string DefaultTimeZoneId, TenantStatus Status,
    string? PlanName, SubscriptionStatus? SubscriptionStatus, int StudentCount, int TeacherCount, DateTime CreatedAtUtc, bool IsDemo);

public sealed record CreateTenantRequest(
    string Name, string Slug, string CountryCode, string DefaultTimeZoneId, string CurrencyCode, string DefaultLanguage,
    string? ContactEmail, Guid PlanId, string AdminFullName, string AdminEmail);

public sealed record CreateTenantResponse(Guid TenantId, Guid AdminUserId, string AdminTemporaryPassword);

public sealed record UpdateTenantStatusRequest(TenantStatus Status, string Reason);

public sealed record PlanDto(
    Guid Id, string Code, string Name, string? Description, decimal MonthlyPrice, string CurrencyCode,
    int MaxStudents, int MaxTeachers, int MaxStorageMb, int MonthlyAiJobs, bool IsActive, IReadOnlyList<string> Features);

public sealed record SavePlanRequest(
    string Code, string Name, string? Description, decimal MonthlyPrice, string CurrencyCode,
    int MaxStudents, int MaxTeachers, int MaxStorageMb, int MonthlyAiJobs, bool IsActive, List<string> Features);

public sealed record SubscriptionDto(
    Guid Id, Guid TenantId, string TenantName, Guid PlanId, string PlanName, SubscriptionStatus Status,
    DateTime StartedAtUtc, DateTime? TrialEndsAtUtc, DateTime? CurrentPeriodEndUtc, string? BillingEmail,
    bool BillingProviderConfigured, UsageDto Usage);

public sealed record UsageDto(int Students, int MaxStudents, int Teachers, int MaxTeachers, long StorageBytes, int MaxStorageMb, int AiJobsThisMonth, int MonthlyAiJobs);

public sealed record ChangeSubscriptionRequest(Guid PlanId, SubscriptionStatus Status, DateTime? CurrentPeriodEndUtc, string? Note);

public sealed record SubscriptionEventDto(DateTime AtUtc, string EventType, string? FromPlan, string? ToPlan, SubscriptionStatus? FromStatus, SubscriptionStatus? ToStatus, string? Note);

public sealed record PlatformOverviewDto(
    int Tenants, int ActiveTenants, int TotalUsers, int Students, int Teachers, int SessionsNext7Days,
    int AiJobsLast30Days, int FailedAiJobsLast30Days, int PendingEmailDeliveries, int FailedEmailDeliveries,
    IReadOnlyList<IntegrationStatusDto> Integrations);

public sealed record IntegrationStatusDto(string Name, bool Configured, string Detail);

public sealed record AuditEntryDto(Guid Id, DateTime CreatedAtUtc, string? UserName, string Action, string EntityType, string? EntityId, string? Summary, string? IpAddress);

public sealed record AuditQuery : PageQuery
{
    public string? Action { get; init; }
    public string? EntityType { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
}

public sealed record FeatureFlagDto(Guid Id, string Key, string Description, bool Enabled);
public sealed record SetFeatureFlagRequest(bool Enabled);

// ---------- Institution ----------

public sealed record InstitutionSettingsDto(
    Guid Id, string Name, string Slug, string CountryCode, string DefaultTimeZoneId, string CurrencyCode, string DefaultLanguage,
    string? ContactEmail, string? BrandColorHex, bool RecordingEnabled, bool TranscriptionEnabled, bool AiAssistantEnabled,
    bool DoubtFollowUpEnabled, int RecordingRetentionDays, bool StudentsCanMessageTeachers, bool ParentsCanMessageTeachers,
    string RowVersion);

public sealed record UpdateInstitutionSettingsRequest(
    string Name, string CountryCode, string DefaultTimeZoneId, string CurrencyCode, string DefaultLanguage,
    string? ContactEmail, string? BrandColorHex, bool RecordingEnabled, bool TranscriptionEnabled, bool AiAssistantEnabled,
    bool DoubtFollowUpEnabled, int RecordingRetentionDays, bool StudentsCanMessageTeachers, bool ParentsCanMessageTeachers,
    string RowVersion);

public sealed record HolidayDto(Guid Id, DateOnly Date, string Name);
public sealed record SaveHolidayRequest(DateOnly Date, string Name);

public sealed record RolePermissionDto(string Role, IReadOnlyList<string> Permissions);
