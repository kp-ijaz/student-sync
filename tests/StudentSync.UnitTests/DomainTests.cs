using StudentSync.Application.AI;
using StudentSync.Application.Jobs;
using StudentSync.Domain.AI;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Assessments;
using StudentSync.Domain.Assignments;
using StudentSync.Domain.Common;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Content;
using StudentSync.Domain.Doubts;
using StudentSync.Domain.Scheduling;

namespace StudentSync.UnitTests;

public class ZonedTimeTests
{
    [Fact]
    public void Dubai_evening_class_maps_to_correct_instant_for_viewers_worldwide()
    {
        // 19:00 in Dubai (UTC+4, no DST) is 15:00 UTC.
        var z = ZonedTime.ToUtc(new DateOnly(2026, 11, 2), new TimeOnly(19, 0), "Asia/Dubai");
        Assert.Equal(new DateTime(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc), z.Utc);
        Assert.Equal(LocalTimeResolution.Exact, z.Resolution);
        Assert.Equal(15, ZonedTime.ToLocal(z.Utc, "Europe/London").Hour);       // GMT in November
        Assert.Equal(20, ZonedTime.ToLocal(z.Utc, "Asia/Kolkata").Hour);        // 20:30 IST
        Assert.Equal(30, ZonedTime.ToLocal(z.Utc, "Asia/Kolkata").Minute);
        Assert.Equal(10, ZonedTime.ToLocal(z.Utc, "America/New_York").Hour);    // EST
    }

    [Fact]
    public void Nonexistent_local_time_in_spring_gap_is_shifted_forward_by_the_gap()
    {
        // US clocks jump from 02:00 to 03:00 on 8 March 2026; 02:30 does not exist.
        var z = ZonedTime.ToUtc(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), "America/New_York");
        Assert.Equal(LocalTimeResolution.SkippedShiftedForward, z.Resolution);
        Assert.Equal(new TimeOnly(3, 30), TimeOnly.FromDateTime(ZonedTime.ToLocal(z.Utc, "America/New_York")));
    }

    [Fact]
    public void Ambiguous_local_time_in_autumn_overlap_uses_first_occurrence()
    {
        // US clocks fall back at 02:00 on 1 November 2026; 01:30 happens twice. First is EDT (UTC-4).
        var z = ZonedTime.ToUtc(new DateOnly(2026, 11, 1), new TimeOnly(1, 30), "America/New_York");
        Assert.Equal(LocalTimeResolution.AmbiguousEarlierChosen, z.Resolution);
        Assert.Equal(new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc), z.Utc);
    }

    [Theory]
    [InlineData("Asia/Dubai", true)]
    [InlineData("Europe/London", true)]
    [InlineData("UTC", true)]
    [InlineData("Not/AZone", false)]
    [InlineData("", false)]
    public void Only_valid_iana_ids_are_accepted(string id, bool valid) => Assert.Equal(valid, ZonedTime.TryFind(id, out _));
}

public class RecurrenceTests
{
    [Fact]
    public void Weekly_pattern_produces_requested_weekdays_and_count()
    {
        var p = new RecurrencePattern(RecurrenceFrequency.Weekly, 1, WeekDays.Monday | WeekDays.Wednesday, new DateOnly(2026, 11, 2), null, 6);
        var dates = p.Dates().ToList();
        Assert.Equal(6, dates.Count);
        Assert.All(dates, d => Assert.Contains(d.DayOfWeek, new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }));
        Assert.Equal(new DateOnly(2026, 11, 2), dates[0]);
        Assert.Equal(new DateOnly(2026, 11, 18), dates[^1]);
    }

    [Fact]
    public void Fortnightly_pattern_skips_alternate_weeks()
    {
        var p = new RecurrencePattern(RecurrenceFrequency.Weekly, 2, WeekDays.Saturday, new DateOnly(2026, 11, 7), new DateOnly(2026, 12, 31), null);
        var dates = p.Dates().ToList();
        Assert.Equal([new(2026, 11, 7), new(2026, 11, 21), new(2026, 12, 5), new(2026, 12, 19)], dates);
    }

    [Fact]
    public void Recurring_lesson_keeps_wall_clock_time_across_dst_change()
    {
        // Weekly 17:00 London lessons spanning the end of BST (25 Oct 2026): UTC instant changes, local time does not.
        var p = new RecurrencePattern(RecurrenceFrequency.Weekly, 1, WeekDays.Tuesday, new DateOnly(2026, 10, 20), null, 2);
        var instants = p.Dates().Select(d => ZonedTime.ToUtc(d, new TimeOnly(17, 0), "Europe/London").Utc).ToList();
        Assert.Equal(16, instants[0].Hour); // BST = UTC+1
        Assert.Equal(17, instants[1].Hour); // GMT = UTC+0
        Assert.All(instants, i => Assert.Equal(17, ZonedTime.ToLocal(i, "Europe/London").Hour));
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(13, 5)]
    public void Invalid_interval_is_rejected(int interval, int count)
        => Assert.Throws<DomainException>(() => new RecurrencePattern(RecurrenceFrequency.Daily, interval, WeekDays.None, new DateOnly(2026, 1, 1), null, count).Validate());

    [Fact]
    public void Open_ended_schedule_is_rejected()
        => Assert.Throws<DomainException>(() => new RecurrencePattern(RecurrenceFrequency.Daily, 1, WeekDays.None, new DateOnly(2026, 1, 1), null, null).Validate());

    [Fact]
    public void Weekly_pattern_without_days_is_rejected()
        => Assert.Throws<DomainException>(() => new RecurrencePattern(RecurrenceFrequency.Weekly, 1, WeekDays.None, new DateOnly(2026, 1, 1), null, 3).Validate());
}

public class TeachingModeTests
{
    [Fact]
    public void One_to_one_session_needs_no_batch_and_exactly_one_student()
    {
        LearningSession.ValidateShape(SessionType.OneToOne, null, 1);
        Assert.Throws<DomainException>(() => LearningSession.ValidateShape(SessionType.OneToOne, null, 2));
        Assert.Throws<DomainException>(() => LearningSession.ValidateShape(SessionType.OneToOne, null, 0));
        Assert.Throws<DomainException>(() => LearningSession.ValidateShape(SessionType.OneToOne, Guid.NewGuid(), 1));
    }

    [Fact]
    public void Group_session_may_exist_without_a_batch_and_may_optionally_reference_one()
    {
        LearningSession.ValidateShape(SessionType.Group, null, 3);
        LearningSession.ValidateShape(SessionType.Group, Guid.NewGuid(), 3);
        Assert.Throws<DomainException>(() => LearningSession.ValidateShape(SessionType.Group, null, 0));
    }

    [Fact]
    public void Batch_session_requires_a_batch_and_private_group_forbids_one()
    {
        Assert.Throws<DomainException>(() => LearningSession.ValidateShape(SessionType.Batch, null, 5));
        LearningSession.ValidateShape(SessionType.Batch, Guid.NewGuid(), 5);
        Assert.Throws<DomainException>(() => LearningSession.ValidateShape(SessionType.PrivateGroup, Guid.NewGuid(), 2));
    }

    [Fact]
    public void Reschedule_preserves_previous_time_in_history()
    {
        var start = new DateTime(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc);
        var s = new LearningSession { Id = Guid.NewGuid(), StartUtc = start, EndUtc = start.AddHours(1), TimeZoneId = "Asia/Dubai" };
        var change = s.Reschedule(start.AddDays(1), start.AddDays(1).AddHours(1), "Asia/Dubai", "Holiday", Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal(start, change.PreviousStartUtc);
        Assert.Equal(start.AddDays(1), s.StartUtc);
        Assert.Equal(ScheduleChangeType.Rescheduled, change.ChangeType);
    }

    [Fact]
    public void Cancelled_session_cannot_be_rescheduled_or_completed()
    {
        var s = new LearningSession { StartUtc = DateTime.UtcNow, EndUtc = DateTime.UtcNow.AddHours(1) };
        s.Cancel("Weather", Guid.NewGuid(), DateTime.UtcNow);
        Assert.Throws<DomainException>(() => s.Reschedule(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1), "UTC", null, Guid.NewGuid(), DateTime.UtcNow));
        Assert.Throws<DomainException>(() => s.SetStatus(SessionStatus.Completed));
    }

    [Fact]
    public void Session_must_end_after_it_starts()
        => Assert.Throws<DomainException>(() => LearningSession.ValidateTimes(DateTime.UtcNow, DateTime.UtcNow.AddMinutes(-5)));
}

public class DoubtWorkflowTests
{
    [Fact]
    public void Answered_doubt_can_be_confirmed_resolved_and_reopened()
    {
        var d = new Doubt { Status = DoubtStatus.Answered };
        var change = d.TransitionTo(DoubtStatus.Resolved, Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal(DoubtStatus.Answered, change.FromStatus);
        Assert.NotNull(d.ResolvedAtUtc);
        d.TransitionTo(DoubtStatus.Reopened, Guid.NewGuid(), DateTime.UtcNow);
        Assert.Null(d.ResolvedAtUtc);
    }

    [Theory]
    [InlineData(DoubtStatus.New, DoubtStatus.Resolved)]
    [InlineData(DoubtStatus.Resolved, DoubtStatus.Answered)]
    [InlineData(DoubtStatus.PendingConfirmation, DoubtStatus.InProgress)]
    public void Invalid_transitions_are_rejected(DoubtStatus from, DoubtStatus to)
        => Assert.Throws<DomainException>(() => new Doubt { Status = from }.TransitionTo(to, Guid.NewGuid(), DateTime.UtcNow));

    [Fact]
    public void Discussing_in_class_records_discussion_time()
    {
        var d = new Doubt { Status = DoubtStatus.Assigned };
        d.TransitionTo(DoubtStatus.PendingConfirmation, Guid.NewGuid(), DateTime.UtcNow);
        Assert.NotNull(d.DiscussedAtUtc);
    }
}

public class GradingTests
{
    private static Question Choice(QuestionType type, params bool[] correct)
    {
        var q = new Question { Type = type, Stem = "Q", DefaultMarks = 2 };
        foreach (var c in correct) q.Options.Add(new QuestionOption { Id = Guid.NewGuid(), Text = "o", IsCorrect = c });
        return q;
    }

    [Fact]
    public void Single_choice_awards_full_marks_only_for_correct_option()
    {
        var q = Choice(QuestionType.SingleChoice, false, true);
        Assert.Equal(2, QuestionGrader.Grade(q, new ExamResponse([q.Options[1].Id]), 2).Awarded);
        Assert.Equal(0, QuestionGrader.Grade(q, new ExamResponse([q.Options[0].Id]), 2).Awarded);
        Assert.Equal(0, QuestionGrader.Grade(q, null, 2).Awarded);
    }

    [Fact]
    public void Multiple_choice_requires_the_exact_set()
    {
        var q = Choice(QuestionType.MultipleChoice, true, false, true);
        Assert.True(QuestionGrader.Grade(q, new ExamResponse([q.Options[0].Id, q.Options[2].Id]), 2).IsCorrect);
        Assert.False(QuestionGrader.Grade(q, new ExamResponse([q.Options[0].Id]), 2).IsCorrect);
    }

    [Fact]
    public void Numerical_answers_respect_tolerance()
    {
        var q = new Question { Type = QuestionType.Numerical, Stem = "x", NumericAnswer = 9.81m, NumericTolerance = 0.05m };
        Assert.True(QuestionGrader.Grade(q, new ExamResponse(Number: 9.8m), 1).IsCorrect);
        Assert.False(QuestionGrader.Grade(q, new ExamResponse(Number: 9.7m), 1).IsCorrect);
    }

    [Fact]
    public void Short_answer_matches_accepted_answers_case_and_space_insensitively()
    {
        var q = new Question { Type = QuestionType.ShortAnswer, Stem = "x", AcceptedAnswers = "photosynthesis|photo synthesis" };
        Assert.True(QuestionGrader.Grade(q, new ExamResponse(Text: "  Photosynthesis "), 1).IsCorrect);
    }

    [Fact]
    public void Long_answers_are_left_for_manual_grading()
        => Assert.False(QuestionGrader.Grade(new Question { Type = QuestionType.LongAnswer, Stem = "x" }, new ExamResponse(Text: "essay"), 5).AutoGraded);

    [Fact]
    public void Matching_awards_partial_credit()
    {
        var q = new Question { Type = QuestionType.Matching, Stem = "m" };
        q.Options.Add(new QuestionOption { Id = Guid.NewGuid(), Text = "H2O", MatchText = "Water" });
        q.Options.Add(new QuestionOption { Id = Guid.NewGuid(), Text = "NaCl", MatchText = "Salt" });
        var r = QuestionGrader.Grade(q, new ExamResponse(Matches: new() { [q.Options[0].Id] = "water", [q.Options[1].Id] = "Sugar" }), 4);
        Assert.Equal(2, r.Awarded);
        Assert.False(r.IsCorrect);
    }

    [Fact]
    public void Question_validation_requires_one_correct_single_choice_option()
        => Assert.Throws<DomainException>(() => Choice(QuestionType.SingleChoice, true, true).Validate());
}

public class AssignmentRulesTests
{
    private static Assignment Published(DateTime due, bool late)
    {
        var a = new Assignment { DueAtUtc = due, MaxMarks = 10, AllowLateSubmission = late };
        a.Recipients.Add(new AssignmentRecipient { StudentId = Guid.NewGuid() });
        a.Publish(DateTime.UtcNow.AddDays(-1));
        return a;
    }

    [Fact]
    public void Late_submission_is_rejected_unless_allowed()
    {
        var now = DateTime.UtcNow;
        Assert.Throws<DomainException>(() => new AssignmentSubmission { TextAnswer = "x" }.Submit(Published(now.AddHours(-1), false), now));
        var s = new AssignmentSubmission { TextAnswer = "x" };
        s.Submit(Published(now.AddHours(-1), true), now);
        Assert.True(s.IsLate);
    }

    [Fact]
    public void Empty_submission_is_rejected()
        => Assert.Throws<DomainException>(() => new AssignmentSubmission().Submit(Published(DateTime.UtcNow.AddDays(1), false), DateTime.UtcNow));

    [Fact]
    public void Marks_must_be_within_range_and_graded_work_is_locked()
    {
        var a = Published(DateTime.UtcNow.AddDays(1), false);
        var s = new AssignmentSubmission { TextAnswer = "x" };
        s.Submit(a, DateTime.UtcNow);
        Assert.Throws<DomainException>(() => s.Grade(11, 10, null, false, Guid.NewGuid(), DateTime.UtcNow));
        s.Grade(8, 10, "Good", false, Guid.NewGuid(), DateTime.UtcNow);
        Assert.Throws<DomainException>(() => s.Submit(a, DateTime.UtcNow));
    }

    [Fact]
    public void Assignment_cannot_be_published_without_recipients()
        => Assert.Throws<DomainException>(() => new Assignment().Publish(DateTime.UtcNow));
}

public class AiApprovalTests
{
    [Fact]
    public void Draft_cannot_be_published_before_teacher_approval()
    {
        var d = new AiLessonDraft();
        Assert.Throws<DomainException>(() => d.MarkPublished(Guid.NewGuid(), DateTime.UtcNow));
        d.Approve(Guid.NewGuid(), DateTime.UtcNow);
        d.MarkPublished(Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal(AiDraftStatus.Published, d.Status);
    }

    [Fact]
    public void Editing_an_approved_draft_requires_fresh_approval_and_keeps_history()
    {
        var d = new AiLessonDraft();
        d.Approve(Guid.NewGuid(), DateTime.UtcNow);
        d.Edit("{}", Guid.NewGuid(), DateTime.UtcNow, "fix");
        Assert.Equal(AiDraftStatus.AwaitingTeacherReview, d.Status);
        Assert.Equal(2, d.CurrentVersion);
        Assert.Single(d.Versions);
    }

    [Fact]
    public void Model_output_without_homework_evidence_fails_validation()
    {
        var json = """{"recap":"r","learningOutcomes":[],"topicNotes":[],"definitions":[],"examples":[],"questionsAnswered":[],"unansweredQuestions":[],"homework":[{"description":"Do it","statedDeadline":null,"confidence":0.9,"evidence":{"startSeconds":1,"quote":""}}],"revisionSummary":"s","practiceQuestions":[],"flashcards":[],"nextClassAgenda":[],"timestamps":[]}""";
        Assert.False(AiJobRunner.TryParse(json, out _, out var error));
        Assert.Contains("evidence", error);
    }

    [Fact]
    public void Valid_model_output_wrapped_in_text_is_parsed()
    {
        var json = """Here you go: {"recap":"Quadratics","learningOutcomes":["a"],"topicNotes":[],"definitions":[],"examples":[],"questionsAnswered":[],"unansweredQuestions":[],"homework":[],"revisionSummary":"s","practiceQuestions":[],"flashcards":[],"nextClassAgenda":[],"timestamps":[]} thanks""";
        Assert.True(AiJobRunner.TryParse(json, out var content, out _));
        Assert.Equal("Quadratics", content.Recap);
    }

    [Fact]
    public void Prompt_redacts_participant_names()
    {
        var t = new ClassTranscript();
        t.Segments.Add(new TranscriptSegment { Sequence = 0, StartSeconds = 3, Speaker = "Aisha Khan", Text = "Aisha asked about roots" });
        var prompt = AiJobRunner.BuildPrompt("Maths", t, ["Aisha Khan (Demo Student)"]);
        Assert.DoesNotContain("Aisha", prompt);
        Assert.DoesNotContain("Khan", prompt);
        Assert.Contains("a student", prompt);
    }
}

public class TranscriptParserTests
{
    [Fact]
    public void Parses_webvtt_with_voice_tags()
    {
        var segs = TranscriptParser.Parse("WEBVTT\n\n00:00:01.000 --> 00:00:04.500\n<v Teacher>Hello class\n\n01:02.000 --> 01:05.000\nStudent: A question\n");
        Assert.Equal(2, segs.Count);
        Assert.Equal("Teacher", segs[0].Speaker);
        Assert.Equal(1, segs[0].Start);
        Assert.Equal(62, segs[1].Start);
        Assert.Equal("Student", segs[1].Speaker);
    }

    [Fact]
    public void Parses_srt_comma_timestamps()
    {
        var segs = TranscriptParser.Parse("1\n00:00:10,500 --> 00:00:12,000\nIntro\n\n2\n00:00:13,000 --> 00:00:15,000\nMore\n");
        Assert.Equal(10.5, segs[0].Start);
        Assert.Equal(2, segs.Count);
    }

    [Fact]
    public void Plain_text_keeps_paragraph_order()
        => Assert.Equal(3, TranscriptParser.Parse("Teacher: one\n\nStudent: two\nthree").Count);
}

public class QuietHoursTests
{
    [Fact]
    public void Quiet_hours_wrapping_midnight_are_respected_in_user_time_zone()
    {
        var prefs = new NotificationPreference { QuietHoursStart = new TimeOnly(21, 0), QuietHoursEnd = new TimeOnly(7, 0) };
        // 19:00 UTC = 23:00 Dubai → inside quiet hours, resume at 07:00 (8 hours later).
        Assert.True(NotificationDeliveryJob.InQuietHours(prefs, "Asia/Dubai", new DateTime(2026, 11, 2, 19, 0, 0, DateTimeKind.Utc), out var wait));
        Assert.Equal(TimeSpan.FromHours(8), wait);
        Assert.False(NotificationDeliveryJob.InQuietHours(prefs, "Asia/Dubai", new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc), out _));
    }
}

public class PrerecordedVideoUnlockTests
{
    private static readonly DateTime Start = new(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc);
    private static LearningSession Class() => new() { StartUtc = Start, EndUtc = Start.AddHours(1) };

    [Fact]
    public void Before_class_video_unlocks_the_chosen_time_before_the_class_starts()
    {
        var v = new PrerecordedVideo { UnlockRule = VideoUnlockRule.BeforeClass, UnlockOffsetMinutes = 120 };
        Assert.False(v.IsUnlocked(Class(), Start.AddMinutes(-121)));
        Assert.Equal(Start.AddHours(-2), v.NextUnlockAtUtc(Class(), Start.AddMinutes(-121)));
        Assert.True(v.IsUnlocked(Class(), Start.AddMinutes(-120)));
        Assert.Null(v.NextUnlockAtUtc(Class(), Start.AddMinutes(-120)));
    }

    [Fact]
    public void After_class_video_stays_locked_during_the_live_class()
    {
        var v = new PrerecordedVideo { UnlockRule = VideoUnlockRule.AfterClass, UnlockOffsetMinutes = 0 };
        Assert.False(v.IsUnlocked(Class(), Start.AddMinutes(30)));
        Assert.True(v.IsUnlocked(Class(), Start.AddHours(1)));
    }

    [Fact]
    public void Manual_override_beats_the_automatic_rule_both_ways()
    {
        var early = new PrerecordedVideo { UnlockRule = VideoUnlockRule.AfterClass, AccessOverride = VideoAccessOverride.Unlocked };
        Assert.True(early.IsUnlocked(Class(), Start.AddDays(-3)));
        var relocked = new PrerecordedVideo { UnlockRule = VideoUnlockRule.BeforeClass, AccessOverride = VideoAccessOverride.Locked };
        Assert.False(relocked.IsUnlocked(Class(), Start.AddDays(3)));
        Assert.Null(relocked.NextUnlockAtUtc(Class(), Start.AddDays(-3)));
    }

    [Fact]
    public void Video_already_unlocked_stays_unlocked_when_the_class_moves_later()
    {
        var v = new PrerecordedVideo { UnlockRule = VideoUnlockRule.BeforeClass, UnlockOffsetMinutes = 60, UnlockedAtUtc = Start.AddMinutes(-30) };
        var moved = new LearningSession { StartUtc = Start.AddDays(7), EndUtc = Start.AddDays(7).AddHours(1) };
        Assert.True(v.IsUnlocked(moved, Start));
    }

    [Fact]
    public void Cancelled_class_never_auto_unlocks_an_after_class_video()
    {
        var cancelled = Class();
        cancelled.CancelledAtUtc = Start.AddDays(-1);
        var v = new PrerecordedVideo { UnlockRule = VideoUnlockRule.AfterClass };
        Assert.False(v.IsUnlocked(cancelled, Start.AddDays(2)));
        Assert.Null(v.NextUnlockAtUtc(cancelled, Start.AddDays(-2)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(PrerecordedVideo.MaxOffsetMinutes + 1)]
    public void Unlock_offset_outside_zero_to_fourteen_days_is_rejected(int minutes)
        => Assert.Throws<DomainException>(() => PrerecordedVideo.ValidateOffset(minutes));
}

public class SyllabusChapterStatusTests
{
    [Theory]
    [InlineData(new SessionStatus[0], "notScheduled")]
    [InlineData(new[] { SessionStatus.Cancelled }, "notScheduled")]
    [InlineData(new[] { SessionStatus.Completed, SessionStatus.Completed }, "completed")]
    [InlineData(new[] { SessionStatus.Completed, SessionStatus.Cancelled }, "completed")]
    [InlineData(new[] { SessionStatus.Completed, SessionStatus.Scheduled }, "inProgress")]
    [InlineData(new[] { SessionStatus.Completed, SessionStatus.Live }, "inProgress")]
    [InlineData(new[] { SessionStatus.Scheduled, SessionStatus.Scheduled }, "upcoming")]
    [InlineData(new[] { SessionStatus.Live, SessionStatus.Cancelled }, "upcoming")]
    public void Chapter_status_follows_its_non_cancelled_classes(SessionStatus[] statuses, string expected)
    {
        Assert.Equal(expected, StudentSync.Application.Academics.SyllabusChapterStatus.From(statuses));
        // The count-based overload used by the subject list agrees with the per-class rule.
        var open = statuses.Count(s => s is SessionStatus.Scheduled or SessionStatus.Live);
        var completed = statuses.Count(s => s == SessionStatus.Completed);
        Assert.Equal(expected, StudentSync.Application.Academics.SyllabusChapterStatus.From(open, completed));
    }
}

public class StudentCurriculumTests
{
    private static readonly Guid Student = Guid.NewGuid(), Other = Guid.NewGuid();
    private static readonly Guid Class10 = Guid.NewGuid(), Class11 = Guid.NewGuid();

    private static Subject S(Guid course, string code, SubjectKind kind, string? group = null,
        CatalogStatus status = CatalogStatus.Active)
    {
        var s = new Subject { Id = Guid.NewGuid(), CourseId = course, Code = code, Name = code, Status = status };
        s.SetKind(kind, group);
        return s;
    }

    [Fact]
    public void Student_takes_core_subjects_of_enrolled_courses_plus_chosen_languages_and_assigned_extras_only()
    {
        var core = S(Class10, "C10-ENG", SubjectKind.Core);
        var archivedCore = S(Class10, "C10-OLD", SubjectKind.Core, status: CatalogStatus.Archived);
        var hindi = S(Class10, "C10-L2-HIN", SubjectKind.LanguageOption, "Second language");
        var french = S(Class10, "C10-L2-FRE", SubjectKind.LanguageOption, "Second language");
        var physicsCoaching = S(Class10, "PHYS10", SubjectKind.Additional);
        var chemistryCoaching = S(Class10, "CHEM10", SubjectKind.Additional);
        var class11Physics = S(Class11, "C11S-PHY", SubjectKind.Core);
        var class11Hindi = S(Class11, "C11S-LO-HIN", SubjectKind.LanguageOption, "Language (optional)");
        var subjects = new[] { core, archivedCore, hindi, french, physicsCoaching, chemistryCoaching, class11Physics, class11Hindi }.AsQueryable();
        var enrolments = new[]
        {
            new CourseEnrolment { StudentId = Student, CourseId = Class10 },
            new CourseEnrolment { StudentId = Student, CourseId = Class11, Status = EnrolmentStatus.Withdrawn },
            new CourseEnrolment { StudentId = Other, CourseId = Class11 },
        }.AsQueryable();
        var assigned = new[]
        {
            new StudentSubject { StudentId = Student, SubjectId = hindi.Id },
            new StudentSubject { StudentId = Student, SubjectId = physicsCoaching.Id },
            new StudentSubject { StudentId = Other, SubjectId = chemistryCoaching.Id },
            new StudentSubject { StudentId = Student, SubjectId = class11Hindi.Id },
        }.AsQueryable();

        var mine = StudentCurriculum.SubjectsOf(Student, subjects, enrolments, assigned).Select(s => s.Code).Order().ToList();
        // Not: archived core, the unchosen language option, someone else's extra, core subjects of a withdrawn course.
        Assert.Equal(["C10-ENG", "C10-L2-HIN", "C11S-LO-HIN", "PHYS10"], mine);
        // The other student is in Class 11 only: its core subject plus their own extra.
        Assert.Equal(["C11S-PHY", "CHEM10"], StudentCurriculum.SubjectsOf(Other, subjects, enrolments, assigned).Select(s => s.Code).Order().ToList());
    }

    [Theory]
    [InlineData(SubjectKind.Core, true, "core")]
    [InlineData(SubjectKind.LanguageOption, true, "language")]
    [InlineData(SubjectKind.Additional, true, "additional")]
    // Given from a course the student is not enrolled in: always an additional subject for them.
    [InlineData(SubjectKind.Core, false, "additional")]
    [InlineData(SubjectKind.LanguageOption, false, "additional")]
    public void Kind_depends_on_the_catalogue_kind_and_enrolment(SubjectKind kind, bool enrolled, string expected)
        => Assert.Equal(expected, StudentCurriculum.KindFor(kind, enrolled));

    [Fact]
    public void Language_options_need_a_choice_group_and_core_subjects_never_keep_one()
    {
        var s = new Subject();
        var ex = Assert.Throws<DomainException>(() => s.SetKind(SubjectKind.LanguageOption, "  "));
        Assert.Equal("choice_group_required", ex.Code);
        s.SetKind(SubjectKind.LanguageOption, "  Second language ");
        Assert.Equal("Second language", s.ChoiceGroup);
        s.SetKind(SubjectKind.Core, "Second language");
        Assert.Null(s.ChoiceGroup);
        s.SetKind(SubjectKind.Additional, null);
        Assert.Null(s.ChoiceGroup);
        Assert.Throws<DomainException>(() => s.SetKind((SubjectKind)42, null));
    }
}
