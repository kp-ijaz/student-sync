using StudentSync.Domain.Common;
using StudentSync.Domain.People;

namespace StudentSync.Domain.Fees;

public enum PackageStatus { Active, Cancelled }
public enum PaymentMethod { Cash, Card, BankTransfer, Upi, Cheque, Other, Online }

/// <summary>
/// Prepaid learning hours added for one student (addendum A): usable across any subject. The name, hours and price are
/// recorded on the sale itself, so later price changes never rewrite what the student bought.
/// </summary>
public class StudentPackage : TenantEntity
{
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public string Name { get; set; } = "";
    public decimal Hours { get; set; }
    public decimal Price { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    /// <summary>Hours count from this day: classes attended before it are not taken from this package.</summary>
    public DateOnly StartsOn { get; set; }
    /// <summary><see cref="StartsOn"/> at midnight in the centre's time zone, so hours used can be counted in the database.</summary>
    public DateTime CountsFromUtc { get; set; }
    /// <summary>When the fee should be paid. Empty means "on purchase" (the start day).</summary>
    public DateOnly? PaymentDueOn { get; set; }
    public string? Notes { get; set; }
    public PackageStatus Status { get; set; } = PackageStatus.Active;
    public DateTime? CancelledAtUtc { get; set; }
    public List<FeePayment> Payments { get; set; } = [];

    public DateOnly DueOn => PaymentDueOn ?? StartsOn;

    public static void Validate(decimal hours, decimal price)
    {
        if (hours is <= 0 or > 1000) throw new DomainException("Hours must be between 0.5 and 1000.", "invalid_hours");
        if (decimal.Round(hours * 2) != hours * 2) throw new DomainException("Hours must be whole or half hours.", "invalid_hours");
        if (price is < 0 or > 100_000_000) throw new DomainException("Enter a valid price.", "invalid_price");
    }
}

/// <summary>
/// A package the centre offers (its fee structure), e.g. "30 hours" for 15,000, optionally only for one class and/or
/// syllabus. Picking a plan fills in the hours and price when hours are added for a student; the sale copies them, so
/// editing or deleting a plan never changes what a student already bought.
/// </summary>
public class FeePlan : TenantEntity
{
    public string Name { get; set; } = "";
    public decimal Hours { get; set; }
    public decimal Price { get; set; }
    /// <summary>Only offered for students in this class; empty = any class.</summary>
    public Guid? GradeLevelId { get; set; }
    /// <summary>Only offered for students on this syllabus; empty = any syllabus.</summary>
    public Guid? CurriculumId { get; set; }
    public string? Notes { get; set; }
    /// <summary>Hidden plans stay listed for the admin but are not offered when adding hours.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Price per hour, rounded to the cent (shown next to every plan so plans can be compared).</summary>
    public static decimal PerHour(decimal hours, decimal price) => hours <= 0 ? 0 : decimal.Round(price / hours, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Money received for a package. Never edited: a mistake is voided (kept, with who and why) and recorded again.</summary>
public class FeePayment : TenantEntity
{
    public Guid PackageId { get; set; }
    public Guid StudentId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaidOn { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public Guid RecordedByUserId { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public Guid? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
}
