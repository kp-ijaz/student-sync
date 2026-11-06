using StudentSync.Contracts;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Web.Services;

// Pure view logic for the student pages (dashboard, subjects, timetable, class page, doubts). Ports of the approved Flutter
// rules (mobile/student_sync/lib). Kept free of Blazor and HTTP so the unit tests compile this file directly; it builds on
// TeachingRules (join window, doubt grouping, greeting, day labels), which is shared with the teacher pages.

/// <summary>When a class offers "Join" — thin wrappers over <see cref="TeachingRules"/> (the single source) for the shapes the student pages use.</summary>
public static class JoinRules
{
    public static bool CanJoin(SessionStatus status, bool hasLink) => TeachingRules.CanJoin(status, hasLink);

    /// <summary>Joinable right now: live, or from 15 minutes before the start until the end (never days ahead).</summary>
    public static bool JoinableNow(SessionStatus status, bool hasLink, DateTime startUtc, DateTime endUtc, DateTime nowUtc) =>
        CanJoin(status, hasLink) && (status == SessionStatus.Live || (startUtc - nowUtc <= TeachingRules.JoinWindow && endUtc > nowUtc));

    public static bool JoinableNow(SessionListItemDto s, DateTime nowUtc) => TeachingRules.JoinableNow(s, nowUtc);

    public static bool JoinableNow(SessionDetailDto s, DateTime nowUtc) =>
        JoinableNow(s.Status, !string.IsNullOrWhiteSpace(s.MeetingUrl), s.StartUtc, s.EndUtc, nowUtc);

    /// <summary>
    /// The class is over: marked completed, or its end time has passed. Teachers add notes and assignments only then
    /// (DESIGN.md §9); the server keeps its own rules.
    /// </summary>
    public static bool HasEnded(SessionStatus status, DateTime endUtc, DateTime nowUtc) => status == SessionStatus.Completed || endUtc <= nowUtc;
}

/// <summary>One section of the Subjects page.</summary>
public sealed record SubjectSection(string Kind, string Title, string Icon, string Tone, IReadOnlyList<MySubjectDto> Items);

/// <summary>Subjects grouping and progress (port of subjects_screen.dart / subject_models.dart).</summary>
public static class SubjectGroups
{
    private static readonly (string Kind, string Title, string Icon, string Tone)[] Sections =
    [
        ("core", "Core subjects", "book-open", "blue"),
        ("language", "Languages", "languages", "orange"),
        ("additional", "Additional subjects", "circle-plus", "green"),
    ];

    /// <summary>Core, then languages, then additional subjects; empty sections are left out. Unknown kinds count as core.</summary>
    public static IReadOnlyList<SubjectSection> Group(IEnumerable<MySubjectDto> subjects)
    {
        var list = subjects.ToList();
        string KindOf(MySubjectDto s) => s.Kind is "language" or "additional" ? s.Kind : "core";
        return Sections
            .Select(x => new SubjectSection(x.Kind, x.Title, x.Icon, x.Tone, list.Where(s => KindOf(s) == x.Kind).OrderBy(s => SlotRank(s.Group)).ToList()))
            .Where(x => x.Items.Count > 0)
            .ToList();
    }

    /// <summary>Languages read in slot order (second before third); subjects without a slot keep their place at the end. Stable.</summary>
    private static int SlotRank(string? group)
    {
        var g = (group ?? "").ToLowerInvariant();
        return g.Contains("first") ? 1 : g.Contains("second") ? 2 : g.Contains("third") ? 3 : g.Length == 0 ? 9 : 5;
    }

    /// <summary>Share of the syllabus covered (0..1): by chapters, falling back to classes.</summary>
    public static double Progress(int chapterCount, int completedChapters, int classCount, int completedClasses) =>
        Math.Clamp(chapterCount > 0 ? (double)completedChapters / chapterCount : classCount > 0 ? (double)completedClasses / classCount : 0, 0, 1);

    public static double Progress(MySubjectDto s) => Progress(s.ChapterCount, s.CompletedChapterCount, s.ClassCount, s.CompletedClassCount);
    public static double Progress(SubjectSyllabusDto s) => Progress(s.ChapterCount, s.CompletedChapterCount, s.ClassCount, s.CompletedClassCount);
}

/// <summary>A colour per subject so the same subject looks the same every day (port of timetable_screen.dart subjectColor).</summary>
public static class SubjectColors
{
    // blue, success, orangeDeep, violet, cyan, pink, navy — same order as mobile.
    private static readonly string[] Palette = ["#1264D8", "#16A36A", "#C2410C", "#7C3AED", "#0891B2", "#DB2777", "#0B2854"];
    public const string None = "#64748B";

    public static string For(string? subject)
    {
        var key = (subject ?? "").ToLowerInvariant();
        if (key.Length == 0) return None;
        var hash = 0;
        foreach (var c in key) hash = (int)(((long)hash * 31 + c) & 0x7fffffff);
        return Palette[hash % Palette.Length];
    }
}

/// <summary>A subject's videos split for its Videos tab.</summary>
public sealed record SubjectMedia(
    IReadOnlyList<(PrerecordedVideoDto Video, string Chapter)> Ready,
    IReadOnlyList<(RecordingDto Recording, string Chapter)> Recordings,
    IReadOnlyList<(PrerecordedVideoDto Video, string Chapter)> Later)
{
    public bool IsEmpty => Ready.Count == 0 && Recordings.Count == 0 && Later.Count == 0;
}

/// <summary>Subject detail logic (port of subject_detail_screen.dart).</summary>
public static class Syllabus
{
    /// <summary>Watchable first (latest class first), class recordings, then locked videos (soonest to open first).</summary>
    public static SubjectMedia Media(IEnumerable<SyllabusChapterDto> chapters)
    {
        var list = chapters.ToList();
        var videos = list.SelectMany(c => c.Videos.Select(v => (Video: v, Chapter: c.Title))).ToList();
        var ready = videos.Where(x => x.Video.IsUnlocked).OrderByDescending(x => x.Video.SessionStartUtc).ToList();
        var later = videos.Where(x => !x.Video.IsUnlocked).OrderBy(x => x.Video.UnlocksAtUtc ?? x.Video.SessionStartUtc).ToList();
        var recordings = list.SelectMany(c => c.Recordings.Select(r => (Recording: r, Chapter: c.Title))).ToList();
        return new SubjectMedia(ready, recordings, later);
    }

    /// <summary>Upcoming (not yet ended, soonest first) and past (latest first) classes of all chapters.</summary>
    public static (List<SessionListItemDto> Upcoming, List<SessionListItemDto> Past) Classes(IEnumerable<SyllabusChapterDto> chapters, DateTime nowUtc)
    {
        var all = chapters.SelectMany(c => c.Sessions).DistinctBy(s => s.Id).ToList();
        return (all.Where(s => s.EndUtc > nowUtc).OrderBy(s => s.StartUtc).ToList(),
                all.Where(s => s.EndUtc <= nowUtc).OrderByDescending(s => s.StartUtc).ToList());
    }

    /// <summary>The chapter that opens by itself: the one in progress, else the next one coming up; null when neither.</summary>
    public static int? FocusIndex(IReadOnlyList<SyllabusChapterDto> chapters)
    {
        for (var i = 0; i < chapters.Count; i++) if (chapters[i].Status == "inProgress") return i;
        for (var i = 0; i < chapters.Count; i++) if (chapters[i].Status == "upcoming") return i;
        return null;
    }

    /// <summary>Label and tone for a chapter status ("completed", "inProgress", "upcoming", "notScheduled").</summary>
    public static (string Label, string Tone) Status(string status) => status switch
    {
        "completed" => ("Completed", "green"),
        "inProgress" => ("In progress", "blue"),
        "upcoming" => ("Coming up", "orange"),
        _ => ("Not scheduled yet", "slate"),
    };
}

/// <summary>How a pre-recorded video's automatic unlock rule reads to a student ("Opens 1 hour after the class").</summary>
public static class VideoRules
{
    public static string OffsetLabel(int minutes) => minutes switch
    {
        0 => "",
        < 60 => $"{minutes} minutes",
        _ when minutes % 10080 == 0 => minutes == 10080 ? "1 week" : $"{minutes / 10080} weeks",
        _ when minutes % 1440 == 0 => minutes == 1440 ? "1 day" : $"{minutes / 1440} days",
        _ when minutes % 60 == 0 => minutes == 60 ? "1 hour" : $"{minutes / 60} hours",
        _ => $"{minutes} minutes",
    };

    public static string RuleText(StudentSync.Domain.Content.VideoUnlockRule rule, int offsetMinutes)
    {
        var when = OffsetLabel(offsetMinutes);
        return rule == StudentSync.Domain.Content.VideoUnlockRule.BeforeClass
            ? (when == "" ? "Opens when the class starts" : $"Opens {when} before the class")
            : (when == "" ? "Opens when the class ends" : $"Opens {when} after the class");
    }

    /// <summary>"5 sec", "12 min" or "1 h 5 min".</summary>
    public static string Length(int seconds) => seconds < 60 ? $"{seconds} sec" : seconds < 3600 ? $"{seconds / 60} min" : $"{seconds / 3600} h {seconds % 3600 / 60} min";

    /// <summary>A video the viewer can play right now: unlocked with a file (managers may also preview locked ones).</summary>
    public static bool CanPlay(PrerecordedVideoDto v) => v.FileId is not null && (v.IsUnlocked || v.CanManage);
}

/// <summary>A teacher the student has upcoming classes with (dashboard "Your teachers").</summary>
public sealed record TeacherClasses(Guid Id, string Name, string? PhotoUrl, string? Subject, IReadOnlyList<SessionListItemDto> Classes);

/// <summary>Student home logic (port of student_home_screen.dart).</summary>
public static class StudentHome
{
    /// <summary>Teachers of the next and upcoming classes, in order of their first class, each with their classes (no duplicates).</summary>
    public static IReadOnlyList<TeacherClasses> Teachers(SessionListItemDto? next, IEnumerable<SessionListItemDto> upcoming)
    {
        var all = (next is null ? upcoming : upcoming.Prepend(next)).DistinctBy(s => s.Id).ToList();
        return all.GroupBy(s => s.TeacherId)
            .Select(g => new TeacherClasses(g.Key, g.First().TeacherName, g.Select(x => x.TeacherPhotoUrl).FirstOrDefault(p => p is not null),
                g.First().SubjectName, g.OrderBy(s => s.StartUtc).ToList()))
            .ToList();
    }

    /// <summary>
    /// Today's remaining classes (by the viewer's local day, not yet ended) and the classes on later days.
    /// <paramref name="localDay"/> maps a UTC instant to the viewer's calendar day.
    /// </summary>
    public static (List<SessionListItemDto> Today, List<SessionListItemDto> Later) SplitByDay(
        SessionListItemDto? next, IEnumerable<SessionListItemDto> upcoming, DateTime nowUtc, Func<DateTime, DateOnly> localDay)
    {
        var all = (next is null ? upcoming : upcoming.Prepend(next)).DistinctBy(s => s.Id).OrderBy(s => s.StartUtc).ToList();
        var today = localDay(nowUtc);
        return (all.Where(s => localDay(s.StartUtc) == today && s.EndUtc > nowUtc).ToList(),
                all.Where(s => s.Id != next?.Id && localDay(s.StartUtc) > today).ToList());
    }
}

/// <summary>Calendar helpers shared by the timetable and the sessions calendar.</summary>
public static class CalendarMath
{
    /// <summary>Monday of the week containing <paramref name="day"/>.</summary>
    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>The 42 days (6 weeks, Monday first) of the month grid containing <paramref name="anchor"/>.</summary>
    public static IEnumerable<DateOnly> MonthDays(DateOnly anchor)
    {
        var start = WeekStart(new DateOnly(anchor.Year, anchor.Month, 1));
        for (var d = start; d < start.AddDays(42); d = d.AddDays(1)) yield return d;
    }

    /// <summary>The UTC instant at which <paramref name="day"/> starts in <paramref name="tz"/> (a skipped midnight moves on an hour).</summary>
    public static DateTime DayStartUtc(DateOnly day, string tz)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(tz); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.Utc; }
        var local = day.ToDateTime(TimeOnly.MinValue);
        return zone.IsInvalidTime(local) ? TimeZoneInfo.ConvertTimeToUtc(local.AddHours(1), zone) : TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    /// <summary>"1 h", "1 h 30 min" or "45 min".</summary>
    public static string Duration(TimeSpan d)
    {
        var minutes = (int)Math.Round(d.TotalMinutes);
        if (minutes < 60) return $"{minutes} min";
        return minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";
    }
}
