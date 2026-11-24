using StudentSync.Domain.Common;

namespace StudentSync.Domain.Fees;

/// <summary>
/// A teacher's pay per hour from a date. A new rate applies from its EffectiveFrom day onward; earlier classes keep the
/// rate that applied to them, so a raise never rewrites past pay.
/// </summary>
public class TeacherPayRate : TenantEntity
{
    public Guid TeacherId { get; set; }
    public decimal HourlyRate { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public DateOnly EffectiveFrom { get; set; }

    public static void Validate(decimal rate)
    {
        if (rate is < 0 or > 1_000_000) throw new DomainException("Enter a valid rate per hour.", "invalid_rate");
    }
}

/// <summary>Salary paid to a teacher. An admin can correct it (audited before → after) or void it (kept, with who and why).</summary>
public class TeacherPayment : TenantEntity
{
    public Guid TeacherId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public DateOnly PaidOn { get; set; }
    /// <summary>Optional: the period the payment is for (e.g. October), shown on the teacher's pay history.</summary>
    public DateOnly? PeriodFrom { get; set; }
    public DateOnly? PeriodTo { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public string? Note { get; set; }
    public Guid RecordedByUserId { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public Guid? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }

    public static void Validate(decimal amount, DateOnly? from, DateOnly? to)
    {
        if (amount is <= 0 or > 100_000_000) throw new DomainException("Enter an amount greater than zero.", "invalid_amount");
        if (from is { } f && to is { } t && t < f) throw new DomainException("The period ends before it starts.", "invalid_period");
    }
}

/// <summary>Pure pay rules (unit-tested): what a teacher earned for classes taught, each at the rate in force that day.</summary>
public static class TeacherPayRules
{
    public sealed record TaughtClass(DateOnly Date, decimal Hours);
    public sealed record Rate(DateOnly EffectiveFrom, decimal HourlyRate);

    /// <summary>The rate in force on a day: the latest one that started on or before it. No rate yet = 0 (not paid).</summary>
    public static decimal RateOn(IReadOnlyList<Rate> rates, DateOnly day) =>
        rates.Where(r => r.EffectiveFrom <= day).OrderByDescending(r => r.EffectiveFrom).Select(r => r.HourlyRate).FirstOrDefault();

    public static decimal Earned(IEnumerable<TaughtClass> classes, IReadOnlyList<Rate> rates) =>
        decimal.Round(classes.Sum(c => c.Hours * RateOn(rates, c.Date)), 2, MidpointRounding.AwayFromZero);

    /// <summary>Class length in hours, to the minute (a 45-minute class is 0.75 h).</summary>
    public static decimal Hours(DateTime startUtc, DateTime endUtc) => decimal.Round((decimal)(endUtc - startUtc).TotalMinutes / 60m, 2);
}
