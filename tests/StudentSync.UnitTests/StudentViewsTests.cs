using StudentSync.Contracts;
using StudentSync.Domain.Content;
using StudentSync.Domain.Scheduling;
using StudentSync.Web.Services;

namespace StudentSync.UnitTests;

/// <summary>Student page view rules (src/StudentSync.Web/Services/StudentViews.cs), ported from the approved mobile app.</summary>
public class StudentViewsTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TeacherA = Guid.NewGuid(), TeacherB = Guid.NewGuid();

    private static SessionListItemDto Session(DateTime startUtc, Guid? teacher = null, SessionStatus status = SessionStatus.Scheduled, string? photo = null) =>
        new(Guid.NewGuid(), "Class 10 — Mathematics", SessionType.Batch, status, startUtc, startUtc.AddHours(1), "Asia/Dubai",
            teacher ?? TeacherA, "Sample Teacher (Demo)", null, null, "Mathematics", 5, DeliveryMode.Online, true, false, photo);

    private static MySubjectDto Subject(string name, string kind, int chapters = 0, int doneChapters = 0, int classes = 0, int doneClasses = 0, string? group = null) =>
        new(Guid.NewGuid(), name, name.ToUpperInvariant(), "Class 10", [], chapters, doneChapters, classes, doneClasses, 0, 0, null, kind, group);

    private static PrerecordedVideoDto Video(string title, bool unlocked, DateTime sessionStart, DateTime? unlocksAt = null, bool canManage = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Class", sessionStart, sessionStart.AddHours(1), false, title, null, 60, VideoUnlockRule.AfterClass, 0,
            VideoAccessOverride.Automatic, unlocked, unlocksAt, unlocked || canManage ? Guid.NewGuid() : null, canManage, sessionStart, "rv");

    private static SyllabusChapterDto Chapter(string title, string status, IReadOnlyList<SessionListItemDto>? sessions = null, IReadOnlyList<PrerecordedVideoDto>? videos = null) =>
        new(Guid.NewGuid(), title, 0, status, sessions ?? [], videos ?? [], []);

    [Fact]
    public void Subjects_are_grouped_core_then_languages_then_additional_and_empty_groups_are_left_out()
    {
        var sections = SubjectGroups.Group([
            Subject("Hindi", "language", group: "Second language"),
            Subject("Mathematics", "core"),
            Subject("Painting", "additional"),
            Subject("English", "core"),
        ]);
        Assert.Equal(["core", "language", "additional"], sections.Select(s => s.Kind));
        Assert.Equal(["Mathematics", "English"], sections[0].Items.Select(s => s.Name));
        Assert.Equal("Languages", sections[1].Title);

        var onlyCore = SubjectGroups.Group([Subject("Science", "core"), Subject("Odd", "unknown-kind")]);
        Assert.Single(onlyCore);
        Assert.Equal(2, onlyCore[0].Items.Count); // unknown kinds are treated as core
        Assert.Empty(SubjectGroups.Group([]));
    }

    [Fact]
    public void Syllabus_progress_uses_chapters_then_falls_back_to_classes()
    {
        Assert.Equal(0.25, SubjectGroups.Progress(Subject("A", "core", chapters: 4, doneChapters: 1, classes: 10, doneClasses: 9)));
        Assert.Equal(0.5, SubjectGroups.Progress(Subject("B", "core", classes: 4, doneClasses: 2)));
        Assert.Equal(0, SubjectGroups.Progress(Subject("C", "core")));
    }

    [Fact]
    public void Subject_colour_is_stable_and_case_insensitive()
    {
        Assert.Equal(SubjectColors.For("Mathematics"), SubjectColors.For("MATHEMATICS"));
        Assert.Equal(SubjectColors.None, SubjectColors.For(null));
        Assert.Equal(SubjectColors.None, SubjectColors.For(""));
        Assert.StartsWith("#", SubjectColors.For("Physics"));
    }

    [Fact]
    public void Videos_tab_lists_watchable_latest_first_and_locked_soonest_first()
    {
        var chapters = new[]
        {
            Chapter("One", "completed", videos: [Video("old", true, Now.AddDays(-5)), Video("later-2", false, Now.AddDays(2), Now.AddDays(3))]),
            Chapter("Two", "inProgress", videos: [Video("new", true, Now.AddDays(-1)), Video("later-1", false, Now.AddDays(1), Now.AddDays(1))]),
        };
        var media = Syllabus.Media(chapters);
        Assert.Equal(["new", "old"], media.Ready.Select(x => x.Video.Title));
        Assert.Equal(["later-1", "later-2"], media.Later.Select(x => x.Video.Title));
        Assert.Equal("Two", media.Ready[0].Chapter);
        Assert.True(Syllabus.Media([Chapter("Empty", "notScheduled")]).IsEmpty);
    }

    [Fact]
    public void Locked_videos_are_never_playable_for_students()
    {
        Assert.False(VideoRules.CanPlay(Video("locked", false, Now)));
        Assert.True(VideoRules.CanPlay(Video("open", true, Now)));
        Assert.True(VideoRules.CanPlay(Video("teacher preview", false, Now, canManage: true)));
    }

    [Fact]
    public void Unlock_rule_reads_naturally()
    {
        Assert.Equal("Opens when the class ends", VideoRules.RuleText(VideoUnlockRule.AfterClass, 0));
        Assert.Equal("Opens 1 hour before the class", VideoRules.RuleText(VideoUnlockRule.BeforeClass, 60));
        Assert.Equal("Opens 2 days after the class", VideoRules.RuleText(VideoUnlockRule.AfterClass, 2880));
        Assert.Equal("Opens 30 minutes before the class", VideoRules.RuleText(VideoUnlockRule.BeforeClass, 30));
        Assert.Equal("5 sec", VideoRules.Length(5));
        Assert.Equal("1 h 5 min", VideoRules.Length(3900));
    }

    [Fact]
    public void Classes_split_into_upcoming_soonest_first_and_past_latest_first_without_duplicates()
    {
        var running = Session(Now.AddMinutes(-30));          // not ended yet → upcoming
        var past1 = Session(Now.AddDays(-2));
        var past2 = Session(Now.AddDays(-1));
        var next = Session(Now.AddDays(1));
        var chapters = new[] { Chapter("A", "completed", [past1, past2]), Chapter("B", "inProgress", [next, running, next]) };
        var (upcoming, past) = Syllabus.Classes(chapters, Now);
        Assert.Equal([running.Id, next.Id], upcoming.Select(s => s.Id));
        Assert.Equal([past2.Id, past1.Id], past.Select(s => s.Id));
    }

    [Fact]
    public void Chapter_in_progress_opens_first_else_the_next_upcoming()
    {
        Assert.Equal(1, Syllabus.FocusIndex([Chapter("a", "completed"), Chapter("b", "inProgress"), Chapter("c", "upcoming")]));
        Assert.Equal(2, Syllabus.FocusIndex([Chapter("a", "completed"), Chapter("b", "notScheduled"), Chapter("c", "upcoming")]));
        Assert.Null(Syllabus.FocusIndex([Chapter("a", "completed")]));
        Assert.Equal(("In progress", "blue"), Syllabus.Status("inProgress"));
    }

    [Fact]
    public void Home_splits_today_from_later_days_in_the_viewers_zone()
    {
        // Dubai (UTC+4): 3 Nov local runs 2 Nov 20:00 UTC → 3 Nov 20:00 UTC.
        DateOnly Day(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai")));
        var next = Session(Now.AddHours(2));                       // today
        var ended = Session(Now.AddHours(-3));                     // today but over
        var lateTonight = Session(new DateTime(2026, 11, 3, 19, 30, 0, DateTimeKind.Utc)); // 23:30 local, still today
        var tomorrow = Session(new DateTime(2026, 11, 3, 20, 30, 0, DateTimeKind.Utc));    // 00:30 local on 4 Nov
        var (today, later) = StudentHome.SplitByDay(next, [ended, next, lateTonight, tomorrow], Now, Day);
        Assert.Equal([next.Id, lateTonight.Id], today.Select(s => s.Id));
        Assert.Equal([tomorrow.Id], later.Select(s => s.Id));
    }

    [Fact]
    public void Teachers_are_collected_once_each_with_their_classes()
    {
        var a1 = Session(Now.AddDays(1), TeacherA, photo: "/a.jpg");
        var b1 = Session(Now.AddDays(2), TeacherB);
        var a2 = Session(Now.AddDays(3), TeacherA);
        var teachers = StudentHome.Teachers(a1, [a1, b1, a2]);
        Assert.Equal([TeacherA, TeacherB], teachers.Select(t => t.Id));
        Assert.Equal([a1.Id, a2.Id], teachers[0].Classes.Select(s => s.Id));
        Assert.Equal("/a.jpg", teachers[0].PhotoUrl);
        Assert.Empty(StudentHome.Teachers(null, []));
    }

    [Fact]
    public void Notes_and_assignments_open_once_the_class_has_ended()
    {
        Assert.False(JoinRules.HasEnded(SessionStatus.Scheduled, Now.AddMinutes(1), Now));
        Assert.True(JoinRules.HasEnded(SessionStatus.Scheduled, Now, Now));
        Assert.True(JoinRules.HasEnded(SessionStatus.Completed, Now.AddHours(1), Now)); // marked completed early
        Assert.False(JoinRules.HasEnded(SessionStatus.Live, Now.AddMinutes(10), Now));
    }

    [Fact]
    public void Class_page_join_follows_the_same_window_using_the_meeting_link()
    {
        Assert.True(JoinRules.JoinableNow(SessionStatus.Scheduled, true, Now.AddMinutes(10), Now.AddMinutes(70), Now));
        Assert.False(JoinRules.JoinableNow(SessionStatus.Scheduled, true, Now.AddMinutes(20), Now.AddMinutes(80), Now));
        Assert.False(JoinRules.JoinableNow(SessionStatus.Cancelled, true, Now, Now.AddHours(1), Now));
        Assert.True(JoinRules.JoinableNow(SessionStatus.Live, true, Now.AddHours(2), Now.AddHours(3), Now));
    }

    [Fact]
    public void Calendar_weeks_start_on_monday_and_days_start_at_local_midnight()
    {
        Assert.Equal(new DateOnly(2026, 11, 2), CalendarMath.WeekStart(new DateOnly(2026, 11, 3)));  // Tue → Mon
        Assert.Equal(new DateOnly(2026, 11, 2), CalendarMath.WeekStart(new DateOnly(2026, 11, 8)));  // Sun → Mon
        var month = CalendarMath.MonthDays(new DateOnly(2026, 11, 15)).ToList();
        Assert.Equal(42, month.Count);
        Assert.Equal(DayOfWeek.Monday, month[0].DayOfWeek);
        Assert.Equal(new DateOnly(2026, 10, 26), month[0]);
        Assert.Equal(new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc), CalendarMath.DayStartUtc(new DateOnly(2026, 11, 3), "Asia/Dubai"));
        Assert.Equal("1 h", CalendarMath.Duration(TimeSpan.FromHours(1)));
        Assert.Equal("1 h 30 min", CalendarMath.Duration(TimeSpan.FromMinutes(90)));
        Assert.Equal("45 min", CalendarMath.Duration(TimeSpan.FromMinutes(45)));
    }
}
