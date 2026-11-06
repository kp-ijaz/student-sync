using StudentSync.Domain.Common;
using StudentSync.Domain.People;

namespace StudentSync.Domain.Assessments;

public enum QuestionType { SingleChoice, MultipleChoice, TrueFalse, ShortAnswer, LongAnswer, FillBlank, Matching, Numerical }
public enum Difficulty { Easy, Medium, Hard }
public enum ExamStatus { Draft, Published, Closed }
public enum AttemptStatus { InProgress, Submitted, Graded }
/// <summary>Diagnostic: an entry assessment whose per-subject/chapter result feeds the student's learning needs (addendum C).</summary>
public enum ExamPurpose { Regular, Diagnostic }
/// <summary>PaperEntered: the student answered on paper (OMR-style sheet); staff entered the marked options against the key.</summary>
public enum AttemptEntryMode { Digital, PaperEntered }

public class Question : TenantEntity
{
    public Guid SubjectId { get; set; }
    public Guid? ChapterId { get; set; }
    public QuestionType Type { get; set; }
    public Difficulty Difficulty { get; set; } = Difficulty.Medium;
    public string Stem { get; set; } = "";
    public string? Explanation { get; set; }
    public decimal DefaultMarks { get; set; } = 1;
    /// <summary>Accepted answers for short/fill-blank questions, separated by '|'. Empty means manual grading.</summary>
    public string? AcceptedAnswers { get; set; }
    public decimal? NumericAnswer { get; set; }
    public decimal NumericTolerance { get; set; }
    public bool IsArchived { get; set; }
    /// <summary>Optional diagram/image shown with the stem (a private StoredFile with purpose QuestionImage).</summary>
    public Guid? ImageFileId { get; set; }
    public List<QuestionOption> Options { get; set; } = [];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Stem)) throw new DomainException("Question text is required.", "invalid_question");
        if (DefaultMarks <= 0) throw new DomainException("Marks must be greater than zero.", "invalid_question");
        var correct = Options.Count(o => o.IsCorrect);
        switch (Type)
        {
            case QuestionType.SingleChoice or QuestionType.TrueFalse when Options.Count < 2 || correct != 1:
                throw new DomainException("Single-choice and true/false questions need at least two options and exactly one correct answer.", "invalid_question");
            case QuestionType.MultipleChoice when Options.Count < 2 || correct < 1:
                throw new DomainException("Multiple-choice questions need at least two options and one or more correct answers.", "invalid_question");
            case QuestionType.Matching when Options.Count < 2 || Options.Any(o => string.IsNullOrWhiteSpace(o.MatchText)):
                throw new DomainException("Matching questions need at least two pairs, each with a match.", "invalid_question");
            case QuestionType.Numerical when NumericAnswer is null:
                throw new DomainException("Numerical questions need an answer.", "invalid_question");
        }
    }
}

public class QuestionOption : Entity
{
    public Guid QuestionId { get; set; }
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
    /// <summary>For matching questions: the right-hand item this option pairs with.</summary>
    public string? MatchText { get; set; }
    public int SortOrder { get; set; }
}

public class Exam : TenantEntity
{
    public string Title { get; set; } = "";
    public string? Instructions { get; set; }
    public Guid TeacherId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? BatchId { get; set; }
    public DateTime OpensAtUtc { get; set; }
    public DateTime ClosesAtUtc { get; set; }
    public int DurationMinutes { get; set; }
    public int MaxAttempts { get; set; } = 1;
    public bool ShuffleQuestions { get; set; }
    public ExamStatus Status { get; set; } = ExamStatus.Draft;
    public bool ResultsReleased { get; set; }
    public ExamPurpose Purpose { get; set; } = ExamPurpose.Regular;
    /// <summary>Diagnostic metadata: the class and syllabus the questions were written for.</summary>
    public Guid? GradeLevelId { get; set; }
    public Guid? CurriculumId { get; set; }
    public List<ExamQuestion> Questions { get; set; } = [];
    public List<ExamRecipient> Recipients { get; set; } = [];

    public decimal TotalMarks => Questions.Sum(q => q.Marks);

    public void ValidateForPublish()
    {
        if (Questions.Count == 0) throw new DomainException("Add at least one question before publishing.", "no_questions");
        if (Recipients.Count == 0) throw new DomainException("Choose at least one student before publishing.", "no_recipients");
        if (ClosesAtUtc <= OpensAtUtc) throw new DomainException("The exam must close after it opens.", "invalid_time_range");
        if (DurationMinutes is < 1 or > 600) throw new DomainException("Duration must be between 1 and 600 minutes.", "invalid_duration");
    }
}

public class ExamQuestion : TenantEntity
{
    public Guid ExamId { get; set; }
    public Guid QuestionId { get; set; }
    public Question? Question { get; set; }
    public int SortOrder { get; set; }
    public decimal Marks { get; set; }
}

public class ExamRecipient : TenantEntity
{
    public Guid ExamId { get; set; }
    public Guid StudentId { get; set; }
}

public class ExamAttempt : TenantEntity
{
    public Guid ExamId { get; set; }
    public Exam? Exam { get; set; }
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public int AttemptNumber { get; set; }
    public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;
    public DateTime StartedAtUtc { get; set; }
    /// <summary>Server-computed deadline: the earlier of start + duration and the exam closing time.</summary>
    public DateTime DeadlineUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    /// <summary>Comma-separated ExamQuestion ids in the order presented to this student.</summary>
    public string QuestionOrder { get; set; } = "";
    public decimal? Score { get; set; }
    public bool RequiresManualGrading { get; set; }
    public AttemptEntryMode EntryMode { get; set; } = AttemptEntryMode.Digital;
    /// <summary>Staff member who entered a paper attempt.</summary>
    public Guid? EnteredByUserId { get; set; }
    public List<ExamAnswer> Answers { get; set; } = [];

    public bool IsExpired(DateTime nowUtc) => nowUtc > DeadlineUtc.AddSeconds(30);
}

public class ExamAnswer : TenantEntity
{
    public Guid AttemptId { get; set; }
    public Guid ExamQuestionId { get; set; }
    public string ResponseJson { get; set; } = "{}";
    public bool? IsCorrect { get; set; }
    public decimal? AwardedMarks { get; set; }
    public string? Feedback { get; set; }
    public Guid? GradedByUserId { get; set; }
}

/// <summary>A student's response. Only the fields relevant to the question type are used.</summary>
public sealed record ExamResponse(List<Guid>? OptionIds = null, string? Text = null, decimal? Number = null, Dictionary<Guid, string>? Matches = null);

public readonly record struct GradeResult(bool AutoGraded, bool? IsCorrect, decimal? Awarded);

public static class QuestionGrader
{
    public static GradeResult Grade(Question q, ExamResponse? r, decimal marks)
    {
        r ??= new ExamResponse();
        switch (q.Type)
        {
            case QuestionType.SingleChoice or QuestionType.TrueFalse or QuestionType.MultipleChoice:
                {
                    var correct = q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
                    var chosen = (r.OptionIds ?? []).ToHashSet();
                    var ok = chosen.SetEquals(correct);
                    return new GradeResult(true, ok, ok ? marks : 0);
                }
            case QuestionType.Numerical:
                {
                    var ok = r.Number is { } n && q.NumericAnswer is { } a && Math.Abs(n - a) <= q.NumericTolerance;
                    return new GradeResult(true, ok, ok ? marks : 0);
                }
            case QuestionType.ShortAnswer or QuestionType.FillBlank when !string.IsNullOrWhiteSpace(q.AcceptedAnswers):
                {
                    var accepted = q.AcceptedAnswers!.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    var given = Normalise(r.Text);
                    var ok = given.Length > 0 && accepted.Any(a => Normalise(a) == given);
                    return new GradeResult(true, ok, ok ? marks : 0);
                }
            case QuestionType.Matching:
                {
                    var pairs = q.Options.Count;
                    var right = q.Options.Count(o => r.Matches is not null && r.Matches.TryGetValue(o.Id, out var m)
                                                     && Normalise(m) == Normalise(o.MatchText));
                    var awarded = pairs == 0 ? 0 : Math.Round(marks * right / pairs, 2);
                    return new GradeResult(true, right == pairs, awarded);
                }
            default:
                // Long answers and short answers without an answer key are graded by the teacher.
                return new GradeResult(false, null, null);
        }
    }

    private static string Normalise(string? s) => string.Join(' ', (s ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>One row of a diagnostic breakdown: how the student did on one subject or chapter of the assessment.</summary>
public sealed record DiagnosticRow(Guid SubjectId, Guid? ChapterId, int Questions, int Answered, int Correct, decimal Marks, decimal Awarded, bool Pending)
{
    public decimal? Percent => Pending || Marks == 0 ? null : Math.Round(Awarded * 100 / Marks, 1);
}

/// <summary>Input to the breakdown: one answered question with where it belongs and how it was marked.</summary>
public sealed record DiagnosticItem(Guid SubjectId, Guid? ChapterId, decimal Marks, decimal? Awarded, bool? IsCorrect, bool Answered);

/// <summary>
/// Pure aggregation of an attempt by subject and chapter. Rows are grouped per chapter (NULL chapter = questions with no
/// chapter) and per subject. A row "needs attention" when its percentage is below the threshold; rows with ungraded
/// answers are Pending and never flagged, so a teacher's unfinished marking cannot label a student.
/// </summary>
public static class DiagnosticBreakdown
{
    public const decimal DefaultThresholdPercent = 50;

    public static IReadOnlyList<DiagnosticRow> ByChapter(IEnumerable<DiagnosticItem> items) =>
        items.GroupBy(i => (i.SubjectId, i.ChapterId)).Select(g => Row(g.Key.SubjectId, g.Key.ChapterId, g)).ToList();

    public static IReadOnlyList<DiagnosticRow> BySubject(IEnumerable<DiagnosticItem> items) =>
        items.GroupBy(i => i.SubjectId).Select(g => Row(g.Key, null, g)).ToList();

    public static bool NeedsAttention(DiagnosticRow row, decimal thresholdPercent = DefaultThresholdPercent) =>
        row.Percent is { } p && p < thresholdPercent;

    private static DiagnosticRow Row(Guid subjectId, Guid? chapterId, IEnumerable<DiagnosticItem> g)
    {
        var list = g.ToList();
        return new DiagnosticRow(subjectId, chapterId, list.Count, list.Count(i => i.Answered), list.Count(i => i.IsCorrect == true),
            list.Sum(i => i.Marks), list.Sum(i => i.Awarded ?? 0), list.Any(i => i.Awarded is null));
    }
}
