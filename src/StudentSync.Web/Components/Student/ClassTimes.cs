using StudentSync.Domain.Scheduling;
using StudentSync.Web.Services;

namespace StudentSync.Web.Components.Student;

/// <summary>Short time labels for the student class components, in the viewer's zone (mobile: TimeFormatter).</summary>
public static class ClassTimes
{
    private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>"Tue 3 Nov · 15:30–16:30".</summary>
    public static string Range(DateTime startUtc, DateTime endUtc, string tz) =>
        $"{Time.ToLocal(startUtc, tz).ToString("ddd d MMM", Inv)} · {Time.Clock(startUtc, tz)}–{Time.Clock(endUtc, tz)}";

    /// <summary>"Tue 3 Nov · 15:30".</summary>
    public static string DateTime(DateTime utc, string tz) => $"{Time.ToLocal(utc, tz).ToString("ddd d MMM", Inv)} · {Time.Clock(utc, tz)}";

    public static DateOnly Day(DateTime utc, string tz) => DateOnly.FromDateTime(Time.ToLocal(utc, tz));

    /// <summary>"Today" / "Tomorrow" / "Yesterday" / "Tue 3 Nov" for the class's day.</summary>
    public static string DayLabel(DateTime utc, string tz) => TeachingRules.DayLabel(utc, System.DateTime.UtcNow, tz);

    /// <summary>The organiser's range when their zone differs from the viewer's, else null.</summary>
    public static string? Organiser(DateTime startUtc, DateTime endUtc, string organiserTz, string viewerTz) =>
        organiserTz == viewerTz || Time.Clock(startUtc, organiserTz) == Time.Clock(startUtc, viewerTz)
            ? null
            : $"{Range(startUtc, endUtc, organiserTz)} ({Time.Abbrev(organiserTz)})";

    public static string Mode(DeliveryMode m) => TeachingRules.DeliveryModeLabel(m);
    public static string ModeIcon(DeliveryMode m) => m == DeliveryMode.InPerson ? "map-pin" : "video";
}
