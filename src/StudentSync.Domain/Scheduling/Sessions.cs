using StudentSync.Domain.Academics;
using StudentSync.Domain.Common;
using StudentSync.Domain.People;

namespace StudentSync.Domain.Scheduling;

/// <summary>Teaching mode of a session. Only <see cref="Batch"/> requires a batch.</summary>
public enum SessionType
{
    /// <summary>Scheduled for a predefined batch.</summary>
    Batch,
    /// <summary>Ad hoc group of selected students; may optionally reference a batch.</summary>
    Group,
    /// <summary>One teacher and exactly one student. Never linked to a batch.</summary>
    OneToOne,
    /// <summary>Small private group of selected students. Never linked to a batch.</summary>
    PrivateGroup
}

public enum SessionStatus { Scheduled, Live, Completed, Cancelled }
public enum DeliveryMode { Online, InPerson, Hybrid }
public enum MeetingProvider { None, Manual, GoogleMeet }
public enum ParticipantStatus { Active, Removed }
public enum AttendanceStatus { Present, Late, Absent, Excused }

/// <summary>A recurring schedule. Occurrences are materialised as <see cref="LearningSession"/> rows,
/// unique per (SeriesId, OccurrenceDate), so regeneration never duplicates sessions.</summary>
public class SessionSeries : TenantEntity
{
    public string Title { get; set; } = "";
    public SessionType SessionType { get; set; }
    public Guid TeacherId { get; set; }
    public Guid? BatchId { get; set; }
    public Guid? ArrangementId { get; set; }
    public Guid? CourseId { get; set; }
    public Guid? SubjectId { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public TimeOnly LocalStartTime { get; set; }
    public int DurationMinutes { get; set; }
    public RecurrenceFrequency Frequency { get; set; }
    public int Interval { get; set; } = 1;
    public WeekDays Days { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? OccurrenceCount { get; set; }
    public bool SkipHolidays { get; set; } = true;
    public DeliveryMode DeliveryMode { get; set; }
    public string? MeetingUrl { get; set; }
    public string? Location { get; set; }
    public bool IsActive { get; set; } = true;

    public RecurrencePattern Pattern => new(Frequency, Interval, Days, StartDate, EndDate, OccurrenceCount);
}

public class LearningSession : TenantEntity
{
    public string Title { get; set; } = "";
    public SessionType SessionType { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Scheduled;
    public Guid TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public Guid? BatchId { get; set; }
    public Batch? Batch { get; set; }
    public Guid? ArrangementId { get; set; }
    public TeachingArrangement? Arrangement { get; set; }
    public Guid? SeriesId { get; set; }
    public DateOnly? OccurrenceDate { get; set; }
    public Guid? CourseId { get; set; }
    public Course? Course { get; set; }
    public Guid? SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public Guid? ChapterId { get; set; }
    public Chapter? Chapter { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    /// <summary>IANA zone the session was scheduled in; used to show the organiser's intended wall-clock time.</summary>
    public string TimeZoneId { get; set; } = "UTC";
    public DeliveryMode DeliveryMode { get; set; }
    public MeetingProvider MeetingProvider { get; set; }
    public string? MeetingUrl { get; set; }
    public string? Location { get; set; }
    public string? Objectives { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public bool RecordingConsentRequired { get; set; } = true;
    public List<SessionParticipant> Participants { get; set; } = [];

    public static void ValidateShape(SessionType type, Guid? batchId, int activeStudentCount)
    {
        switch (type)
        {
            case SessionType.Batch when batchId is null:
                throw new DomainException("A batch session must reference a batch.", "batch_required");
            case SessionType.OneToOne when batchId is not null:
            case SessionType.PrivateGroup when batchId is not null:
                throw new DomainException("One-to-one and private group sessions are not linked to a batch.", "batch_not_allowed");
            case SessionType.OneToOne when activeStudentCount != 1:
                throw new DomainException("A one-to-one session must have exactly one student.", "one_to_one_student_count");
            case SessionType.Group or SessionType.PrivateGroup when activeStudentCount < 1:
                throw new DomainException("Select at least one student for a group session.", "group_student_count");
        }
    }

    public static void ValidateTimes(DateTime startUtc, DateTime endUtc)
    {
        if (endUtc <= startUtc) throw new DomainException("The session must end after it starts.", "invalid_time_range");
        if (endUtc - startUtc > TimeSpan.FromHours(12)) throw new DomainException("A session cannot be longer than 12 hours.", "invalid_time_range");
    }

    public SessionScheduleChange Reschedule(DateTime newStartUtc, DateTime newEndUtc, string newTimeZoneId, string? reason, Guid userId, DateTime nowUtc)
    {
        if (Status is SessionStatus.Cancelled or SessionStatus.Completed)
            throw new DomainException("Cancelled or completed sessions cannot be rescheduled.", "invalid_session_state");
        ValidateTimes(newStartUtc, newEndUtc);
        var change = new SessionScheduleChange
        {
            TenantId = TenantId,
            SessionId = Id,
            ChangeType = ScheduleChangeType.Rescheduled,
            PreviousStartUtc = StartUtc,
            PreviousEndUtc = EndUtc,
            NewStartUtc = newStartUtc,
            NewEndUtc = newEndUtc,
            Reason = reason,
            ChangedByUserId = userId,
            ChangedAtUtc = nowUtc,
        };
        StartUtc = newStartUtc;
        EndUtc = newEndUtc;
        TimeZoneId = newTimeZoneId;
        return change;
    }

    public SessionScheduleChange Cancel(string reason, Guid userId, DateTime nowUtc)
    {
        if (Status is SessionStatus.Cancelled) throw new DomainException("The session is already cancelled.", "invalid_session_state");
        if (Status is SessionStatus.Completed) throw new DomainException("A completed session cannot be cancelled.", "invalid_session_state");
        Status = SessionStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        CancellationReason = reason;
        return new SessionScheduleChange
        {
            TenantId = TenantId,
            SessionId = Id,
            ChangeType = ScheduleChangeType.Cancelled,
            PreviousStartUtc = StartUtc,
            PreviousEndUtc = EndUtc,
            Reason = reason,
            ChangedByUserId = userId,
            ChangedAtUtc = nowUtc,
        };
    }

    public void SetStatus(SessionStatus next)
    {
        var allowed = (Status, next) switch
        {
            (SessionStatus.Scheduled, SessionStatus.Live) => true,
            (SessionStatus.Scheduled, SessionStatus.Completed) => true,
            (SessionStatus.Live, SessionStatus.Completed) => true,
            _ => false,
        };
        if (!allowed) throw new DomainException($"Cannot change a session from {Status} to {next}.", "invalid_session_state");
        Status = next;
    }
}

/// <summary>A student taking part in a session. Removal is a status change, preserving history.</summary>
public class SessionParticipant : TenantEntity
{
    public Guid SessionId { get; set; }
    public LearningSession? Session { get; set; }
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public ParticipantStatus Status { get; set; } = ParticipantStatus.Active;
    public DateTime? RemovedAtUtc { get; set; }
    public string? RemovalReason { get; set; }
}

public enum ScheduleChangeType { Rescheduled, Cancelled, ParticipantAdded, ParticipantRemoved }

/// <summary>Immutable history of schedule and participant changes.</summary>
public class SessionScheduleChange : TenantEntity
{
    public Guid SessionId { get; set; }
    public ScheduleChangeType ChangeType { get; set; }
    public DateTime? PreviousStartUtc { get; set; }
    public DateTime? PreviousEndUtc { get; set; }
    public DateTime? NewStartUtc { get; set; }
    public DateTime? NewEndUtc { get; set; }
    public Guid? StudentId { get; set; }
    public string? Reason { get; set; }
    public Guid ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}

public enum AgendaItemKind { Doubt, Topic, Note }

/// <summary>Draft agenda for a session, including unresolved doubts carried forward automatically.</summary>
public class SessionAgendaItem : TenantEntity
{
    public Guid SessionId { get; set; }
    public AgendaItemKind Kind { get; set; }
    public Guid? DoubtId { get; set; }
    public string Title { get; set; } = "";
    public bool IsDiscussed { get; set; }
    public int SortOrder { get; set; }
}

public class AttendanceRecord : TenantEntity
{
    public Guid SessionId { get; set; }
    public LearningSession? Session { get; set; }
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public AttendanceStatus Status { get; set; }
    public string? Note { get; set; }
    public DateTime MarkedAtUtc { get; set; }
    public Guid MarkedByUserId { get; set; }
}

/// <summary>Weekly availability window in the teacher's local time zone.</summary>
public class TeacherAvailability : TenantEntity
{
    public Guid TeacherId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartLocalTime { get; set; }
    public TimeOnly EndLocalTime { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
}
