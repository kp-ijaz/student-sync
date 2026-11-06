using StudentSync.Domain.Common;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Domain.Content;

public enum FilePurpose { Material, NoteImage, Recording, Transcript, AssignmentAttachment, Submission, DoubtAttachment, MessageAttachment, Avatar, PrerecordedVideo, QuestionImage }

/// <summary>Metadata for a file held in private object storage. Bytes never live in the database.
/// Access is always decided by the owning record (session, assignment, doubt…), never by the file id alone.</summary>
public class StoredFile : TenantEntity
{
    public string StorageKey { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public FilePurpose Purpose { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}

public enum NoteVisibility { TeacherOnly, Participants }
public enum PublicationStatus { Draft, Published }
public enum ContentSource { Manual, AiApproved }

/// <summary>Lesson notes in Markdown (headings, tables, images, LaTeX-style formulas). Rendered sanitised.</summary>
public class SessionNote : TenantEntity
{
    public Guid SessionId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Title { get; set; } = "";
    public string BodyMarkdown { get; set; } = "";
    public NoteVisibility Visibility { get; set; } = NoteVisibility.Participants;
    public PublicationStatus Status { get; set; } = PublicationStatus.Draft;
    public DateTime? PublishedAtUtc { get; set; }
    public ContentSource Source { get; set; } = ContentSource.Manual;
    /// <summary>Set when this note was published from an approved AI draft.</summary>
    public Guid? AiDraftId { get; set; }
    public bool AvailableOffline { get; set; }
}

public enum MaterialKind { File, Link }

/// <summary>A learning resource attached to a session and/or a subject/chapter.</summary>
public class LearningMaterial : TenantEntity
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public MaterialKind Kind { get; set; }
    public Guid? FileId { get; set; }
    public StoredFile? File { get; set; }
    public string? Url { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? SubjectId { get; set; }
    public Guid? ChapterId { get; set; }
    public Guid? BatchId { get; set; }
    public bool AvailableOffline { get; set; }
    public Guid CreatedByTeacherOrAdminId { get; set; }
}

public enum RecordingStatus { Pending, Available, Failed, Deleted }
public enum RecordingSource { Upload, MeetingProvider }

public class ClassRecording : TenantEntity
{
    public Guid SessionId { get; set; }
    public string Title { get; set; } = "";
    public RecordingSource Source { get; set; }
    public Guid? FileId { get; set; }
    public StoredFile? File { get; set; }
    public int? DurationSeconds { get; set; }
    public RecordingStatus Status { get; set; } = RecordingStatus.Pending;
    public DateTime RetainUntilUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid UploadedByUserId { get; set; }
}

public enum VideoUnlockRule { BeforeClass, AfterClass }
public enum VideoAccessOverride { Automatic, Unlocked, Locked }

/// <summary>
/// A pre-recorded lesson attached to a scheduled class. Students can watch it only once it unlocks:
/// automatically before the class starts (preparation) or after it ends (revision), unless a teacher or
/// admin has locked or unlocked it by hand.
/// </summary>
public class PrerecordedVideo : TenantEntity
{
    public const int MaxOffsetMinutes = 14 * 24 * 60;

    public Guid SessionId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public Guid FileId { get; set; }
    public StoredFile? File { get; set; }
    public int? DurationSeconds { get; set; }
    public VideoUnlockRule UnlockRule { get; set; }
    /// <summary>Minutes before the class starts (BeforeClass) or after it ends (AfterClass).</summary>
    public int UnlockOffsetMinutes { get; set; }
    public VideoAccessOverride AccessOverride { get; set; }
    /// <summary>Set once the automatic rule has been met, so moving the class later never re-locks a video students already had.</summary>
    public DateTime? UnlockedAtUtc { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public DateTime AutoUnlockAtUtc(LearningSession session) => UnlockRule == VideoUnlockRule.BeforeClass
        ? session.StartUtc.AddMinutes(-UnlockOffsetMinutes)
        : session.EndUtc.AddMinutes(UnlockOffsetMinutes);

    public bool IsUnlocked(LearningSession session, DateTime nowUtc) => AccessOverride switch
    {
        VideoAccessOverride.Unlocked => true,
        VideoAccessOverride.Locked => false,
        _ => UnlockedAtUtc is not null || (nowUtc >= AutoUnlockAtUtc(session)
            // A cancelled class only counts if the video had already unlocked before the cancellation.
            && (session.CancelledAtUtc is not { } cancelled || cancelled >= AutoUnlockAtUtc(session))),
    };

    /// <summary>When the video will unlock on its own, or null if it is unlocked already or will not unlock automatically.</summary>
    public DateTime? NextUnlockAtUtc(LearningSession session, DateTime nowUtc)
    {
        if (AccessOverride != VideoAccessOverride.Automatic || IsUnlocked(session, nowUtc) || session.CancelledAtUtc is not null) return null;
        return AutoUnlockAtUtc(session);
    }

    public static void ValidateOffset(int minutes)
    {
        if (minutes is < 0 or > MaxOffsetMinutes)
            throw new DomainException("The unlock time must be between 0 minutes and 14 days from the class.", "invalid_unlock_offset");
    }
}

/// <summary>Per-student playback progress, used for resume only — never for ranking or evaluation.</summary>
public class RecordingProgress : TenantEntity
{
    public Guid RecordingId { get; set; }
    public Guid StudentId { get; set; }
    public int PositionSeconds { get; set; }
    public bool Completed { get; set; }
    public DateTime LastWatchedAtUtc { get; set; }
}

public enum ConsentType { Recording, Transcription, AiProcessing }

/// <summary>Consent captured for a session participant. A guardian may consent on behalf of a linked child.</summary>
public class ConsentRecord : TenantEntity
{
    public Guid SessionId { get; set; }
    public Guid StudentId { get; set; }
    public ConsentType Type { get; set; }
    public bool Granted { get; set; }
    public Guid GivenByUserId { get; set; }
    public string GivenByRole { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; }
    public string? Method { get; set; }
}

public enum TranscriptSource { Upload, Provider, Manual }

/// <summary>A transcript. Originals are immutable; teacher corrections are stored as a new row pointing to the original.</summary>
public class ClassTranscript : TenantEntity
{
    public Guid SessionId { get; set; }
    public Guid? RecordingId { get; set; }
    public TranscriptSource Source { get; set; }
    public string Language { get; set; } = "en";
    public bool IsOriginal { get; set; } = true;
    public Guid? CorrectsTranscriptId { get; set; }
    public string FullText { get; set; } = "";
    public List<TranscriptSegment> Segments { get; set; } = [];
}

public class TranscriptSegment : Entity
{
    public Guid TranscriptId { get; set; }
    public int Sequence { get; set; }
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public string? Speaker { get; set; }
    public string Text { get; set; } = "";
    public double? Confidence { get; set; }
}
