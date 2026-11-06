using StudentSync.Domain.Common;

namespace StudentSync.Domain.Tenancy;

public enum TenantStatus { Active, Suspended, Archived }

/// <summary>An institution / tuition centre. The root of tenant isolation.</summary>
public class Tenant : AuditableEntity
{
    public string Name { get; set; } = "";
    /// <summary>Short unique code used at sign-in when an email exists in more than one institution.</summary>
    public string Slug { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string DefaultTimeZoneId { get; set; } = "UTC";
    public string CurrencyCode { get; set; } = "USD";
    public string DefaultLanguage { get; set; } = "en";
    public string? ContactEmail { get; set; }
    public string? BrandColorHex { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public bool IsDemo { get; set; }

    // Institution policy settings.
    public bool RecordingEnabled { get; set; }
    public bool TranscriptionEnabled { get; set; }
    public bool AiAssistantEnabled { get; set; }
    public bool DoubtFollowUpEnabled { get; set; } = true;
    public int RecordingRetentionDays { get; set; } = 365;
    public bool StudentsCanMessageTeachers { get; set; } = true;
    public bool ParentsCanMessageTeachers { get; set; } = true;
}

public class InstitutionHoliday : TenantEntity
{
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
}

public class SubscriptionPlan : AuditableEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal MonthlyPrice { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public int MaxStudents { get; set; }
    public int MaxTeachers { get; set; }
    public int MaxStorageMb { get; set; }
    public int MonthlyAiJobs { get; set; }
    public bool IsActive { get; set; } = true;
    public List<PlanFeature> Features { get; set; } = [];
}

public static class FeatureKeys
{
    public const string AiAssistant = "ai_assistant";
    public const string Recordings = "recordings";
    public const string Exams = "exams";
    public const string Messaging = "messaging";
    public const string ParentPortal = "parent_portal";
    public const string Reports = "advanced_reports";
    public static readonly string[] All = [AiAssistant, Recordings, Exams, Messaging, ParentPortal, Reports];
}

public class PlanFeature : Entity
{
    public Guid PlanId { get; set; }
    public string FeatureKey { get; set; } = "";
    public bool Enabled { get; set; }
}

public enum SubscriptionStatus { Trialing, Active, PastDue, Cancelled, Expired }

/// <summary>An institution's subscription. Exactly one row per tenant; changes are recorded in <see cref="SubscriptionEvent"/>.</summary>
public class TenantSubscription : TenantEntity
{
    public Guid PlanId { get; set; }
    public SubscriptionPlan? Plan { get; set; }
    public SubscriptionStatus Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? TrialEndsAtUtc { get; set; }
    public DateTime? CurrentPeriodEndUtc { get; set; }
    public string? BillingEmail { get; set; }
    /// <summary>Reference in the external billing provider, when one is configured.</summary>
    public string? ExternalReference { get; set; }

    public bool IsUsable => Status is SubscriptionStatus.Trialing or SubscriptionStatus.Active or SubscriptionStatus.PastDue;
}

public class SubscriptionEvent : TenantEntity
{
    public Guid SubscriptionId { get; set; }
    public string EventType { get; set; } = "";
    public Guid? FromPlanId { get; set; }
    public Guid? ToPlanId { get; set; }
    public SubscriptionStatus? FromStatus { get; set; }
    public SubscriptionStatus? ToStatus { get; set; }
    public string? Note { get; set; }
}
