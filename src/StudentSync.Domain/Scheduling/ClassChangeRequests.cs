using StudentSync.Domain.Common;

namespace StudentSync.Domain.Scheduling;

/// <summary>Postpone = "I can't make it, please move this class" (the teacher picks the new time); Reschedule = the student proposes one.</summary>
public enum ClassChangeKind { Postpone, Reschedule }
public enum ClassChangeStatus { Pending, Accepted, Declined, Withdrawn }

/// <summary>
/// A student's request to postpone or reschedule one of their upcoming classes (owner request 2026-11-17). It goes to the
/// class's teacher (admins can answer too). Accepting moves the class through the normal reschedule rules (clashes,
/// hours, history, notifications); nothing changes until then. At most one pending request per class and student.
/// </summary>
public class ClassChangeRequest : TenantEntity
{
    public Guid SessionId { get; set; }
    public Guid StudentId { get; set; }
    public Guid TeacherId { get; set; }
    public ClassChangeKind Kind { get; set; }
    public DateOnly? ProposedDate { get; set; }
    public TimeOnly? ProposedTime { get; set; }
    /// <summary>The time zone the proposed date and time are in (the class's own zone).</summary>
    public string? TimeZoneId { get; set; }
    public string Reason { get; set; } = "";
    public ClassChangeStatus Status { get; set; }
    public string? ResponseNote { get; set; }
    public Guid? RespondedByUserId { get; set; }
    public DateTime? RespondedAtUtc { get; set; }
    public DateTime? NewStartUtc { get; set; }
}
