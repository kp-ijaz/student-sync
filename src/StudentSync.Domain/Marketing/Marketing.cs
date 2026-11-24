using StudentSync.Domain.Common;

namespace StudentSync.Domain.Marketing;

/// <summary>Where an enquiry came from, and the channel a campaign runs on (one list for both, so they can be compared).</summary>
public enum LeadSource { WalkIn, PhoneCall, WhatsApp, Instagram, Facebook, Google, Website, Referral, Flyer, SchoolEvent, Newspaper, Other }

/// <summary>The enquiry pipeline. Admitted and NotInterested are final; everything else is still open.</summary>
public enum LeadStatus { New, Contacted, Interested, TrialBooked, Admitted, NotInterested }

public enum LeadActivityKind { Call, WhatsApp, Email, Visit, Note }

/// <summary>How a contact attempt went. Only calls use the "reached" distinction for the answer rate.</summary>
public enum LeadActivityOutcome { Answered, NoAnswer, Busy, CallBack, WrongNumber, Interested, NotInterested, None }

public enum CampaignStatus { Planned, Running, Paused, Ended }

/// <summary>
/// An enquiry from a prospective student or their parent (owner request 2026-11-24). Only what the front desk needs to
/// follow up is kept: names, a phone number, class and what they asked about. It is not a student record and gives no
/// access to anything; when the child joins, an admin admits them as a student and the lead is linked to that student.
/// </summary>
public class Lead : TenantEntity
{
    public string StudentName { get; set; } = "";
    public string? GuardianName { get; set; }
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public Guid? GradeLevelId { get; set; }
    public Guid? CurriculumId { get; set; }
    public string? SchoolName { get; set; }
    /// <summary>Subjects or help they asked about, as the caller said it (free text).</summary>
    public string? Interest { get; set; }
    public LeadSource Source { get; set; }
    public Guid? CampaignId { get; set; }
    public Campaign? Campaign { get; set; }
    public LeadStatus Status { get; set; } = LeadStatus.New;
    /// <summary>Why a NotInterested lead was lost (fees, distance, joined elsewhere…).</summary>
    public string? LostReason { get; set; }
    public Guid? AssignedToUserId { get; set; }
    /// <summary>The day someone should contact them next; empty when nothing is planned (or the lead is closed).</summary>
    public DateOnly? NextFollowUpOn { get; set; }
    public string? Notes { get; set; }
    /// <summary>When the enquiry came in (centre's day), which may be earlier than the day it was typed in.</summary>
    public DateOnly EnquiredOn { get; set; }
    /// <summary>Who sent them (a parent, a student, a teacher) when the source is a referral. Free text: the marketing team cannot look up students.</summary>
    public string? ReferredBy { get; set; }
    /// <summary>A free trial class agreed with the family: when, and with whom / which subject (free text).</summary>
    public DateTime? TrialAtUtc { get; set; }
    public string? TrialNotes { get; set; }
    /// <summary>True for an enquiry sent through the public website form (not yet looked at by the team).</summary>
    public bool FromWebsiteForm { get; set; }
    public Guid? ConvertedStudentId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public List<LeadActivity> Activities { get; set; } = [];

    public bool IsOpen => MarketingRules.IsOpen(Status);
}

/// <summary>One contact with a lead: a call, a WhatsApp message, a visit or a note, with how it went.</summary>
public class LeadActivity : TenantEntity
{
    public Guid LeadId { get; set; }
    public LeadActivityKind Kind { get; set; }
    public LeadActivityOutcome Outcome { get; set; } = LeadActivityOutcome.None;
    public DateTime OccurredAtUtc { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Notes { get; set; }
    public Guid ByUserId { get; set; }
}

/// <summary>
/// A marketing effort: where it runs, when, and its budget. What was actually spent is not typed here: each payment is an
/// <see cref="StudentSync.Domain.Fees.Expense"/> (category Marketing) linked to the campaign, so the same money shows in
/// Finance and is never counted twice.
/// </summary>
public class Campaign : TenantEntity
{
    public string Name { get; set; } = "";
    public LeadSource Channel { get; set; }
    /// <summary>Where it runs: an area, a school, a page or account name.</summary>
    public string? Location { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public decimal Budget { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public CampaignStatus Status { get; set; } = CampaignStatus.Planned;
    public string? Notes { get; set; }
}

/// <summary>What one team member is expected to do. Empty fields mean "no target".</summary>
public class MarketingTarget : TenantEntity
{
    public Guid UserId { get; set; }
    public int? CallsPerDay { get; set; }
    public int? LeadsPerMonth { get; set; }
    public int? AdmissionsPerMonth { get; set; }
}

/// <summary>Pure marketing rules (unit-tested): how every figure on the marketing screens is calculated.</summary>
public static class MarketingRules
{
    public static bool IsOpen(LeadStatus s) => s is not (LeadStatus.Admitted or LeadStatus.NotInterested);

    /// <summary>A call counts as reached unless nobody picked up, the line was busy or the number was wrong.</summary>
    public static bool Reached(LeadActivityOutcome o) => o is not (LeadActivityOutcome.NoAnswer or LeadActivityOutcome.Busy or LeadActivityOutcome.WrongNumber);

    /// <summary>Share as a percentage with one decimal; empty when there is nothing to divide by (never a made-up 0%).</summary>
    public static decimal? Percent(int part, int whole) => whole <= 0 ? null : decimal.Round(100m * part / whole, 1, MidpointRounding.AwayFromZero);

    /// <summary>Money spent for each lead (or admission); empty when there are none yet.</summary>
    public static decimal? CostPer(decimal spent, int count) => count <= 0 ? null : decimal.Round(spent / count, 2, MidpointRounding.AwayFromZero);

    /// <summary>A follow-up is overdue when its day has passed and the lead is still open.</summary>
    public static bool Overdue(LeadStatus status, DateOnly? followUpOn, DateOnly today) => IsOpen(status) && followUpOn is { } d && d < today;

    /// <summary>
    /// A target for a period: a monthly target scaled by the months in the period; a daily one by its working days
    /// (Monday–Saturday; the centre is closed on Sundays). Empty when no target is set.
    /// </summary>
    public static int? MonthlyTarget(int? perMonth, DateOnly from, DateOnly to) =>
        perMonth is not { } m ? null : (int)Math.Round(m * Months(from, to), MidpointRounding.AwayFromZero);

    public static int? DailyTarget(int? perDay, DateOnly from, DateOnly to) =>
        perDay is not { } d ? null : d * Enumerable.Range(0, Math.Max(0, to.DayNumber - from.DayNumber + 1)).Count(i => from.AddDays(i).DayOfWeek != DayOfWeek.Sunday);

    private static double Months(DateOnly from, DateOnly to) =>
        from.Day == 1 && to.AddDays(1).Day == 1 ? (to.Year - from.Year) * 12 + to.Month - from.Month + 1 : (to.DayNumber - from.DayNumber + 1) / 30.4375;

    /// <summary>Phone numbers are compared by their digits (last 10), so "+91 98765 43210" and "9876543210" are the same person.</summary>
    public static string PhoneKey(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        return digits.Length > 10 ? digits[^10..] : digits;
    }
}
