using StudentSync.Domain.Common;

namespace StudentSync.Domain.Fees;

/// <summary>What the centre spends money on, apart from teachers' salaries (which are <see cref="TeacherPayment"/>s).</summary>
public enum ExpenseCategory { Rent, Utilities, InternetPhone, Supplies, Marketing, Maintenance, StaffSalaries, Software, Taxes, Other }

/// <summary>
/// Money the centre paid out for running costs (rent, electricity, books…). Teachers' salaries are not expenses here: they
/// are recorded as salary payments, so profit = fees received − salaries paid − expenses never counts a salary twice.
/// </summary>
public class Expense : TenantEntity
{
    public DateOnly SpentOn { get; set; }
    public ExpenseCategory Category { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public string Description { get; set; } = "";
    /// <summary>Who was paid (landlord, shop…), optional.</summary>
    public string? PaidTo { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public Guid RecordedByUserId { get; set; }

    public static void Validate(decimal amount)
    {
        if (amount is <= 0 or > 100_000_000) throw new DomainException("Enter an amount greater than zero.", "invalid_amount");
    }
}

/// <summary>Pure finance rules (unit-tested). Cash basis: money counts on the day it was received or paid.</summary>
public static class FinanceRules
{
    /// <summary>Profit for a period = fees received − salaries paid − other expenses (can be negative: a loss).</summary>
    public static decimal Profit(decimal income, decimal salaries, decimal expenses) => income - salaries - expenses;

    /// <summary>Profit as a share of income, rounded to one decimal; empty when nothing was received.</summary>
    public static decimal? MarginPercent(decimal income, decimal profit) =>
        income <= 0 ? null : decimal.Round(profit / income * 100m, 1, MidpointRounding.AwayFromZero);

    /// <summary>
    /// A former teacher's balance: earned − paid. Positive = the centre still owes them; negative = they were paid in
    /// advance (they owe the centre).
    /// </summary>
    public static decimal TeacherBalance(decimal earned, decimal paid) => earned - paid;
}
