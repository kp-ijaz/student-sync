using StudentSync.Domain.Assessments;

namespace StudentSync.UnitTests;

/// <summary>Addendum C: diagnostic results are aggregated per subject/chapter, flag weak areas below a threshold, and never flag unmarked work.</summary>
public sealed class DiagnosticBreakdownTests
{
    private static readonly Guid Maths = Guid.NewGuid(), Science = Guid.NewGuid(), Ch1 = Guid.NewGuid(), Ch2 = Guid.NewGuid();

    private static List<DiagnosticItem> Items() =>
    [
        new(Maths, Ch1, 1, 0, false, true),    // wrong
        new(Maths, Ch1, 1, 0, false, false),   // unanswered
        new(Maths, Ch2, 2, 2, true, true),
        new(Maths, Ch2, 1, 1, true, true),
        new(Science, null, 1, 1, true, true),
        new(Science, null, 1, 0, false, true),
    ];

    [Fact]
    public void Rows_are_grouped_by_chapter_and_by_subject_with_percentages()
    {
        var byChapter = DiagnosticBreakdown.ByChapter(Items());
        var ch1 = byChapter.Single(r => r.ChapterId == Ch1);
        Assert.Equal((2, 1, 0, 2m, 0m, 0m), (ch1.Questions, ch1.Answered, ch1.Correct, ch1.Marks, ch1.Awarded, ch1.Percent));
        var ch2 = byChapter.Single(r => r.ChapterId == Ch2);
        Assert.Equal(100m, ch2.Percent);
        var science = byChapter.Single(r => r.SubjectId == Science);
        Assert.Null(science.ChapterId);
        Assert.Equal(50m, science.Percent);

        var bySubject = DiagnosticBreakdown.BySubject(Items());
        Assert.Equal(60m, bySubject.Single(r => r.SubjectId == Maths).Percent); // 3 of 5 marks
    }

    [Fact]
    public void Areas_below_the_threshold_need_attention_and_the_threshold_is_configurable()
    {
        var rows = DiagnosticBreakdown.ByChapter(Items());
        Assert.True(DiagnosticBreakdown.NeedsAttention(rows.Single(r => r.ChapterId == Ch1)));
        Assert.False(DiagnosticBreakdown.NeedsAttention(rows.Single(r => r.ChapterId == Ch2)));
        var science = rows.Single(r => r.SubjectId == Science);
        Assert.False(DiagnosticBreakdown.NeedsAttention(science));          // 50% is not below 50%
        Assert.True(DiagnosticBreakdown.NeedsAttention(science, 60));       // but it is below 60%
    }

    [Fact]
    public void Unmarked_written_answers_leave_the_row_pending_and_never_flagged()
    {
        var rows = DiagnosticBreakdown.ByChapter([new DiagnosticItem(Maths, Ch1, 3, null, null, true), new DiagnosticItem(Maths, Ch1, 1, 0, false, true)]);
        var row = rows.Single();
        Assert.True(row.Pending);
        Assert.Null(row.Percent);
        Assert.False(DiagnosticBreakdown.NeedsAttention(row));
    }
}
