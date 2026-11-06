using StudentSync.Domain.AI;
using StudentSync.Domain.Assessments;
using StudentSync.Domain.Assignments;
using StudentSync.Domain.Content;
using StudentSync.Domain.Doubts;

namespace StudentSync.Contracts;

// ---------- Files ----------

public sealed record FileDto(Guid Id, string FileName, string ContentType, long SizeBytes);
public sealed record FileLinkDto(string Url, DateTime ExpiresAtUtc);

// ---------- Notes & materials ----------

public sealed record NoteDto(
    Guid Id, Guid SessionId, string Title, string BodyMarkdown, NoteVisibility Visibility, PublicationStatus Status,
    ContentSource Source, DateTime? PublishedAtUtc, DateTime UpdatedAtUtc, string AuthorName, bool AvailableOffline, string RowVersion);
public sealed record SaveNoteRequest(string Title, string BodyMarkdown, NoteVisibility Visibility, bool AvailableOffline, string? RowVersion);

public sealed record MaterialDto(Guid Id, string Title, string? Description, MaterialKind Kind, string? Url, FileDto? File, Guid? SessionId, string? SessionTitle, Guid? SubjectId, string? SubjectName, bool AvailableOffline, DateTime CreatedAtUtc);
public sealed record CreateLinkMaterialRequest(string Title, string? Description, string Url, Guid? SessionId, Guid? SubjectId, Guid? ChapterId, Guid? BatchId, bool AvailableOffline);

public sealed record LibraryQuery : PageQuery
{
    public Guid? SubjectId { get; init; }
    public Guid? SessionId { get; init; }
}

// ---------- Recordings, consent, transcripts ----------

public sealed record RecordingDto(Guid Id, Guid SessionId, string SessionTitle, string Title, RecordingStatus Status, int? DurationSeconds, DateTime CreatedAtUtc, DateTime RetainUntilUtc, int? ResumePositionSeconds, bool Completed, Guid? FileId);
public sealed record RecordingProgressRequest(int PositionSeconds, bool Completed);

// ---------- Pre-recorded videos ----------

/// <summary>FileId is only present when the caller may play the video (unlocked, or the caller manages the class).</summary>
public sealed record PrerecordedVideoDto(
    Guid Id, Guid SessionId, string SessionTitle, DateTime SessionStartUtc, DateTime SessionEndUtc, bool SessionCancelled,
    string Title, string? Description, int? DurationSeconds, VideoUnlockRule UnlockRule, int UnlockOffsetMinutes, VideoAccessOverride AccessOverride,
    bool IsUnlocked, DateTime? UnlocksAtUtc, Guid? FileId, bool CanManage, DateTime CreatedAtUtc, string RowVersion);
public sealed record UpdatePrerecordedVideoRequest(string Title, string? Description, VideoUnlockRule UnlockRule, int UnlockOffsetMinutes, string? RowVersion);
public sealed record SetVideoAccessRequest(VideoAccessOverride AccessOverride);
public sealed record VideoQuery : PageQuery
{
    public Guid? SessionId { get; init; }
}

// ---------- My subjects & syllabus (student-facing learning path) ----------

public sealed record SubjectTeacherDto(Guid Id, string Name, string? PhotoUrl);

/// <summary>
/// A subject of the caller. Students: their curriculum (core subjects of their courses, chosen languages, assigned additional
/// subjects) even before any class exists. Teachers: subjects they are assigned to or teach. Admins: subjects with classes.
/// Counts, videos and the next class come only from classes the caller can see. Class counts exclude cancelled classes; chapter
/// counts cover the subject's real chapters (not the "Other classes" group). Video counts use the same lock rule as GET /videos.
/// Kind is "core", "language" or "additional"; Group is the subject's choice group (e.g. "Second language"), if any.
/// </summary>
public sealed record MySubjectDto(
    Guid SubjectId, string Name, string Code, string CourseName, IReadOnlyList<SubjectTeacherDto> Teachers,
    int ChapterCount, int CompletedChapterCount, int ClassCount, int CompletedClassCount, int VideoCount, int UnlockedVideoCount,
    SessionListItemDto? NextSession, string Kind = "core", string? Group = null);

public sealed record SubjectSyllabusDto(
    Guid SubjectId, string Name, string Code, string CourseName, string? Description, IReadOnlyList<SubjectTeacherDto> Teachers,
    int ChapterCount, int CompletedChapterCount, int ClassCount, int CompletedClassCount, int VideoCount, int UnlockedVideoCount,
    SessionListItemDto? NextSession, IReadOnlyList<SyllabusChapterDto> Chapters, string Kind = "core", string? Group = null);

/// <summary>
/// One chapter of the syllabus. Status is "completed", "inProgress", "upcoming" or "notScheduled". ChapterId is null for the
/// trailing "Other classes" group (sessions of the subject without a chapter).
/// </summary>
public sealed record SyllabusChapterDto(
    Guid? ChapterId, string Title, int SortOrder, string Status,
    IReadOnlyList<SessionListItemDto> Sessions, IReadOnlyList<PrerecordedVideoDto> Videos, IReadOnlyList<RecordingDto> Recordings);

public sealed record ConsentStatusDto(Guid SessionId, bool RecordingEnabledByInstitution, bool TranscriptionEnabledByInstitution, bool AiEnabledByInstitution, IReadOnlyList<StudentConsentDto> Students, bool AllConsentedToRecording, bool AllConsentedToAi);
public sealed record StudentConsentDto(Guid StudentId, string StudentName, bool? Recording, bool? Transcription, bool? AiProcessing);
public sealed record GiveConsentRequest(Guid StudentId, ConsentType Type, bool Granted);

public sealed record TranscriptDto(Guid Id, Guid SessionId, string Source, string Language, bool IsOriginal, Guid? CorrectsTranscriptId, DateTime CreatedAtUtc, IReadOnlyList<TranscriptSegmentDto> Segments, int LowConfidenceSegments);
public sealed record TranscriptSegmentDto(int Sequence, double StartSeconds, double EndSeconds, string? Speaker, string Text, double? Confidence);
public sealed record CorrectTranscriptRequest(List<TranscriptSegmentDto> Segments);

// ---------- AI ----------

public sealed record AiSessionStatusDto(
    Guid SessionId, AiPipelineStatus Status, string StatusDetail, bool LanguageModelConfigured, bool TranscriptionConfigured,
    bool InstitutionAiEnabled, bool AllParticipantsConsented, Guid? LatestTranscriptId, IReadOnlyList<AiJobDto> Jobs, IReadOnlyList<AiDraftSummaryDto> Drafts);
public sealed record AiJobDto(Guid Id, AiJobKind Kind, AiJobStatus Status, int Attempts, string? FailureReason, string? Provider, string? Model, DateTime CreatedAtUtc, DateTime? CompletedAtUtc);
public sealed record AiDraftSummaryDto(Guid Id, AiDraftStatus Status, int CurrentVersion, DateTime CreatedAtUtc, DateTime? ApprovedAtUtc, DateTime? PublishedAtUtc, bool IsDemo);
public sealed record AiDraftDto(Guid Id, Guid SessionId, string SessionTitle, AiDraftStatus Status, int CurrentVersion, LessonDraftContent Content, string? Provider, string? Model, bool IsDemo, string? RejectionReason, DateTime CreatedAtUtc, IReadOnlyList<AiDraftVersionDto> Versions, string RowVersion);
public sealed record AiDraftVersionDto(int VersionNumber, DateTime CreatedAtUtc, string? EditedBy, string? ChangeNote);
public sealed record EditAiDraftRequest(LessonDraftContent Content, string? ChangeNote, string RowVersion);
public sealed record RejectAiDraftRequest(string Reason);
public sealed record GenerateDraftRequest(Guid? TranscriptId);

public sealed record AssistantAskRequest(string Question, Guid? SessionId);
public sealed record AssistantAnswerDto(bool ProviderConfigured, string? Answer, IReadOnlyList<AssistantSourceDto> Sources, string Disclaimer);
public sealed record AssistantSourceDto(Guid SessionId, string SessionTitle, Guid NoteId, string NoteTitle, string Excerpt);

public sealed record AiUsageSummaryDto(int JobsThisMonth, int MonthlyLimit, int InputTokens, int OutputTokens, decimal EstimatedCostUsd, bool LanguageModelConfigured, string? Provider, string? Model);

// ---------- Assignments ----------

public sealed record AssignmentListItemDto(Guid Id, string Title, AssignmentStatus Status, DateTime DueAtUtc, decimal MaxMarks, string TeacherName, string? SessionTitle, int RecipientCount, int SubmittedCount, int GradedCount, SubmissionStatus? MySubmissionStatus, decimal? MyMarks);
public sealed record AssignmentDetailDto(
    Guid Id, string Title, string InstructionsMarkdown, AssignmentStatus Status, DateTime DueAtUtc, decimal MaxMarks,
    bool AllowLateSubmission, bool AllowResubmission, Guid TeacherId, string TeacherName, Guid? SessionId, string? SessionTitle,
    Guid? BatchId, Guid? SubjectId, IReadOnlyList<FileDto> Attachments, IReadOnlyList<LookupItem> Recipients, bool CanManage, string RowVersion);
public sealed record SaveAssignmentRequest(
    string Title, string InstructionsMarkdown, DateTime DueAtUtc, decimal MaxMarks, bool AllowLateSubmission, bool AllowResubmission,
    Guid? SessionId, Guid? BatchId, Guid? SubjectId, List<Guid>? StudentIds, string? RowVersion);
public sealed record AssignmentQuery : PageQuery
{
    public AssignmentStatus? Status { get; init; }
    public Guid? SessionId { get; init; }
    public Guid? StudentId { get; init; }
}

public sealed record SubmissionDto(Guid Id, Guid AssignmentId, string AssignmentTitle, Guid StudentId, string StudentName, SubmissionStatus Status, string? TextAnswer, DateTime? SubmittedAtUtc, bool IsLate, int AttemptNumber, decimal? Marks, decimal MaxMarks, string? Feedback, DateTime? GradedAtUtc, IReadOnlyList<FileDto> Files, string RowVersion);
public sealed record SaveSubmissionRequest(string? TextAnswer, List<Guid>? FileIds, bool Submit, string? RowVersion);
public sealed record GradeSubmissionRequest(decimal Marks, string? Feedback, bool ReturnForCorrection, string RowVersion);

// ---------- Question bank & exams ----------

public sealed record QuestionDto(Guid Id, Guid SubjectId, string SubjectName, Guid? ChapterId, QuestionType Type, Difficulty Difficulty, string Stem, string? Explanation, decimal DefaultMarks, string? AcceptedAnswers, decimal? NumericAnswer, decimal NumericTolerance, IReadOnlyList<QuestionOptionDto> Options,
    Guid? ImageFileId = null, string? ChapterTitle = null);
public sealed record QuestionOptionDto(Guid Id, string Text, bool IsCorrect, string? MatchText, int SortOrder);
public sealed record SaveQuestionRequest(Guid SubjectId, Guid? ChapterId, QuestionType Type, Difficulty Difficulty, string Stem, string? Explanation, decimal DefaultMarks, string? AcceptedAnswers, decimal? NumericAnswer, decimal NumericTolerance, List<SaveQuestionOption> Options,
    Guid? ImageFileId = null);
public sealed record SaveQuestionOption(string Text, bool IsCorrect, string? MatchText);
public sealed record QuestionQuery : PageQuery
{
    public Guid? SubjectId { get; init; }
    public QuestionType? Type { get; init; }
    public Difficulty? Difficulty { get; init; }
}

public sealed record ExamListItemDto(Guid Id, string Title, ExamStatus Status, DateTime OpensAtUtc, DateTime ClosesAtUtc, int DurationMinutes, int QuestionCount, decimal TotalMarks, string SubjectName, bool ResultsReleased, int AttemptsUsed, int MaxAttempts, decimal? MyBestScore, bool HasInProgressAttempt, Guid? LatestAttemptId,
    ExamPurpose Purpose = ExamPurpose.Regular);
public sealed record ExamDetailDto(Guid Id, string Title, string? Instructions, ExamStatus Status, Guid SubjectId, Guid? SessionId, Guid? BatchId, DateTime OpensAtUtc, DateTime ClosesAtUtc, int DurationMinutes, int MaxAttempts, bool ShuffleQuestions, bool ResultsReleased, IReadOnlyList<ExamQuestionItemDto> Questions, IReadOnlyList<LookupItem> Recipients, decimal TotalMarks,
    ExamPurpose Purpose = ExamPurpose.Regular, Guid? GradeLevelId = null, Guid? CurriculumId = null, Guid? TeacherId = null);
public sealed record ExamQuestionItemDto(Guid ExamQuestionId, Guid QuestionId, QuestionType Type, string Stem, decimal Marks, int SortOrder,
    Guid? ImageFileId = null, IReadOnlyList<AttemptOptionDto>? Options = null, IReadOnlyList<string>? MatchChoices = null, string? SubjectName = null, string? ChapterTitle = null);
/// <summary>Purpose Diagnostic: GradeLevelId/CurriculumId describe the assessment; an admin must name the TeacherId who runs it when no session or batch is linked.</summary>
public sealed record SaveExamRequest(string Title, string? Instructions, Guid SubjectId, Guid? SessionId, Guid? BatchId, DateTime OpensAtUtc, DateTime ClosesAtUtc, int DurationMinutes, int MaxAttempts, bool ShuffleQuestions, List<ExamQuestionSelection> Questions, List<Guid>? StudentIds,
    ExamPurpose Purpose = ExamPurpose.Regular, Guid? GradeLevelId = null, Guid? CurriculumId = null, Guid? TeacherId = null);
public sealed record ExamQuestionSelection(Guid QuestionId, decimal? Marks);

/// <summary>What a student sees while taking an exam. Answer keys are never included.</summary>
public sealed record AttemptDto(Guid AttemptId, Guid ExamId, string ExamTitle, AttemptStatus Status, DateTime StartedAtUtc, DateTime DeadlineUtc, int AttemptNumber, IReadOnlyList<AttemptQuestionDto> Questions);
public sealed record AttemptQuestionDto(Guid ExamQuestionId, QuestionType Type, string Stem, decimal Marks, IReadOnlyList<AttemptOptionDto> Options, IReadOnlyList<string>? MatchChoices, ExamResponse? SavedResponse, Guid? ImageFileId = null);
public sealed record AttemptOptionDto(Guid Id, string Text);
public sealed record SaveAnswerRequest(Guid ExamQuestionId, ExamResponse Response);

public sealed record AttemptResultDto(Guid AttemptId, string ExamTitle, Guid StudentId, string StudentName, AttemptStatus Status, DateTime? SubmittedAtUtc, decimal? Score, decimal TotalMarks, bool RequiresManualGrading, bool ResultsReleased, IReadOnlyList<AnswerResultDto> Answers,
    ExamPurpose Purpose = ExamPurpose.Regular, AttemptEntryMode EntryMode = AttemptEntryMode.Digital, Guid? ExamId = null);
public sealed record AnswerResultDto(Guid ExamQuestionId, string Stem, QuestionType Type, ExamResponse? Response, bool? IsCorrect, decimal? AwardedMarks, decimal Marks, string? Feedback, string? Explanation, string? CorrectAnswer,
    Guid? ImageFileId = null, string? AnswerText = null);
public sealed record GradeAnswerRequest(Guid ExamQuestionId, decimal AwardedMarks, string? Feedback);
public sealed record ExamResultsDto(Guid ExamId, string Title, decimal TotalMarks, IReadOnlyList<AttemptSummaryDto> Attempts, decimal? Average, decimal? Highest, int PendingManualGrading);
public sealed record AttemptSummaryDto(Guid AttemptId, Guid StudentId, string StudentName, int AttemptNumber, AttemptStatus Status, DateTime? SubmittedAtUtc, decimal? Score, bool RequiresManualGrading,
    AttemptEntryMode EntryMode = AttemptEntryMode.Digital);

// ---------- Diagnostic assessments (addendum C): paper entry, breakdown and learning-need recommendations ----------

/// <summary>Staff enter a student's paper (OMR-style) answers against the key. One response per exam question; omitted questions count as unanswered.</summary>
public sealed record PaperAttemptRequest(Guid StudentId, List<SaveAnswerRequest> Answers);
public sealed record DiagnosticRowDto(Guid SubjectId, string SubjectName, Guid? ChapterId, string? ChapterTitle, int Questions, int Answered, int Correct, decimal Marks, decimal Awarded, decimal? Percent, bool Pending, bool NeedsAttention, bool AlreadyRecorded);
/// <summary>Rows per chapter (and per subject) plus the threshold used; Recommended lists the rows below the threshold that are not already open learning needs.</summary>
public sealed record AttemptBreakdownDto(Guid AttemptId, Guid ExamId, string ExamTitle, ExamPurpose Purpose, Guid StudentId, string StudentName, AttemptStatus Status, decimal? Score, decimal TotalMarks,
    decimal ThresholdPercent, IReadOnlyList<DiagnosticRowDto> BySubject, IReadOnlyList<DiagnosticRowDto> ByChapter, bool CanRecordNeeds);
public sealed record DiagnosticNeedSelection(Guid SubjectId, Guid? ChapterId);
/// <summary>Staff confirm which rows become learning needs (source Diagnostic). Rows already open are skipped, so replaying is safe.</summary>
public sealed record ApplyDiagnosticRequest(List<DiagnosticNeedSelection> Items, string? Note);
public sealed record ApplyDiagnosticResultDto(int Created, int Skipped);

// ---------- Doubts ----------

public sealed record DoubtListItemDto(Guid Id, string Title, DoubtStatus Status, DoubtPriority Priority, string StudentName, string? SubjectName, string? AssignedTeacherName, Guid? SessionId, Guid? FollowUpSessionId, DateTime? FollowUpSessionStartUtc, DateTime CreatedAtUtc, DateTime LastActivityAtUtc, int ReplyCount);
public sealed record DoubtDetailDto(
    Guid Id, string Title, string Description, DoubtStatus Status, DoubtPriority Priority, bool IsPrivate,
    Guid StudentId, string StudentName, Guid? SubjectId, string? SubjectName, Guid? ChapterId, Guid? SessionId, string? SessionTitle,
    Guid? RecordingId, int? RecordingTimestampSeconds, Guid? AssignedTeacherId, string? AssignedTeacherName,
    Guid? FollowUpSessionId, string? FollowUpSessionTitle, DateTime? FollowUpSessionStartUtc,
    IReadOnlyList<DoubtMessageDto> Messages, IReadOnlyList<DoubtHistoryDto> History, IReadOnlyList<FileDto> Attachments,
    bool CanAnswer, bool CanConfirm, string RowVersion);
public sealed record DoubtMessageDto(Guid Id, string AuthorName, bool IsTeacherReply, string Body, DateTime CreatedAtUtc, IReadOnlyList<FileDto> Attachments);
public sealed record DoubtHistoryDto(DateTime AtUtc, DoubtStatus From, DoubtStatus To, string ChangedBy, string? Note);
public sealed record CreateDoubtRequest(string Title, string Description, Guid? SubjectId, Guid? ChapterId, Guid? SessionId, Guid? RecordingId, int? RecordingTimestampSeconds, bool IsPrivate, List<Guid>? FileIds);
public sealed record ReplyDoubtRequest(string Body, List<Guid>? FileIds, bool MarkAnswered);
public sealed record UpdateDoubtRequest(Guid? AssignedTeacherId, DoubtPriority Priority, DoubtStatus? Status, Guid? FollowUpSessionId, string RowVersion);
public sealed record ConfirmDoubtRequest(bool Resolved, string? Comment);
public sealed record DoubtQuery : PageQuery
{
    public DoubtStatus? Status { get; init; }
    public bool? UnresolvedOnly { get; init; }
    public Guid? SubjectId { get; init; }
    public Guid? SessionId { get; init; }
    public bool? AssignedToMe { get; init; }
}
