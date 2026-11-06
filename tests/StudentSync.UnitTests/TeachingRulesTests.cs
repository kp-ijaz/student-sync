using StudentSync.Contracts;
using StudentSync.Domain.Doubts;
using StudentSync.Domain.Scheduling;
using StudentSync.Web.Services;

namespace StudentSync.UnitTests;

public class TeachingRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static SessionListItemDto Session(DateTime startUtc, SessionStatus status = SessionStatus.Scheduled, bool hasLink = true, int minutes = 60) =>
        new(Guid.NewGuid(), "Class 10 — Mathematics", SessionType.Batch, status, startUtc, startUtc.AddMinutes(minutes), "Asia/Dubai",
            Guid.NewGuid(), "Sample Teacher", null, null, "Mathematics", 5, DeliveryMode.Online, hasLink, false);

    [Fact]
    public void Join_opens_fifteen_minutes_before_start_and_closes_at_the_end()
    {
        Assert.False(TeachingRules.JoinableNow(Session(Now.AddMinutes(16)), Now));
        Assert.True(TeachingRules.JoinableNow(Session(Now.AddMinutes(15)), Now));
        Assert.True(TeachingRules.JoinableNow(Session(Now.AddMinutes(-30)), Now));
        Assert.False(TeachingRules.JoinableNow(Session(Now.AddMinutes(-60)), Now)); // ended exactly now
        Assert.False(TeachingRules.JoinableNow(Session(Now.AddDays(2)), Now));
    }

    [Fact]
    public void Live_class_is_joinable_but_not_without_a_link_or_when_cancelled()
    {
        Assert.True(TeachingRules.JoinableNow(Session(Now.AddHours(3), SessionStatus.Live), Now));
        Assert.False(TeachingRules.JoinableNow(Session(Now.AddMinutes(5), hasLink: false), Now));
        Assert.False(TeachingRules.JoinableNow(Session(Now.AddMinutes(5), SessionStatus.Cancelled), Now));
        Assert.False(TeachingRules.JoinableNow(Session(Now.AddMinutes(-10), SessionStatus.Completed), Now));
    }

    [Theory]
    [InlineData(DoubtStatus.New, true)]
    [InlineData(DoubtStatus.Assigned, true)]
    [InlineData(DoubtStatus.InProgress, true)]
    [InlineData(DoubtStatus.Reopened, true)]
    [InlineData(DoubtStatus.Answered, false)]
    [InlineData(DoubtStatus.PendingConfirmation, false)]
    [InlineData(DoubtStatus.Resolved, false)]
    public void Doubt_is_open_until_answered(DoubtStatus status, bool open) => Assert.Equal(open, TeachingRules.IsOpenDoubt(status));

    [Fact]
    public void Split_doubts_keeps_order_within_each_group()
    {
        DoubtListItemDto D(string title, DoubtStatus s) =>
            new(Guid.NewGuid(), title, s, DoubtPriority.Normal, "Student", null, null, null, null, null, Now, Now, 0);
        var (open, done) = TeachingRules.SplitDoubts([D("a", DoubtStatus.New), D("b", DoubtStatus.Answered), D("c", DoubtStatus.Reopened), D("d", DoubtStatus.Resolved)]);
        Assert.Equal(["a", "c"], open.Select(d => d.Title));
        Assert.Equal(["b", "d"], done.Select(d => d.Title));
    }

    [Theory]
    [InlineData(4, "Good evening")]
    [InlineData(5, "Good morning")]
    [InlineData(11, "Good morning")]
    [InlineData(12, "Good afternoon")]
    [InlineData(17, "Good evening")]
    public void Greeting_follows_the_local_hour(int hour, string expected) => Assert.Equal(expected, TeachingRules.Greeting(hour));

    [Fact]
    public void Day_label_compares_calendar_days_in_the_viewer_zone()
    {
        // 21:30 UTC on 3 Nov is already 01:30 on 4 Nov in Dubai (UTC+4): "Tomorrow" there, "Today" in London.
        var start = new DateTime(2026, 11, 3, 21, 30, 0, DateTimeKind.Utc);
        Assert.Equal("Tomorrow", TeachingRules.DayLabel(start, Now, "Asia/Dubai"));
        Assert.Equal("Today", TeachingRules.DayLabel(start, Now, "Europe/London"));
        Assert.Equal("Yesterday", TeachingRules.DayLabel(Now.AddDays(-1), Now, "UTC"));
        Assert.Equal("Fri 6 Nov", TeachingRules.DayLabel(Now.AddDays(3), Now, "UTC"));
    }
}
