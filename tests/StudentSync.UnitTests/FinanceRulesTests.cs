using StudentSync.Domain.Common;
using StudentSync.Domain.Fees;

namespace StudentSync.UnitTests;

/// <summary>Finance rules (owner request 2026-11-24): cash-basis profit, margin and a former teacher's balance.</summary>
public sealed class FinanceRulesTests
{
    [Theory]
    [InlineData(100000, 40000, 15000, 45000)]
    [InlineData(20000, 25000, 5000, -10000)] // a loss is negative
    [InlineData(0, 0, 0, 0)]
    public void Profit_is_income_minus_salaries_minus_expenses(decimal income, decimal salaries, decimal expenses, decimal profit) =>
        Assert.Equal(profit, FinanceRules.Profit(income, salaries, expenses));

    [Fact]
    public void Margin_is_profit_as_a_share_of_income_and_empty_without_income()
    {
        Assert.Equal(45.0m, FinanceRules.MarginPercent(100000, 45000));
        Assert.Equal(-50.0m, FinanceRules.MarginPercent(20000, -10000));
        Assert.Equal(33.3m, FinanceRules.MarginPercent(3, 1));
        Assert.Null(FinanceRules.MarginPercent(0, -500));
    }

    [Fact]
    public void A_former_teachers_balance_is_earned_minus_paid()
    {
        Assert.Equal(1500m, FinanceRules.TeacherBalance(4500, 3000));   // the centre still owes them
        Assert.Equal(-500m, FinanceRules.TeacherBalance(2500, 3000));   // paid in advance
        Assert.Equal(0m, FinanceRules.TeacherBalance(3000, 3000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_000_001)]
    public void Expense_amount_must_be_positive_and_sane(decimal amount) =>
        Assert.Throws<DomainException>(() => Expense.Validate(amount));
}
