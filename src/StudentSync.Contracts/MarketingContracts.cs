using StudentSync.Domain.Marketing;

namespace StudentSync.Contracts;

// ---------- Marketing: enquiries (leads), calls and follow-ups, campaigns, team ----------

/// <summary>Plain names used on every marketing screen and export (one source).</summary>
public static class MarketingLabels
{
    public static string Source(LeadSource s) => s switch
    {
        LeadSource.WalkIn => "Walk-in",
        LeadSource.PhoneCall => "Phone call",
        LeadSource.SchoolEvent => "School event",
        _ => s.ToString(),
    };

    public static string Status(LeadStatus s) => s switch
    {
        LeadStatus.TrialBooked => "Trial booked",
        LeadStatus.NotInterested => "Not interested",
        _ => s.ToString(),
    };

    public static string Outcome(LeadActivityOutcome o) => o switch
    {
        LeadActivityOutcome.NoAnswer => "No answer",
        LeadActivityOutcome.CallBack => "Call back later",
        LeadActivityOutcome.WrongNumber => "Wrong number",
        LeadActivityOutcome.NotInterested => "Not interested",
        LeadActivityOutcome.None => "—",
        _ => o.ToString(),
    };
}

public sealed record LeadDto(
    Guid Id, string StudentName, string? GuardianName, string Phone, string? Email, Guid? GradeLevelId, string? GradeLevelName,
    Guid? CurriculumId, string? CurriculumName, string? SchoolName, string? Interest, LeadSource Source, Guid? CampaignId, string? CampaignName,
    LeadStatus Status, string? LostReason, Guid? AssignedToUserId, string? AssignedToName, DateOnly? NextFollowUpOn, string? Notes,
    DateOnly EnquiredOn, Guid? ConvertedStudentId, int Calls, DateTime? LastContactUtc, bool FollowUpOverdue,
    string? ReferredBy = null, DateTime? TrialAtUtc = null, string? TrialNotes = null, bool FromWebsiteForm = false);

public sealed record SaveLeadRequest(
    string StudentName, string Phone, LeadSource Source, string? GuardianName = null, string? Email = null, Guid? GradeLevelId = null,
    Guid? CurriculumId = null, string? SchoolName = null, string? Interest = null, Guid? CampaignId = null, LeadStatus Status = LeadStatus.New,
    string? LostReason = null, Guid? AssignedToUserId = null, DateOnly? NextFollowUpOn = null, string? Notes = null, DateOnly? EnquiredOn = null,
    string? ReferredBy = null, DateTime? TrialAtUtc = null, string? TrialNotes = null);

public enum LeadSort { Newest, Oldest, FollowUp, Name }

/// <summary>Search matches the names, phone, school and what they asked about. <c>FollowUp</c>: "today", "overdue" or "week".</summary>
public sealed record LeadQuery : PageQuery
{
    public LeadStatus? Status { get; init; }
    public bool? Open { get; init; }
    public LeadSource? Source { get; init; }
    public Guid? CampaignId { get; init; }
    public Guid? AssignedToUserId { get; init; }
    public string? FollowUp { get; init; }
    /// <summary>"upcoming": leads with a trial class from now on.</summary>
    public string? Trial { get; init; }
    /// <summary>true: enquiries that came through the website form and have not been contacted yet.</summary>
    public bool? WebsiteNew { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public LeadSort? Sort { get; init; }
}

public sealed record LeadActivityDto(Guid Id, LeadActivityKind Kind, LeadActivityOutcome Outcome, DateTime OccurredAtUtc, int? DurationMinutes,
    string? Notes, Guid ByUserId, string ByName, bool CanEdit);

/// <summary>
/// Logs a contact. <c>SetStatus</c> and <c>NextFollowUpOn</c> update the lead in the same step ("called, interested, call
/// again Friday"); <c>ClearFollowUp</c> removes the planned day.
/// </summary>
public sealed record SaveLeadActivityRequest(LeadActivityKind Kind, LeadActivityOutcome Outcome = LeadActivityOutcome.None, DateTime? OccurredAtUtc = null,
    int? DurationMinutes = null, string? Notes = null, LeadStatus? SetStatus = null, DateOnly? NextFollowUpOn = null, bool ClearFollowUp = false);

public sealed record LeadDetailDto(LeadDto Lead, IReadOnlyList<LeadActivityDto> Activities, string TimeZoneId);

/// <summary>Admin only: link the lead to the student who was admitted (marks it Admitted).</summary>
public sealed record LinkLeadStudentRequest(Guid StudentId);

public sealed record CampaignDto(
    Guid Id, string Name, LeadSource Channel, string? Location, DateOnly StartsOn, DateOnly? EndsOn, decimal Budget, decimal Spent, string CurrencyCode,
    CampaignStatus Status, string? Notes, int Leads, int Admitted, decimal? CostPerLead, decimal? CostPerAdmission, decimal? ConversionPercent);

public sealed record SaveCampaignRequest(string Name, LeadSource Channel, DateOnly StartsOn, DateOnly? EndsOn = null, decimal Budget = 0,
    CampaignStatus Status = CampaignStatus.Planned, string? Location = null, string? Notes = null);

/// <summary>Money spent on a campaign. It is stored as an expense (category Marketing), so it also counts in Finance.</summary>
public sealed record AddCampaignSpendRequest(decimal Amount, DateOnly? SpentOn = null, string? Description = null,
    StudentSync.Domain.Fees.PaymentMethod Method = StudentSync.Domain.Fees.PaymentMethod.Cash, string? Reference = null);
public sealed record CampaignSpendDto(Guid Id, DateOnly SpentOn, decimal Amount, string Description, StudentSync.Domain.Fees.PaymentMethod Method, string? Reference, string? RecordedBy);

/// <summary>Choices for the lead and campaign forms (the marketing team has no access to the academic catalogue itself).</summary>
public sealed record MarketingOptionsDto(IReadOnlyList<LookupItem> Grades, IReadOnlyList<LookupItem> Curricula, IReadOnlyList<LookupItem> Campaigns,
    IReadOnlyList<LookupItem> Team, string CurrencyCode);

public sealed record CountDto<T>(T Key, int Count);
public sealed record SourceRowDto(LeadSource Source, int Leads, int Admitted, decimal? ConversionPercent);
/// <summary>Targets are for the period shown (daily call target × working days, monthly targets × months); empty = no target set.</summary>
public sealed record TeamRowDto(Guid UserId, string Name, int Calls, int CallsReached, int OtherContacts, int LeadsAssignedOpen, int Admitted,
    int LeadsAdded = 0, int? CallsTarget = null, int? LeadsTarget = null, int? AdmissionsTarget = null);
public sealed record DayCountDto(DateOnly Day, int Leads, int Calls);

/// <summary>
/// Marketing for a period. Leads = enquiries dated in the period; Admitted / Lost = those of them now in that state;
/// Conversion = admitted ÷ leads; Calls = calls logged in the period; Answer rate = calls reached ÷ calls; Spend = campaign
/// "spent" for campaigns that ran in the period. Follow-ups and Open leads are today's figures, whatever the period.
/// </summary>
public sealed record MarketingOverviewDto(
    DateOnly From, DateOnly To, string CurrencyCode,
    int Leads, int Admitted, int Lost, decimal? ConversionPercent,
    int Calls, int CallsReached, decimal? AnswerRatePercent, int OtherContacts,
    decimal Spend, decimal? CostPerLead, decimal? CostPerAdmission,
    int OpenLeads, int FollowUpsToday, int FollowUpsOverdue, int Unassigned,
    int PreviousLeads, int PreviousCalls, int PreviousAdmitted,
    IReadOnlyList<CountDto<LeadStatus>> Pipeline, IReadOnlyList<SourceRowDto> Sources, IReadOnlyList<CampaignDto> Campaigns,
    IReadOnlyList<TeamRowDto> Team, IReadOnlyList<DayCountDto> Days, IReadOnlyList<LeadDto> DueFollowUps,
    IReadOnlyList<LeadDto> UpcomingTrials, IReadOnlyList<CountDto<string>> Referrers, int WebsiteFormNew);

/// <summary>A marketing team member's sign-in account (admin only).</summary>
public sealed record MarketingUserDto(Guid Id, string FullName, string Email, bool IsActive, DateTime? LastLoginAtUtc, int OpenLeads,
    int? CallsPerDay = null, int? LeadsPerMonth = null, int? AdmissionsPerMonth = null);
public sealed record CreateMarketingUserRequest(string FullName, string Email);
public sealed record UpdateMarketingUserRequest(string FullName, bool IsActive, int? CallsPerDay = null, int? LeadsPerMonth = null, int? AdmissionsPerMonth = null);

/// <summary>The public enquiry form: whether it accepts enquiries, and the code in its address (/enquire/{Code}).</summary>
public sealed record EnquiryFormDto(bool Enabled, string Code);
public sealed record SetEnquiryFormRequest(bool Enabled);
/// <summary>What the public form shows: the centre's name and its classes. Nothing else about the centre is public.</summary>
public sealed record PublicEnquiryInfoDto(string InstituteName, IReadOnlyList<LookupItem> Grades);
/// <summary><c>Website</c> is a hidden field real visitors leave empty (spam bots fill it in; such posts are dropped).</summary>
public sealed record PublicEnquiryRequest(string StudentName, string Phone, string? GuardianName = null, Guid? GradeLevelId = null, string? Interest = null, string? Website = null);

/// <summary>
/// Leads pasted from a spreadsheet, one per line: name, phone, parent, class, asked about (comma or tab separated; a
/// header line is skipped). All get the same source and campaign. Up to 500 lines.
/// </summary>
public sealed record ImportLeadsRequest(string Rows, LeadSource Source, Guid? CampaignId = null);
public sealed record ImportLeadsResult(int Added, int Skipped, IReadOnlyList<string> Problems);
