using StudentSync.Domain.Common;
using StudentSync.Domain.People;

namespace StudentSync.Domain.Doubts;

public enum DoubtStatus { New, Assigned, InProgress, Answered, PendingConfirmation, Resolved, Reopened }
public enum DoubtPriority { Low, Normal, High, Urgent }

public class Doubt : TenantEntity
{
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid? SubjectId { get; set; }
    public Guid? ChapterId { get; set; }
    /// <summary>The session the question arose in (optional).</summary>
    public Guid? SessionId { get; set; }
    public Guid? RecordingId { get; set; }
    public int? RecordingTimestampSeconds { get; set; }
    public Guid? AssignedTeacherId { get; set; }
    public Teacher? AssignedTeacher { get; set; }
    public DoubtStatus Status { get; set; } = DoubtStatus.New;
    public DoubtPriority Priority { get; set; } = DoubtPriority.Normal;
    /// <summary>Private doubts are visible only to the student, their assigned teacher and admins (never to classmates).</summary>
    public bool IsPrivate { get; set; } = true;
    /// <summary>Upcoming session where this doubt will be discussed (set manually or by the follow-up job).</summary>
    public Guid? FollowUpSessionId { get; set; }
    public DateTime? DiscussedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public DateTime LastActivityAtUtc { get; set; }
    public List<DoubtMessage> Messages { get; set; } = [];

    public static readonly DoubtStatus[] Unresolved =
        [DoubtStatus.New, DoubtStatus.Assigned, DoubtStatus.InProgress, DoubtStatus.Answered, DoubtStatus.PendingConfirmation, DoubtStatus.Reopened];

    private static readonly Dictionary<DoubtStatus, DoubtStatus[]> Allowed = new()
    {
        [DoubtStatus.New] = [DoubtStatus.Assigned, DoubtStatus.InProgress, DoubtStatus.Answered, DoubtStatus.PendingConfirmation],
        [DoubtStatus.Assigned] = [DoubtStatus.InProgress, DoubtStatus.Answered, DoubtStatus.PendingConfirmation],
        [DoubtStatus.InProgress] = [DoubtStatus.Answered, DoubtStatus.PendingConfirmation],
        [DoubtStatus.Answered] = [DoubtStatus.PendingConfirmation, DoubtStatus.Resolved, DoubtStatus.Reopened, DoubtStatus.InProgress],
        [DoubtStatus.PendingConfirmation] = [DoubtStatus.Resolved, DoubtStatus.Reopened],
        [DoubtStatus.Resolved] = [DoubtStatus.Reopened],
        [DoubtStatus.Reopened] = [DoubtStatus.Assigned, DoubtStatus.InProgress, DoubtStatus.Answered, DoubtStatus.PendingConfirmation],
    };

    public static bool CanTransition(DoubtStatus from, DoubtStatus to) => Allowed[from].Contains(to);

    public DoubtStatusChange TransitionTo(DoubtStatus next, Guid userId, DateTime nowUtc, string? note = null)
    {
        if (next == Status) throw new DomainException($"The doubt is already {Status}.", "invalid_doubt_transition");
        if (!CanTransition(Status, next))
            throw new DomainException($"A doubt cannot move from {Status} to {next}.", "invalid_doubt_transition");
        var change = new DoubtStatusChange
        {
            TenantId = TenantId,
            DoubtId = Id,
            FromStatus = Status,
            ToStatus = next,
            ChangedByUserId = userId,
            ChangedAtUtc = nowUtc,
            Note = note,
        };
        Status = next;
        LastActivityAtUtc = nowUtc;
        if (next == DoubtStatus.Resolved) ResolvedAtUtc = nowUtc;
        if (next == DoubtStatus.Reopened) ResolvedAtUtc = null;
        if (next == DoubtStatus.PendingConfirmation) DiscussedAtUtc ??= nowUtc;
        return change;
    }

    public bool IsUnresolved => Status != DoubtStatus.Resolved;
}

public class DoubtMessage : TenantEntity
{
    public Guid DoubtId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Body { get; set; } = "";
    public bool IsTeacherReply { get; set; }
    public List<DoubtAttachment> Attachments { get; set; } = [];
}

public class DoubtAttachment : TenantEntity
{
    public Guid DoubtId { get; set; }
    public Guid? MessageId { get; set; }
    public Guid FileId { get; set; }
}

public class DoubtStatusChange : TenantEntity
{
    public Guid DoubtId { get; set; }
    public DoubtStatus FromStatus { get; set; }
    public DoubtStatus ToStatus { get; set; }
    public Guid ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public string? Note { get; set; }
}
