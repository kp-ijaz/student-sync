using StudentSync.Domain.Fees;
using StudentSync.Domain.People;

namespace StudentSync.Contracts;

// ---------- Finance: income, salaries, expenses and profit (cash basis) ----------

/// <summary>Plain names for payment methods and expense categories: one source for every screen, receipt and export.</summary>
public static class FinanceLabels
{
    public static string Method(PaymentMethod m) => m switch
    {
        PaymentMethod.BankTransfer => "Bank transfer",
        PaymentMethod.Upi => "UPI",
        PaymentMethod.Online => "Online (gateway)",
        _ => m.ToString(),
    };

    public static string Category(ExpenseCategory c) => c switch
    {
        ExpenseCategory.Utilities => "Electricity & water",
        ExpenseCategory.InternetPhone => "Internet & phone",
        ExpenseCategory.Supplies => "Books & supplies",
        ExpenseCategory.StaffSalaries => "Other staff pay",
        _ => c.ToString(),
    };

    public static string Kind(FinanceEntryKind k) => k switch { FinanceEntryKind.Income => "Fee received", FinanceEntryKind.Salary => "Salary paid", _ => "Expense" };
}

public sealed record ExpenseDto(Guid Id, DateOnly SpentOn, ExpenseCategory Category, decimal Amount, string CurrencyCode, string Description,
    string? PaidTo, PaymentMethod Method, string? Reference, string? RecordedBy);

public sealed record SaveExpenseRequest(DateOnly SpentOn, ExpenseCategory Category, decimal Amount, string Description,
    string? PaidTo = null, PaymentMethod Method = PaymentMethod.Cash, string? Reference = null);

public enum FinanceEntryKind { Income, Salary, Expense }

/// <summary>
/// One line of money in or out. <paramref name="RecordId"/> is the fee payment, salary payment or expense id (receipt,
/// salary slip or expense edit); <paramref name="PersonId"/> links income to the student and salary to the teacher.
/// </summary>
public sealed record FinanceEntryDto(DateOnly Date, FinanceEntryKind Kind, string Title, string? Detail, decimal Amount, Guid? PersonId, Guid? ExpenseId,
    Guid RecordId = default, PaymentMethod? Method = null, string? Reference = null, ExpenseCategory? Category = null);

public enum FinanceSort { Newest, Oldest, Largest, Smallest }

/// <summary>Filters for the transaction list and its CSV export. Search matches the person, description, note, paid-to and reference.</summary>
public sealed record FinanceEntryQuery : PageQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public FinanceEntryKind? Kind { get; init; }
    public ExpenseCategory? Category { get; init; }
    public FinanceSort? Sort { get; init; }
}

/// <summary>
/// A page of transactions plus figures for everything that matches the filters (not just the page): money in, money out,
/// net, and how many lines of each kind match (for the tab counts).
/// </summary>
public sealed record FinanceEntriesDto(PagedResult<FinanceEntryDto> Page, decimal MoneyIn, decimal MoneyOut, decimal Net,
    int IncomeCount, int SalaryCount, int ExpenseCount);

/// <summary>Totals for one period.</summary>
public sealed record FinanceTotalsDto(DateOnly From, DateOnly To, decimal Income, decimal Salaries, decimal Expenses, decimal Profit);

public sealed record CategoryTotalDto(ExpenseCategory Category, decimal Amount);

/// <summary>One month of the trend: money received and paid out, by the day it happened.</summary>
public sealed record FinanceMonthDto(DateOnly Month, decimal Income, decimal Salaries, decimal Expenses, decimal Profit);

/// <summary>
/// Money for a period, cash basis. Income = fee payments received (removed payments excluded); Salaries = salary payments
/// made; Expenses = other running costs; Profit = Income − Salaries − Expenses. <paramref name="Previous"/> is the period of
/// the same length just before, for comparison. FeesToCollect / SalariesOwed are today's outstanding amounts (not limited
/// to the period). The transactions themselves come from GET /finance/entries.
/// </summary>
public sealed record FinanceSummaryDto(
    DateOnly From, DateOnly To, string CurrencyCode,
    decimal Income, decimal Salaries, decimal Expenses, decimal Profit, decimal? MarginPercent,
    decimal FeesToCollect, decimal SalariesOwed,
    IReadOnlyList<CategoryTotalDto> ExpensesByCategory, IReadOnlyList<FinanceMonthDto> Months,
    FinanceTotalsDto Previous);

/// <summary>A student who left (archived/inactive) and still owes fees.</summary>
public sealed record FormerStudentBalanceDto(Guid StudentId, string Name, string? StudentCode, PersonStatus Status, decimal Due, DateOnly? DueOn, int DaysOverdue);

/// <summary>
/// A teacher who left with an open balance. <paramref name="Balance"/> = earned − paid: positive = the centre still owes
/// them; negative = they were paid in advance and owe the centre.
/// </summary>
public sealed record FormerTeacherBalanceDto(Guid TeacherId, string Name, PersonStatus Status, decimal Earned, decimal Paid, decimal Balance);

/// <summary>Money still open with people who are no longer active at the centre, kept apart from the current lists.</summary>
public sealed record FormerBalancesDto(string CurrencyCode, decimal StudentsOwe, decimal OwedToTeachers, decimal TeachersOwe,
    IReadOnlyList<FormerStudentBalanceDto> Students, IReadOnlyList<FormerTeacherBalanceDto> Teachers);
