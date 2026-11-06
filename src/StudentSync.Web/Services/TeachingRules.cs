using StudentSync.Contracts;
using StudentSync.Domain.Doubts;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Web.Services;

/// <summary>
/// Pure display rules shared by web pages and mirrored from the mobile app, kept free of UI dependencies so they can be
/// unit-tested (tests/StudentSync.UnitTests links this file). Mobile sources: features/sessions/join_button.dart,
/// features/doubts/doubts_list_screen.dart, core/widgets/home_greeting.dart, core/time/time_format.dart.
/// </summary>
public static class TeachingRules
{
    /// <summary>How long before the start a class may be joined.</summary>
    public static readonly TimeSpan JoinWindow = TimeSpan.FromMinutes(15);

    /// <summary>A class can be joined at all: it has a meeting link and is scheduled or live.</summary>
    public static bool CanJoin(SessionStatus status, bool hasMeetingLink) =>
        hasMeetingLink && status is SessionStatus.Scheduled or SessionStatus.Live;

    /// <summary>Joinable right now: live, or from 15 minutes before the start until the end (never days ahead).</summary>
    public static bool JoinableNow(SessionListItemDto s, DateTime nowUtc) =>
        CanJoin(s.Status, s.HasMeetingLink) &&
        (s.Status == SessionStatus.Live || (s.StartUtc - nowUtc <= JoinWindow && s.EndUtc > nowUtc));

    /// <summary>Still waiting for the teacher: not answered, awaiting the student's confirmation, or resolved.</summary>
    public static bool IsOpenDoubt(DoubtStatus status) =>
        status is not (DoubtStatus.Resolved or DoubtStatus.Answered or DoubtStatus.PendingConfirmation);

    /// <summary>Splits doubts into "to answer" and "answered &amp; closed", keeping the incoming order.</summary>
    public static (IReadOnlyList<DoubtListItemDto> Open, IReadOnlyList<DoubtListItemDto> Done) SplitDoubts(IEnumerable<DoubtListItemDto> items)
    {
        var all = items.ToList();
        return (all.Where(d => IsOpenDoubt(d.Status)).ToList(), all.Where(d => !IsOpenDoubt(d.Status)).ToList());
    }

    /// <summary>Time-of-day greeting for the viewer's local hour (matches the mobile HomeGreeting).</summary>
    public static string Greeting(int localHour) =>
        localHour < 5 ? "Good evening" : localHour < 12 ? "Good morning" : localHour < 17 ? "Good afternoon" : "Good evening";

    /// <summary>"Today", "Tomorrow", "Yesterday" or "Tue 3 Nov", compared by calendar day in the viewer's time zone.</summary>
    public static string DayLabel(DateTime utc, DateTime nowUtc, string tz)
    {
        var day = ToLocal(utc, tz);
        var diff = (day.Date - ToLocal(nowUtc, tz).Date).Days;
        return diff switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "Yesterday",
            _ => day.ToString("ddd d MMM", System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    public static string DeliveryModeLabel(DeliveryMode mode) => mode switch
    {
        DeliveryMode.InPerson => "In person",
        DeliveryMode.Hybrid => "Hybrid",
        _ => "Online",
    };

    private static DateTime ToLocal(DateTime utc, string tz)
    {
        try { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(tz)); }
        catch (TimeZoneNotFoundException) { return utc; }
    }
}
