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

/// <summary>Codes are assigned automatically (owner request 2026-11-17).</summary>
public class CatalogueCodeTests
{
    private static HashSet<string> Taken(params string[] codes) => new(codes, StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("Kerala State (SCERT)", "KERALA")]
    [InlineData("CBSE", "NCERT")]
    [InlineData("ICSE / ISC", "ICSE")]
    [InlineData("Gulf State Board", "GSB")]
    [InlineData("Tamil Nadu State Board", "TNSB")]   // a state board is not Kerala
    [InlineData("IGCSE", "IGCSE")]
    public void Syllabus_codes_come_from_the_name(string name, string code) =>
        Assert.Equal(code, StudentSync.Application.Academics.DefaultCatalogue.SyllabusCode(name, Taken()));

    [Fact]
    public void Codes_never_repeat()
    {
        var taken = Taken("NCERT", "NCERT2", "NCERT-C11");
        Assert.Equal("NCERT3", StudentSync.Application.Academics.DefaultCatalogue.SyllabusCode("CBSE", taken));
        Assert.Equal("NCERT-C112", StudentSync.Application.Academics.DefaultCatalogue.CourseCode("Class 11 · NCERT", "+1", "NCERT", taken));
        Assert.Contains("NCERT-C112", taken);
    }

    [Theory]
    [InlineData("Class 5 · NCERT", "Class 5", "NCERT", "NCERT-C5")]
    [InlineData("Plus one", "+1", "KERALA", "KERALA-C11")]
    [InlineData("Spoken English", null, null, "SE")]
    [InlineData("Abacus", "LKG", null, "ABACUS")]
    public void Course_codes_use_the_syllabus_and_class(string name, string? grade, string? syllabus, string code) =>
        Assert.Equal(code, StudentSync.Application.Academics.DefaultCatalogue.CourseCode(name, grade, syllabus, Taken()));

    [Fact]
    public void Subject_codes_extend_the_course_code() =>
        Assert.Equal("NCERT-C5-MATH", StudentSync.Application.Academics.DefaultCatalogue.SubjectCode("NCERT-C5", "Mathematics", Taken()));
}
