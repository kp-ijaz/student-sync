using StudentSync.Application.Academics;
using StudentSync.Domain.Common;
using StudentSync.Domain.Fees;
using static StudentSync.Domain.Fees.TeacherPayRules;

namespace StudentSync.UnitTests;

/// <summary>Teacher pay (rate per hour from a date) and how class names are recognised by the default catalogue.</summary>
public class TeacherPayAndCatalogueTests
{
    private static readonly DateOnly Oct1 = new(2026, 10, 1), Nov1 = new(2026, 11, 1);

    [Fact]
    public void Each_class_is_paid_at_the_rate_in_force_on_its_day()
    {
        Rate[] rates = [new(Oct1, 300), new(Nov1, 350)];
        TaughtClass[] classes = [new(new(2026, 10, 15), 1), new(new(2026, 10, 31), 1.5m), new(new(2026, 11, 1), 1), new(new(2026, 11, 20), 0.75m)];
        // October: 2.5 h × 300 = 750; November: 1.75 h × 350 = 612.50.
        Assert.Equal(1362.50m, Earned(classes, rates));
    }

    [Fact]
    public void Classes_before_the_first_rate_earn_nothing_until_a_rate_covers_them()
    {
        Rate[] rates = [new(Nov1, 400)];
        Assert.Equal(0m, RateOn(rates, new DateOnly(2026, 10, 31)));
        Assert.Equal(400m, Earned([new(new(2026, 10, 31), 2), new(new(2026, 11, 2), 1)], rates));
        Assert.Equal(0m, Earned([new(Oct1, 3)], []));
    }

    [Fact]
    public void Class_length_counts_to_the_minute()
    {
        var start = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(0.75m, Hours(start, start.AddMinutes(45)));
        Assert.Equal(1.5m, Hours(start, start.AddMinutes(90)));
    }

    [Fact]
    public void Pay_amounts_and_rates_are_validated()
    {
        Assert.Throws<DomainException>(() => TeacherPayment.Validate(0, null, null));
        Assert.Throws<DomainException>(() => TeacherPayment.Validate(100, Nov1, Oct1));
        TeacherPayment.Validate(100, Oct1, Nov1);
        Assert.Throws<DomainException>(() => TeacherPayRate.Validate(-1));
        TeacherPayRate.Validate(0);
    }

    [Theory]
    [InlineData("Class 10", 10)]
    [InlineData("class 1", 1)]
    [InlineData("10", 10)]
    [InlineData("10th", 10)]
    [InlineData("Grade 7", 7)]
    [InlineData("XI", 11)]
    [InlineData("+1", 11)]
    [InlineData("Plus Two", 12)]
    [InlineData("std 3", 3)]
    [InlineData("IX", 9)]
    [InlineData("Kindergarten", null)]
    [InlineData("13", null)]
    [InlineData("", null)]
    public void Class_names_are_recognised_so_existing_classes_are_not_duplicated(string name, int? expected) =>
        Assert.Equal(expected, DefaultCatalogue.ClassNumber(name));
}
