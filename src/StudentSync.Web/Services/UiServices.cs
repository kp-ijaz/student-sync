using Ganss.Xss;
using Markdig;
using Microsoft.AspNetCore.SignalR.Client;
using StudentSync.Domain.AI;
using StudentSync.Domain.Assessments;
using StudentSync.Domain.Assignments;
using StudentSync.Domain.Doubts;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Web.Services;

/// <summary>Time formatting in the viewer's IANA time zone (and the organiser's zone when it differs).</summary>
public static class Time
{
    public static DateTime ToLocal(DateTime utc, string tz)
    {
        try { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(tz)); }
        catch (TimeZoneNotFoundException) { return utc; }
    }

    public static string Short(DateTime utc, string tz) => ToLocal(utc, tz).ToString("ddd d MMM, HH:mm");
    public static string Date(DateTime utc, string tz) => ToLocal(utc, tz).ToString("d MMM yyyy");
    public static string Clock(DateTime utc, string tz) => ToLocal(utc, tz).ToString("HH:mm");
    public static string Range(DateTime startUtc, DateTime endUtc, string tz) => $"{Short(startUtc, tz)}–{Clock(endUtc, tz)}";

    public static string Relative(DateTime utc)
    {
        var d = utc - DateTime.UtcNow;
        var past = d < TimeSpan.Zero;
        d = d.Duration();
        var text = d.TotalMinutes < 1 ? "now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min" : d.TotalDays < 1 ? $"{(int)d.TotalHours} h" : $"{(int)d.TotalDays} d";
        return text == "now" ? "just now" : past ? $"{text} ago" : $"in {text}";
    }

    public static string Abbrev(string tz) => tz.Contains('/') ? tz[(tz.LastIndexOf('/') + 1)..].Replace('_', ' ') : tz;

    public static readonly string[] Common =
    [
        "UTC", "Asia/Dubai", "Asia/Riyadh", "Asia/Qatar", "Asia/Kolkata", "Asia/Karachi", "Asia/Singapore", "Asia/Tokyo",
        "Europe/London", "Europe/Paris", "Europe/Berlin", "Africa/Cairo", "Africa/Lagos", "America/New_York", "America/Chicago",
        "America/Los_Angeles", "America/Toronto", "Australia/Sydney", "Pacific/Auckland",
    ];
}

public sealed record StatusStyle(string Label, string Tone);

/// <summary>Single source for status labels and colour tones (see DESIGN.md §5).</summary>
public static class StatusStyles
{

    public static StatusStyle For(PersonStatus s) => s switch
    {
        PersonStatus.Active => new("Active", "green"),
        PersonStatus.Suspended => new("Suspended", "red"),
        PersonStatus.Archived => new("Archived", "slate"),
        _ => new("Inactive", "slate"),
    };

    public static StatusStyle For(SessionStatus s) => s switch
    {
        SessionStatus.Scheduled => new("Scheduled", "blue"),
        SessionStatus.Live => new("Live", "orange"),
        SessionStatus.Completed => new("Completed", "green"),
        _ => new("Cancelled", "slate"),
    };
    public static StatusStyle For(SessionType t) => t switch
    {
        SessionType.Batch => new("Batch class", "navy"),
        SessionType.Group => new("Group session", "info"),
        SessionType.OneToOne => new("One-to-one", "orange"),
        _ => new("Private group", "violet"),
    };
    public static StatusStyle For(AttendanceStatus s) => s switch
    {
        AttendanceStatus.Present => new("Present", "green"),
        AttendanceStatus.Late => new("Late", "amber"),
        AttendanceStatus.Absent => new("Absent", "red"),
        _ => new("Excused", "slate"),
    };
    public static StatusStyle For(DoubtStatus s) => s switch
    {
        DoubtStatus.New => new("New", "blue"),
        DoubtStatus.Assigned => new("Assigned", "blue"),
        DoubtStatus.InProgress => new("In progress", "info"),
        DoubtStatus.Answered => new("Answered", "amber"),
        DoubtStatus.PendingConfirmation => new("Awaiting confirmation", "amber"),
        DoubtStatus.Resolved => new("Resolved", "green"),
        _ => new("Reopened", "red"),
    };
    public static StatusStyle For(DoubtPriority p) => p switch
    {
        DoubtPriority.Urgent => new("Urgent", "red"),
        DoubtPriority.High => new("High", "amber"),
        DoubtPriority.Low => new("Low", "slate"),
        _ => new("Normal", "slate"),
    };
    public static StatusStyle For(AssignmentStatus s) => s switch
    {
        AssignmentStatus.Draft => new("Draft", "slate"),
        AssignmentStatus.Published => new("Published", "blue"),
        _ => new("Closed", "slate"),
    };
    public static StatusStyle For(SubmissionStatus s) => s switch
    {
        SubmissionStatus.Draft => new("Draft", "slate"),
        SubmissionStatus.Submitted => new("Submitted", "blue"),
        SubmissionStatus.Graded => new("Graded", "green"),
        _ => new("Corrections requested", "amber"),
    };
    public static StatusStyle For(ExamStatus s) => s switch
    {
        ExamStatus.Draft => new("Draft", "slate"),
        ExamStatus.Published => new("Published", "blue"),
        _ => new("Closed", "slate"),
    };
    public static StatusStyle For(AttemptStatus s) => s switch
    {
        AttemptStatus.InProgress => new("In progress", "orange"),
        AttemptStatus.Submitted => new("Awaiting grading", "amber"),
        _ => new("Graded", "green"),
    };
    public static StatusStyle For(AiDraftStatus s) => s switch
    {
        AiDraftStatus.AwaitingTeacherReview => new("Awaiting your review", "amber"),
        AiDraftStatus.Approved => new("Approved", "blue"),
        AiDraftStatus.Published => new("Published", "green"),
        _ => new("Rejected", "red"),
    };
    public static StatusStyle For(AiPipelineStatus s) => s switch
    {
        AiPipelineStatus.Published => new("Published", "green"),
        AiPipelineStatus.Approved => new("Approved", "blue"),
        AiPipelineStatus.AwaitingTeacherReview or AiPipelineStatus.DraftGenerated => new("Awaiting review", "amber"),
        AiPipelineStatus.AIProcessing => new("Processing", "info"),
        AiPipelineStatus.ProcessingFailed => new("Not processed", "red"),
        AiPipelineStatus.TranscriptReady => new("Transcript ready", "blue"),
        AiPipelineStatus.TranscriptionPending => new("Transcript needed", "slate"),
        _ => new("No recording yet", "slate"),
    };
    public static string RoleLabel(UserRole r) => r switch
    {
        UserRole.SuperAdmin => "Platform admin",
        UserRole.InstitutionAdmin => "Administrator",
        UserRole.Teacher => "Teacher",
        UserRole.Student => "Student",
        _ => "Parent",
    };
}

/// <summary>Markdown → sanitised HTML for notes and instructions.</summary>
public sealed class MarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
    private readonly HtmlSanitizer _sanitizer = new();

    public MarkdownRenderer()
    {
        _sanitizer.AllowedSchemes.Add("mailto");
        _sanitizer.AllowedTags.Add("details");
        _sanitizer.AllowedTags.Add("summary");
        _sanitizer.AllowedAttributes.Add("class");
    }

    public string ToHtml(string? markdown) => _sanitizer.Sanitize(Markdown.ToHtml(markdown ?? "", _pipeline));
}

public enum ToastKind { Success, Error, Info }
public sealed record Toast(Guid Id, ToastKind Kind, string Message);

/// <summary>Per-circuit toast notifications (announced via an ARIA live region).</summary>
/// <summary>
/// What the phone/tablet app bar shows for the current page: PageHeader publishes its title here so the shell can show
/// it in the app bar (as the mobile app does) and the page's own heading can be hidden visually below 1024 px.
/// </summary>
public sealed class ShellState
{
    public event Action? Changed;
    public string? Title { get; private set; }
    private object? _owner;

    /// <summary>Publish the title for the current page. The owner is the PageHeader that set it.</summary>
    public void SetTitle(object owner, string title)
    {
        _owner = owner;
        if (title == Title) return;
        Title = title;
        Changed?.Invoke();
    }

    /// <summary>
    /// Clear the title when its PageHeader goes away — but only if no newer page has already set one. During a
    /// client-side navigation the new page's header initialises before the old one is disposed.
    /// </summary>
    public void Clear(object owner)
    {
        if (!ReferenceEquals(_owner, owner)) return;
        _owner = null;
        Title = null;
        Changed?.Invoke();
    }
}

public sealed class ToastService
{
    public event Action? Changed;
    public List<Toast> Items { get; } = [];

    public void Show(string message, ToastKind kind = ToastKind.Success)
    {
        var toast = new Toast(Guid.NewGuid(), kind, message);
        Items.Add(toast);
        Changed?.Invoke();
        _ = Task.Delay(kind == ToastKind.Error ? 8000 : 4500).ContinueWith(_ => Dismiss(toast.Id));
    }

    public void Error(Exception ex) => Show(ex is ApiException a ? a.Describe() : "Something went wrong. Please try again.", ToastKind.Error);

    public void Dismiss(Guid id)
    {
        Items.RemoveAll(t => t.Id == id);
        Changed?.Invoke();
    }
}

/// <summary>Real-time notifications from the API hub for the signed-in user (per circuit).</summary>
public sealed class RealtimeService(ApiClient api, IConfiguration config, ILogger<RealtimeService> logger) : IAsyncDisposable
{
    private HubConnection? _hub;
    public event Action<string>? NotificationReceived;
    public event Action? MessageReceived;

    public async Task StartAsync()
    {
        if (_hub is not null) return;
        try
        {
            _hub = new HubConnectionBuilder()
                .WithUrl(new Uri(new Uri(config["Api:BaseUrl"]!), "hubs/notifications"), o => o.AccessTokenProvider = async () => (await api.TokensAsync()).AccessToken)
                .WithAutomaticReconnect()
                .Build();
            _hub.On<System.Text.Json.JsonElement>("notification", n => NotificationReceived?.Invoke(n.TryGetProperty("title", out var t) ? t.GetString() ?? "" : ""));
            _hub.On<System.Text.Json.JsonElement>("message", _ => MessageReceived?.Invoke());
            await _hub.StartAsync();
        }
        catch (Exception ex)
        {
            // Real-time is an enhancement; pages still load data on navigation.
            logger.LogWarning(ex, "Real-time connection unavailable");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
    }
}

/// <summary>Minimal UI string localisation (navigation and shell). Arabic switches the layout to right-to-left.</summary>
public static class I18n
{
    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["ar"] = new()
        {
            ["Dashboard"] = "لوحة التحكم",
            ["Sessions"] = "الحصص",
            ["Calendar"] = "التقويم",
            ["Students"] = "الطلاب",
            ["Teachers"] = "المعلمون",
            ["Parents"] = "أولياء الأمور",
            ["Academics"] = "الشؤون الأكاديمية",
            ["Batches"] = "المجموعات",
            ["Private tuition"] = "الدروس الخاصة",
            ["Assignments"] = "الواجبات",
            ["Exams"] = "الاختبارات",
            ["Question bank"] = "بنك الأسئلة",
            ["Doubt Centre"] = "مركز الاستفسارات",
            ["Learning plan"] = "خطة التعلم",
            ["Messages"] = "الرسائل",
            ["Announcements"] = "الإعلانات",
            ["Reports"] = "التقارير",
            ["Settings"] = "الإعدادات",
            ["Library"] = "المكتبة",
            ["Progress"] = "التقدم",
            ["AI assistant"] = "المساعد الذكي",
            ["Notifications"] = "الإشعارات",
            ["Profile"] = "الملف الشخصي",
            ["Sign out"] = "تسجيل الخروج",
            ["My classes"] = "حصصي",
            ["Institutions"] = "المؤسسات",
            ["Plans"] = "الخطط",
            ["Audit log"] = "سجل التدقيق",
            ["Availability"] = "التوفر",
            ["Subjects"] = "المواد",
            ["Timetable"] = "الجدول الدراسي",
            ["Learning"] = "التعلّم",
            ["Tasks"] = "المهام",
            ["Home"] = "الرئيسية",
            ["Classes"] = "الحصص",
            ["Open navigation"] = "فتح القائمة",
        },
        ["ml"] = new()
        {
            ["Dashboard"] = "ഡാഷ്ബോർഡ്",
            ["Sessions"] = "ക്ലാസുകൾ",
            ["Calendar"] = "കലണ്ടർ",
            ["Students"] = "വിദ്യാർത്ഥികൾ",
            ["Teachers"] = "അധ്യാപകർ",
            ["Parents"] = "രക്ഷിതാക്കൾ",
            ["Academics"] = "അക്കാദമിക്",
            ["Batches"] = "ബാച്ചുകൾ",
            ["Private tuition"] = "സ്വകാര്യ ട്യൂഷൻ",
            ["Assignments"] = "അസൈൻമെന്റുകൾ",
            ["Exams"] = "പരീക്ഷകൾ",
            ["Question bank"] = "ചോദ്യ ബാങ്ക്",
            ["Doubt Centre"] = "സംശയ കേന്ദ്രം",
            ["Learning plan"] = "പഠന പദ്ധതി",
            ["Messages"] = "സന്ദേശങ്ങൾ",
            ["Announcements"] = "അറിയിപ്പുകൾ",
            ["Reports"] = "റിപ്പോർട്ടുകൾ",
            ["Settings"] = "ക്രമീകരണങ്ങൾ",
            ["Library"] = "ലൈബ്രറി",
            ["Progress"] = "പുരോഗതി",
            ["AI assistant"] = "AI സഹായി",
            ["Notifications"] = "അറിയിപ്പുകൾ",
            ["Profile"] = "പ്രൊഫൈൽ",
            ["Sign out"] = "സൈൻ ഔട്ട്",
            ["My classes"] = "എന്റെ ക്ലാസുകൾ",
            ["Institutions"] = "സ്ഥാപനങ്ങൾ",
            ["Plans"] = "പ്ലാനുകൾ",
            ["Audit log"] = "ഓഡിറ്റ് ലോഗ്",
            ["Availability"] = "ലഭ്യത",
            ["Subjects"] = "വിഷയങ്ങൾ",
            ["Timetable"] = "ടൈംടേബിൾ",
            ["Learning"] = "പഠനം",
            ["Tasks"] = "ജോലികൾ",
            ["Home"] = "ഹോം",
            ["Classes"] = "ക്ലാസുകൾ",
            ["Open navigation"] = "മെനു തുറക്കുക",
        },
    };

    public static string T(string language, string text) =>
        Strings.TryGetValue(language, out var map) && map.TryGetValue(text, out var v) ? v : text;

    public static bool IsRtl(string language) => language == "ar";
}

/// <summary>A navigation link, or a group of links when <see cref="Children"/> is set (a group has no href of its own).</summary>
public sealed record NavItem(string Label, string Href, string Icon, bool Exact = false, IReadOnlyList<NavItem>? Children = null)
{
    public static NavItem Group(string label, string icon, params NavItem[] children) => new(label, "", icon, Children: children);
}

/// <summary>Role-based navigation from one configuration.</summary>
public static class Navigation
{
    public static IReadOnlyList<NavItem> For(UserRole role) => role switch
    {
        UserRole.SuperAdmin =>
        [
            new("Dashboard", "/platform", "grid", true), new("Institutions", "/platform/tenants", "building"),
            new("Plans", "/platform/plans", "layers"), new("Audit log", "/platform/audit", "shield"),
        ],
        UserRole.InstitutionAdmin =>
        [
            new("Dashboard", "/admin", "grid", true),
            NavItem.Group("Classes", "calendar",
                new("Calendar", "/sessions", "calendar"), new("Schedule a class", "/sessions/new", "plus"),
                new("Pre-recorded videos", "/videos", "video"), new("Batches", "/admin/batches", "layers"), new("Private tuition", "/admin/arrangements", "user")),
            NavItem.Group("People", "users",
                new("Students", "/admin/students", "users"),
                new("Teachers", "/admin/teachers", "user-check")),
            NavItem.Group("Learning", "book",
                new("Academics", "/admin/academics", "book"), new("Assignments", "/assignments", "clipboard"),
                new("Exams", "/exams", "check-square"), new("Doubt Centre", "/doubts", "help")),
            NavItem.Group("Communication", "message",
                new("Messages", "/messages", "message"), new("Announcements", "/announcements", "megaphone")),
            new("Reports", "/admin/reports", "bar-chart"), new("Settings", "/admin/settings", "settings"),
        ],
        UserRole.Teacher =>
        [
            new("Dashboard", "/teacher", "grid", true),
            NavItem.Group("Classes", "calendar",
                new("Timetable", "/sessions", "calendar"), new("Schedule a class", "/sessions/new", "plus"),
                new("Pre-recorded videos", "/videos", "video"), new("Availability", "/teacher/availability", "clock")),
            NavItem.Group("Teaching", "book",
                new("Students", "/teacher/students", "users"), new("Tasks", "/teacher/tasks", "list-checks"),
                new("Assignments", "/assignments", "clipboard"), new("Exams", "/exams", "check-square"),
                new("Question bank", "/questions", "database"), new("Doubt Centre", "/doubts", "help")),
            NavItem.Group("Communication", "message",
                new("Messages", "/messages", "message"), new("Announcements", "/announcements", "megaphone")),
        ],
        // Mirrors the mobile tabs (Home · Subjects · Learning · Doubts). "Learning" is one page with pill tabs
        // (Assignments · Exams · Videos); the old /assignments, /exams and /videos routes keep working. Notes live on
        // each class page (no subject-wide library) and progress opens from Profile.
        UserRole.Student =>
        [
            new("Dashboard", "/student", "grid", true),
            new("Subjects", "/subjects", "book-open"),
            new("Timetable", "/timetable", "calendar"),
            new("Learning", "/learning", "graduation"),
            new("Learning plan", "/learning-plan", "target"),
            new("Doubt Centre", "/doubts", "help"),
            new("AI assistant", "/assistant", "sparkle"),
            NavItem.Group("Communication", "message",
                new("Messages", "/messages", "message"), new("Announcements", "/announcements", "megaphone")),
        ],
        _ =>
        [
            new("Dashboard", "/parent", "grid", true), new("Calendar", "/sessions", "calendar"), new("Messages", "/messages", "message"),
            new("Announcements", "/announcements", "megaphone"),
        ],
    };

    /// <summary>
    /// The floating bottom tab bar shown below 1024 px — the same four tabs as the mobile app (role_shell.dart):
    /// student Home · Subjects · Learning · Doubts; teacher Home · Classes · Students · Tasks. Admins get their four
    /// most-used pages. A tab's <see cref="NavItem.Children"/> are the other routes that belong to it (used only for
    /// highlighting, never rendered); everything else stays reachable from the grouped side menu (drawer).
    /// </summary>
    public static IReadOnlyList<NavItem> Tabs(UserRole role) => role switch
    {
        UserRole.Student =>
        [
            new("Home", "/student", "home", true),
            new("Subjects", "/subjects", "book-open", Children: [new("Timetable", "/timetable", "calendar"), new("Classes", "/sessions", "calendar")]),
            new("Learning", "/learning", "graduation", Children: [new("Assignments", "/assignments", "clipboard"), new("Exams", "/exams", "check-square"), new("Videos", "/videos", "video"), new("Results", "/attempts", "check-square"), new("Library", "/library", "book"), new("Learning plan", "/learning-plan", "target")]),
            new("Doubts", "/doubts", "help"),
        ],
        UserRole.Teacher =>
        [
            new("Home", "/teacher", "grid", true),
            new("Classes", "/sessions", "calendar", Children: [new("Videos", "/videos", "video"), new("Availability", "/teacher/availability", "clock")]),
            new("Students", "/teacher/students", "users", Children: [new("Student", "/students", "user")]),
            new("Tasks", "/teacher/tasks", "list-checks", Children: [new("Assignments", "/assignments", "clipboard"), new("Exams", "/exams", "check-square"), new("Question bank", "/questions", "database"), new("Doubt Centre", "/doubts", "help")]),
        ],
        UserRole.InstitutionAdmin =>
        [
            new("Home", "/admin", "grid", true),
            new("Students", "/admin/students", "users", Children: [new("Student", "/students", "user")]),
            new("Classes", "/sessions", "calendar", Children: [new("Batches", "/admin/batches", "layers"), new("Private tuition", "/admin/arrangements", "user"), new("Videos", "/videos", "video")]),
            new("Reports", "/admin/reports", "bar-chart"),
        ],
        _ => [],
    };

    /// <summary>The tab that owns the current path: the tab itself or the tab whose child route matches.</summary>
    public static NavItem? ActiveTab(IReadOnlyList<NavItem> tabs, string path)
    {
        path = Normalize(path);
        // Unlike side-menu groups, a tab has its own href as well as child routes, so match both.
        var best = tabs.SelectMany(t => (t.Children ?? []).Prepend(t).Select(item => (Tab: t, Item: item)))
            .Where(x => Matches(x.Item, path))
            .OrderByDescending(x => x.Item.Href.Length)
            .FirstOrDefault();
        return best.Tab;
    }

    /// <summary>
    /// The single link that should show as current: the longest href matching the path, so
    /// "/sessions/new" highlights "Schedule a class" rather than also highlighting "Calendar".
    /// </summary>
    public static NavItem? Active(IReadOnlyList<NavItem> items, string path)
    {
        path = Normalize(path);
        return items.SelectMany(i => i.Children ?? [i]).Where(i => Matches(i, path)).MaxBy(i => i.Href.Length);
    }

    private static string Normalize(string path) => "/" + path.Split('?', '#')[0].Trim('/');

    private static bool Matches(NavItem item, string path) =>
        item.Href != "" && (item.Exact ? path == item.Href : path == item.Href || path.StartsWith(item.Href.TrimEnd('/') + "/", StringComparison.Ordinal));

    public static string Home(UserRole role) => role switch
    {
        UserRole.SuperAdmin => "/platform",
        UserRole.InstitutionAdmin => "/admin",
        UserRole.Teacher => "/teacher",
        UserRole.Student => "/student",
        _ => "/parent",
    };
}

/// <summary>ARIA state values must be the strings "true"/"false"; Blazor renders raw booleans as empty attributes.</summary>
public static class Aria
{
    public static string Bool(bool value) => value ? "true" : "false";
}

/// <summary>
/// Cover photo for a class, chosen from its subject (or title) — a port of the mobile <c>ClassCovers</c>
/// (mobile/student_sync/lib/core/widgets/class_cover.dart). Keep both lists identical.
/// </summary>
public static class ClassCovers
{
    // Order matters: the first match wins ("Social Science" is social, not science;
    // "Applied Mathematics" is maths; "Business Studies" is commerce).
    private static readonly (string[] Keywords, string File)[] ByKeyword =
    [
        (["social", "history", "geograph", "politic", "civics", "psycholog", "sociolog"], "social"),
        (["account", "business", "economic", "commerce", "finance"], "commerce"),
        (["math", "algebra", "geometry", "calculus", "trigonometr", "statistic", "quadratic"], "maths"),
        (["physic", "mechanic", "optic", "electric"], "physics"),
        (["chemi", "organic"], "chemistry"),
        (["hindi", "malayalam", "arabic", "french", "sanskrit", "tamil", "urdu", "language"], "languages"),
        (["english", "ielts", "literature", "writing", "reading", "grammar", "essay"], "english"),
        (["science", "biology", "world around us", "environment", "evs", "stem", "lab"], "science"),
    ];

    public const string Default = "images/covers/study.jpg";

    /// <summary>The subject decides first; the title (or course name) only when the subject gives no hint.</summary>
    public static string For(string? subject, string? title) => Match(subject) ?? Match(title) ?? Default;

    private static string? Match(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.ToLowerInvariant();
        foreach (var (keywords, file) in ByKeyword)
            if (keywords.Any(t.Contains)) return $"images/covers/{file}.jpg";
        return null;
    }
}

/// <summary>unDraw illustrations in wwwroot/illustrations (recoloured to Royal Blue). Decorative: the text carries the meaning.</summary>
public static class Illustrations
{
    public const string Empty = "empty", Search = "search", Offline = "offline", ServerError = "server_error",
        AccessDenied = "access_denied", NotFound = "not_found", AllDone = "all_done", Questions = "questions", Mailbox = "mailbox";

    public static string Src(string name) => $"illustrations/{name}.svg";

    /// <summary>The illustration for a failed API call: offline/unreachable, forbidden, not found, or a server error.</summary>
    public static string ForError(ApiException error) => error.Status switch
    {
        0 or 503 when error.Code is "unavailable" => Offline,
        403 => AccessDenied,
        404 => NotFound,
        _ => ServerError,
    };
}

/// <summary>One home shortcut for <c>ShortcutGrid</c>: a label, a link and a tinted icon.</summary>
public sealed record ShortcutItem(string Label, string Href, string Icon, string Tone = "blue");

/// <summary>Icon tile for a dashboard stat, keyed by <c>StatDto.Key</c> (never by the display label).</summary>
public static class StatIcons
{
    public static (string Icon, string Tone)? For(string? key) => key switch
    {
        "students" => ("users", "blue"),
        "teachers" => ("graduation", "violet"),
        "courses" => ("layers", "navy"),
        "sessions" or "week" => ("calendar", "info"),
        "doubts" => ("help", "orange"),
        "grading" => ("clipboard-check", "green"),
        _ => null,
    };
}

/// <summary>Display helpers for people.</summary>
public static class People
{
    /// <summary>"Aisha Khan (Demo Student)" → "AK": first and last initials, ignoring bracketed suffixes (as on mobile).</summary>
    public static string Initials(string? name)
    {
        var cleaned = System.Text.RegularExpressions.Regex.Replace(name ?? "", @"\([^)]*\)", " ");
        var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "?";
        var first = parts[0][..1];
        var last = parts.Length > 1 ? parts[^1][..1] : "";
        return (first + last).ToUpperInvariant();
    }

    /// <summary>"Amira Haddad (Demo Teacher)" → "Amira Haddad".</summary>
    public static string ShortName(string? name) => System.Text.RegularExpressions.Regex.Replace(name ?? "", @"\s*\([^)]*\)", "").Trim();
}
