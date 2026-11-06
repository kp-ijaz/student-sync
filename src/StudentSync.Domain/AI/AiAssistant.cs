using StudentSync.Domain.Common;

namespace StudentSync.Domain.AI;

/// <summary>Lesson pipeline status, as shown to teachers for a session.</summary>
public enum AiPipelineStatus
{
    RecordingPending, TranscriptionPending, TranscriptReady, AIProcessing, DraftGenerated, AwaitingTeacherReview, Approved, Published, ProcessingFailed
}

public enum AiJobKind { Transcription, LessonDraft }
public enum AiJobStatus { Queued, Running, Succeeded, Failed, Blocked }

/// <summary>Asynchronous AI work item. The idempotency key prevents duplicate jobs for the same input.</summary>
public class AiProcessingJob : TenantEntity
{
    public Guid SessionId { get; set; }
    public AiJobKind Kind { get; set; }
    public AiJobStatus Status { get; set; } = AiJobStatus.Queued;
    public string IdempotencyKey { get; set; } = "";
    public Guid? RecordingId { get; set; }
    public Guid? TranscriptId { get; set; }
    public Guid? DraftId { get; set; }
    public int Attempts { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid RequestedByUserId { get; set; }
}

public enum AiDraftStatus { AwaitingTeacherReview, Approved, Published, Rejected }

/// <summary>AI-generated lesson draft. Never visible to students until a teacher approves and publishes it.</summary>
public class AiLessonDraft : TenantEntity
{
    public Guid SessionId { get; set; }
    public Guid TranscriptId { get; set; }
    public Guid? JobId { get; set; }
    public AiDraftStatus Status { get; set; } = AiDraftStatus.AwaitingTeacherReview;
    public int CurrentVersion { get; set; } = 1;
    public string ContentJson { get; set; } = "{}";
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public Guid? PublishedNoteId { get; set; }
    public string? RejectionReason { get; set; }
    /// <summary>True for clearly-labelled demo drafts created by the development seeder (not real model output).</summary>
    public bool IsDemo { get; set; }
    public List<AiLessonDraftVersion> Versions { get; set; } = [];

    public void Edit(string contentJson, Guid userId, DateTime nowUtc, string? changeNote)
    {
        if (Status is AiDraftStatus.Published or AiDraftStatus.Rejected)
            throw new DomainException("Published or rejected drafts cannot be edited.", "invalid_draft_state");
        CurrentVersion++;
        ContentJson = contentJson;
        // Any edit after approval requires a fresh approval.
        Status = AiDraftStatus.AwaitingTeacherReview;
        ApprovedAtUtc = null;
        Versions.Add(new AiLessonDraftVersion
        {
            TenantId = TenantId,
            DraftId = Id,
            VersionNumber = CurrentVersion,
            ContentJson = contentJson,
            EditedByUserId = userId,
            CreatedAtUtc = nowUtc,
            ChangeNote = changeNote,
        });
    }

    public void Approve(Guid userId, DateTime nowUtc)
    {
        if (Status != AiDraftStatus.AwaitingTeacherReview) throw new DomainException("Only drafts awaiting review can be approved.", "invalid_draft_state");
        Status = AiDraftStatus.Approved;
        ReviewedByUserId = userId;
        ApprovedAtUtc = nowUtc;
    }

    public void Reject(Guid userId, string reason)
    {
        if (Status is AiDraftStatus.Published) throw new DomainException("A published draft cannot be rejected.", "invalid_draft_state");
        Status = AiDraftStatus.Rejected;
        ReviewedByUserId = userId;
        RejectionReason = reason;
    }

    public void MarkPublished(Guid noteId, DateTime nowUtc)
    {
        if (Status != AiDraftStatus.Approved) throw new DomainException("A draft must be approved by a teacher before it is published.", "approval_required");
        Status = AiDraftStatus.Published;
        PublishedAtUtc = nowUtc;
        PublishedNoteId = noteId;
    }
}

public class AiLessonDraftVersion : TenantEntity
{
    public Guid DraftId { get; set; }
    public int VersionNumber { get; set; }
    public string ContentJson { get; set; } = "{}";
    public Guid? EditedByUserId { get; set; }
    public string? ChangeNote { get; set; }
}

public class AiUsageRecord : TenantEntity
{
    public Guid? JobId { get; set; }
    public string Feature { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
}

// ---- Structured draft content. Validated before it is saved. ----

public sealed record SourceRef(double? StartSeconds, string? Quote);
public sealed record TopicNote(string Topic, string Notes, List<SourceRef>? Sources);
public sealed record DefinitionItem(string Term, string Definition, string? Formula, SourceRef? Source);
public sealed record WorkedExample(string Title, List<string> Steps, SourceRef? Source);
public sealed record QaItem(string Question, string Answer, string? AskedBy, SourceRef? Source);
public sealed record HomeworkItem(string Description, string? StatedDeadline, double Confidence, SourceRef Evidence);
public sealed record PracticeQuestion(string Type, string Question, List<string>? Options, string Answer, string Explanation);
public sealed record Flashcard(string Front, string Back);
public sealed record TimestampLink(double StartSeconds, string Label);

/// <summary>
/// The structured lesson draft. Everything except PracticeQuestions, Flashcards and NextClassAgenda must be
/// grounded in the transcript; those three are AI recommendations and are labelled as such in every UI.
/// </summary>
public sealed record LessonDraftContent(
    string Recap,
    List<string> LearningOutcomes,
    List<TopicNote> TopicNotes,
    List<DefinitionItem> Definitions,
    List<WorkedExample> Examples,
    List<QaItem> QuestionsAnswered,
    List<string> UnansweredQuestions,
    List<HomeworkItem> Homework,
    string RevisionSummary,
    List<PracticeQuestion> PracticeQuestions,
    List<Flashcard> Flashcards,
    List<string> NextClassAgenda,
    List<TimestampLink> Timestamps)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Recap)) errors.Add("Recap is required.");
        if (Recap?.Length > 4000) errors.Add("Recap is too long.");
        if (LearningOutcomes is null || TopicNotes is null || Definitions is null || Examples is null || QuestionsAnswered is null
            || UnansweredQuestions is null || Homework is null || PracticeQuestions is null || Flashcards is null
            || NextClassAgenda is null || Timestamps is null)
        {
            errors.Add("All sections must be present (use empty lists when nothing applies).");
            return errors;
        }
        foreach (var h in Homework)
        {
            if (h.Confidence is < 0 or > 1) errors.Add("Homework confidence must be between 0 and 1.");
            if (string.IsNullOrWhiteSpace(h.Evidence?.Quote)) errors.Add($"Homework '{h.Description}' has no source evidence.");
        }
        if (TopicNotes.Count > 50 || PracticeQuestions.Count > 50 || Flashcards.Count > 100) errors.Add("Too many items in a section.");
        return errors;
    }
}
