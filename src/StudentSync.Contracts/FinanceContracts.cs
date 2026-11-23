using StudentSync.Domain.Fees;
using StudentSync.Domain.People;

namespace StudentSync.Contracts;

// ---------- Finance: income, salaries, expenses and profit (cash basis) ----------

public sealed record ExpenseDto(Guid Id, DateOnly SpentOn, ExpenseCategory Category, decimal Amount, string CurrencyCode, string Description,
    string? PaidTo, PaymentMethod Method, string? Reference, string? RecordedBy);

public sealed record SaveExpenseRequest(DateOnly SpentOn, ExpenseCategory Category, decimal Amount, string Description,
    string? PaidTo = null, PaymentMethod Method = PaymentMethod.Cash, string? Reference = null);

public enum FinanceEntryKind { Income, Salary, Expense }

/// <summary>One line of money in or out. <paramref name="PersonId"/> links income to the student and salary to the teacher.</summary>
public sealed record FinanceEntryDto(DateOnly Date, FinanceEntryKind Kind, string Title, string? Detail, decimal Amount, Guid? PersonId, Guid? ExpenseId);

public sealed record CategoryTotalDto(ExpenseCategory Category, decimal Amount);

/// <summary>One month of the trend: money received and paid out, by the day it happened.</summary>
public sealed record FinanceMonthDto(DateOnly Month, decimal Income, decimal Salaries, decimal Expenses, decimal Profit);

/// <summary>
/// Money for a period, cash basis. Income = fee payments received (removed payments excluded); Salaries = salary payments
/// made; Expenses = other running costs; Profit = Income − Salaries − Expenses. FeesToCollect / SalariesOwed are today's
/// outstanding amounts (not limited to the period). <paramref name="EntriesTotal"/> counts all lines; Entries holds the newest.
/// </summary>
public sealed record FinanceSummaryDto(
    DateOnly From, DateOnly To, string CurrencyCode,
    decimal Income, decimal Salaries, decimal Expenses, decimal Profit, decimal? MarginPercent,
    decimal FeesToCollect, decimal SalariesOwed,
    IReadOnlyList<CategoryTotalDto> ExpensesByCategory, IReadOnlyList<FinanceMonthDto> Months,
    int EntriesTotal, IReadOnlyList<FinanceEntryDto> Entries);

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
