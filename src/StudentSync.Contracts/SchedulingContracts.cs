using StudentSync.Domain.Scheduling;

namespace StudentSync.Contracts;

public sealed record RecurrenceRequest(
    RecurrenceFrequency Frequency, int Interval, WeekDays Days, DateOnly? EndDate, int? Count, bool SkipHolidays = true);

/// <summary>
/// Creates a single session or a recurring series. Times are given as local wall-clock time in <see cref="TimeZoneId"/>.
/// BatchId is required only for <see cref="SessionType.Batch"/>; StudentIds are used for every other type.
/// </summary>
public sealed record CreateSessionRequest(
    string Title,
    SessionType SessionType,
    Guid? TeacherId,
    Guid? BatchId,
    Guid? ArrangementId,
    Guid? CourseId,
    Guid? SubjectId,
    Guid? ChapterId,
    List<Guid>? StudentIds,
    string TimeZoneId,
    DateOnly LocalDate,
    TimeOnly LocalStartTime,
    int DurationMinutes,
    DeliveryMode DeliveryMode,
    string? MeetingUrl,
    string? Location,
    string? Objectives,
    RecurrenceRequest? Recurrence,
    bool AcknowledgeConflicts = false,
    List<DateOnly>? SkipDates = null);

/// <summary>
/// A student's weekly plan: one <see cref="CreateSessionRequest"/> per subject/teacher (for example Monday and Wednesday
/// Maths with one teacher, Tuesday Science with another). All parts are for the same student and are created together.
/// </summary>
public sealed record CreateSessionBatchRequest(List<CreateSessionRequest> Items, bool AcknowledgeConflicts = false);

/// <summary>
/// The weekly-plan check: the overall result (same fields as <see cref="ConflictCheckResponse"/>) plus, per part, what clashes
/// and how to fix it — teachers of that subject who are free (and within their recorded hours) and nearby free start times.
/// </summary>
public sealed record WeeklyPlanCheckResponse(bool HasBlockingConflicts, IReadOnlyList<ScheduleConflictDto> Conflicts, IReadOnlyList<OccurrencePreviewDto> Occurrences,
    IReadOnlyList<string> Warnings, IReadOnlyList<PlanPartCheckDto> Parts, IReadOnlyList<HoursCheckDto>? Hours = null);

/// <summary>
/// Classes must fit the student's prepaid hours (owner decision 2026-11-17). Left = hours added − hours used; Free = Left −
/// hours already booked (classes not yet marked). Needed = these classes; FitCount = how many of them, in date order, fit.
/// </summary>
public sealed record HoursCheckDto(Guid StudentId, string StudentName, bool HasHours, decimal Left, decimal Booked, decimal Free, decimal Needed, int FitCount, int Classes)
{
    public bool Fits => HasHours && Needed <= Free;
}

/// <param name="Index">Position of the part in the request's Items.</param>
/// <param name="ClashDates">Local dates of this part that overlap another class of the teacher or the student.</param>
/// <param name="OutsideAvailability">The teacher recorded weekly hours and some classes fall outside them (a warning, not a block).</param>
public sealed record PlanPartCheckDto(int Index, int ClassCount, bool TeacherBusy, IReadOnlyList<ScheduleConflictDto> Conflicts, IReadOnlyList<DateOnly> ClashDates,
    bool OutsideAvailability, IReadOnlyList<LookupItem> FreeTeachers, IReadOnlyList<TimeOnly> FreeTimes);

public sealed record CreateSessionResponse(IReadOnlyList<Guid> SessionIds, Guid? SeriesId, IReadOnlyList<string> Warnings);

public sealed record ScheduleConflictDto(string Kind, Guid PersonId, string PersonName, Guid SessionId, string SessionTitle, DateTime StartUtc, DateTime EndUtc);

public sealed record ConflictCheckResponse(bool HasBlockingConflicts, IReadOnlyList<ScheduleConflictDto> Conflicts, IReadOnlyList<OccurrencePreviewDto> Occurrences, IReadOnlyList<string> Warnings,
    IReadOnlyList<HoursCheckDto>? Hours = null);
public sealed record OccurrencePreviewDto(DateOnly LocalDate, DateTime StartUtc, DateTime EndUtc, string Resolution, bool SkippedHoliday);

public sealed record RescheduleSessionRequest(string TimeZoneId, DateOnly LocalDate, TimeOnly LocalStartTime, int DurationMinutes, string? Reason, string RowVersion, bool AcknowledgeConflicts = false);
/// <summary>Kind: why the class did not happen (teacher absent = the teacher's missed hours; student absent = the student's).</summary>
public sealed record CancelSessionRequest(string Reason, bool CancelFutureSeriesOccurrences = false, CancellationKind Kind = CancellationKind.Other);
public sealed record UpdateSessionDetailsRequest(string Title, Guid? SubjectId, Guid? ChapterId, DeliveryMode DeliveryMode, string? MeetingUrl, string? Location, string? Objectives, string RowVersion);
public sealed record SetSessionStatusRequest(SessionStatus Status);
public sealed record AddParticipantRequest(Guid StudentId);
public sealed record RemoveParticipantRequest(string Reason);

public sealed record SessionQuery
{
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public SessionType? SessionType { get; init; }
    public SessionStatus? Status { get; init; }
    public Guid? TeacherId { get; init; }
    public Guid? StudentId { get; init; }
    public Guid? BatchId { get; init; }
    public int? Limit { get; init; }
}

public sealed record SessionListItemDto(
    Guid Id, string Title, SessionType SessionType, SessionStatus Status, DateTime StartUtc, DateTime EndUtc, string TimeZoneId,
    Guid TeacherId, string TeacherName, Guid? BatchId, string? BatchName, string? SubjectName, int ParticipantCount,
    DeliveryMode DeliveryMode, bool HasMeetingLink, bool IsRecurring, string? TeacherPhotoUrl = null);

public sealed record SessionDetailDto(
    Guid Id, string Title, SessionType SessionType, SessionStatus Status, DateTime StartUtc, DateTime EndUtc, string TimeZoneId,
    Guid TeacherId, string TeacherName, Guid? BatchId, string? BatchName, Guid? ArrangementId, string? ArrangementTitle,
    Guid? SeriesId, Guid? CourseId, string? CourseName, Guid? SubjectId, string? SubjectName, Guid? ChapterId, string? ChapterTitle,
    DeliveryMode DeliveryMode, MeetingProvider MeetingProvider, string? MeetingUrl, string? Location, string? Objectives,
    string? CancellationReason, bool CanManage, bool CanMarkAttendance, int ParticipantCount,
    IReadOnlyList<ParticipantDto> Participants, string RowVersion, string? TeacherPhotoUrl = null);

/// <summary>Participant list. Students and parents only receive their own entry; classmates are not disclosed.</summary>
public sealed record ParticipantDto(Guid StudentId, string StudentName, ParticipantStatus Status, AttendanceStatus? Attendance);

public sealed record ScheduleChangeDto(DateTime ChangedAtUtc, ScheduleChangeType ChangeType, DateTime? PreviousStartUtc, DateTime? NewStartUtc, string? StudentName, string? Reason, string ChangedBy);

public sealed record AgendaItemDto(Guid Id, AgendaItemKind Kind, Guid? DoubtId, string Title, bool IsDiscussed, string? StudentName);
public sealed record AddAgendaItemRequest(string Title);

public sealed record ClassPreparationDto(
    SessionDetailDto Session,
    IReadOnlyList<AgendaItemDto> Agenda,
    IReadOnlyList<DoubtListItemDto> OpenDoubts,
    IReadOnlyList<PrepStudentDto> Students);

public sealed record PrepStudentDto(Guid StudentId, string StudentName, int MissedRecentSessions, int PendingAssignments, int OpenDoubts);

// ---------- Attendance ----------

public sealed record AttendanceRosterDto(Guid SessionId, SessionStatus SessionStatus, bool CanEdit, IReadOnlyList<AttendanceRowDto> Rows);
public sealed record AttendanceRowDto(Guid StudentId, string StudentName, AttendanceStatus? Status, string? Note, DateTime? MarkedAtUtc);
public sealed record MarkAttendanceRequest(List<AttendanceEntry> Entries);
public sealed record AttendanceEntry(Guid StudentId, AttendanceStatus Status, string? Note);

// ---------- Availability ----------

public sealed record AvailabilityDto(Guid Id, DayOfWeek DayOfWeek, TimeOnly StartLocalTime, TimeOnly EndLocalTime, string TimeZoneId);
public sealed record SaveAvailabilityRequest(List<AvailabilitySlot> Slots, string TimeZoneId);
public sealed record AvailabilitySlot(DayOfWeek DayOfWeek, TimeOnly StartLocalTime, TimeOnly EndLocalTime);
