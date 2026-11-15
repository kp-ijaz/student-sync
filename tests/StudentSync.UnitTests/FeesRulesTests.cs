using StudentSync.Application.Fees;
using StudentSync.Domain.Common;
using StudentSync.Domain.Fees;

namespace StudentSync.UnitTests;

/// <summary>Prepaid hour rules: exact-minute hours and what a package may contain.</summary>
public class FeesRulesTests
{
    private static readonly DateTime Start = new(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(60, 1.0)]
    [InlineData(90, 1.5)]
    [InlineData(45, 0.75)]
    [InlineData(40, 0.67)]
    public void Hours_used_are_exact_minutes(int minutes, double hours) =>
        Assert.Equal((decimal)hours, FeeService.HoursOf(Start, Start.AddMinutes(minutes)));

    [Theory]
    [InlineData(30, 12000)]
    [InlineData(0.5, 0)]
    [InlineData(1000, 1)]
    public void Valid_packages_are_accepted(double hours, double price) => StudentPackage.Validate((decimal)hours, (decimal)price);

    [Theory]
    [InlineData(0, 100, "invalid_hours")]
    [InlineData(-2, 100, "invalid_hours")]
    [InlineData(2.25, 100, "invalid_hours")]
    [InlineData(1001, 100, "invalid_hours")]
    [InlineData(10, -1, "invalid_price")]
    public void Invalid_packages_are_refused(double hours, double price, string code) =>
        Assert.Equal(code, Assert.Throws<DomainException>(() => StudentPackage.Validate((decimal)hours, (decimal)price)).Code);

    [Fact]
    public void Due_day_defaults_to_the_start_day()
    {
        var p = new StudentPackage { StartsOn = new DateOnly(2026, 11, 1) };
        Assert.Equal(p.StartsOn, p.DueOn);
        p.PaymentDueOn = new DateOnly(2026, 11, 10);
        Assert.Equal(new DateOnly(2026, 11, 10), p.DueOn);
    }
}
