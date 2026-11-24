using StudentSync.Domain.Identity;
using StudentSync.Domain.Marketing;

namespace StudentSync.UnitTests;

/// <summary>Marketing rules (owner request 2026-11-24): how the figures are counted, and what the marketing role may do.</summary>
public sealed class MarketingRulesTests
{
    [Fact]
    public void Admitted_and_not_interested_close_a_lead()
    {
        Assert.All([LeadStatus.New, LeadStatus.Contacted, LeadStatus.Interested, LeadStatus.TrialBooked], s => Assert.True(MarketingRules.IsOpen(s)));
        Assert.All([LeadStatus.Admitted, LeadStatus.NotInterested], s => Assert.False(MarketingRules.IsOpen(s)));
    }

    [Theory]
    [InlineData(LeadActivityOutcome.Answered, true)]
    [InlineData(LeadActivityOutcome.CallBack, true)]
    [InlineData(LeadActivityOutcome.Interested, true)]
    [InlineData(LeadActivityOutcome.NoAnswer, false)]
    [InlineData(LeadActivityOutcome.Busy, false)]
    [InlineData(LeadActivityOutcome.WrongNumber, false)]
    public void A_call_is_reached_unless_nobody_could_be_spoken_to(LeadActivityOutcome outcome, bool reached) => Assert.Equal(reached, MarketingRules.Reached(outcome));

    [Fact]
    public void Percentages_and_costs_are_empty_rather_than_invented_when_there_is_nothing_to_divide_by()
    {
        Assert.Equal(25.0m, MarketingRules.Percent(5, 20));
        Assert.Equal(33.3m, MarketingRules.Percent(1, 3));
        Assert.Null(MarketingRules.Percent(0, 0));
        Assert.Equal(250m, MarketingRules.CostPer(5000, 20));
        Assert.Equal(333.33m, MarketingRules.CostPer(1000, 3));
        Assert.Null(MarketingRules.CostPer(5000, 0));
    }

    [Fact]
    public void A_follow_up_is_overdue_only_when_past_and_the_lead_is_open()
    {
        var today = new DateOnly(2026, 11, 24);
        Assert.True(MarketingRules.Overdue(LeadStatus.Interested, today.AddDays(-1), today));
        Assert.False(MarketingRules.Overdue(LeadStatus.Interested, today, today));
        Assert.False(MarketingRules.Overdue(LeadStatus.Admitted, today.AddDays(-5), today));
        Assert.False(MarketingRules.Overdue(LeadStatus.New, null, today));
    }

    [Theory]
    [InlineData("+91 98765 43210", "9876543210")]
    [InlineData("98765-43210", "9876543210")]
    [InlineData("050 123 4567", "0501234567")]
    [InlineData(null, "")]
    public void Phone_numbers_are_compared_by_their_last_ten_digits(string? phone, string key) => Assert.Equal(key, MarketingRules.PhoneKey(phone));

    [Fact]
    public void The_marketing_role_has_marketing_only()
    {
        Assert.Equal([Permissions.MarketingManage], RolePermissions.For(UserRole.Marketing));
        Assert.True(RolePermissions.Has(UserRole.InstitutionAdmin, Permissions.MarketingManage));
        Assert.All([UserRole.Teacher, UserRole.Student, UserRole.Parent], r => Assert.False(RolePermissions.Has(r, Permissions.MarketingManage)));
    }

    [Fact]
    public void Targets_scale_to_the_period()
    {
        var nov = (From: new DateOnly(2026, 11, 1), To: new DateOnly(2026, 11, 30)); // 25 working days (5 Sundays)
        Assert.Equal(20 * 25, MarketingRules.DailyTarget(20, nov.From, nov.To));
        Assert.Equal(20, MarketingRules.DailyTarget(20, new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 23))); // a Monday
        Assert.Equal(0, MarketingRules.DailyTarget(20, new DateOnly(2026, 11, 22), new DateOnly(2026, 11, 22)));  // a Sunday
        Assert.Equal(40, MarketingRules.MonthlyTarget(40, nov.From, nov.To));
        Assert.Equal(480, MarketingRules.MonthlyTarget(40, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
        Assert.Null(MarketingRules.DailyTarget(null, nov.From, nov.To));
        Assert.Null(MarketingRules.MonthlyTarget(null, nov.From, nov.To));
    }
}
