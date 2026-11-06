using StudentSync.Domain.Communication;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Contracts;

// ---------- Messaging ----------

public sealed record ConversationDto(Guid Id, ConversationType Type, string Title, IReadOnlyList<LookupItem> Participants, string? LastMessagePreview, DateTime LastMessageAtUtc, int UnreadCount);
public sealed record MessageDto(Guid Id, Guid ConversationId, Guid SenderId, string SenderName, string Body, DateTime CreatedAtUtc, FileDto? Attachment, bool IsMine);
public sealed record StartConversationRequest(Guid RecipientUserId, string Body);
public sealed record SendMessageRequest(string Body, Guid? AttachmentFileId);
public sealed record ContactDto(Guid UserId, string FullName, string Role, string? Context);

public sealed record AnnouncementDto(Guid Id, string Title, string Body, AnnouncementAudience Audience, Guid? BatchId, string? BatchName, string AuthorName, DateTime PublishedAtUtc);
public sealed record CreateAnnouncementRequest(string Title, string Body, AnnouncementAudience Audience, Guid? BatchId);

// ---------- Notifications ----------

public sealed record NotificationDto(Guid Id, NotificationType Type, string Title, string Body, string? Link, DateTime CreatedAtUtc, bool IsRead);
public sealed record NotificationPreferencesDto(bool EmailEnabled, bool PushEnabled, TimeOnly? QuietHoursStart, TimeOnly? QuietHoursEnd, IReadOnlyList<NotificationType> MutedTypes, bool EmailProviderConfigured, bool PushProviderConfigured);
public sealed record UpdateNotificationPreferencesRequest(bool EmailEnabled, bool PushEnabled, TimeOnly? QuietHoursStart, TimeOnly? QuietHoursEnd, List<NotificationType> MutedTypes);
public sealed record RegisterDeviceRequest(string Platform, string Token);
public sealed record UnreadCountsDto(int Notifications, int Messages);

// ---------- Dashboards ----------

public sealed record StatDto(string Key, string Label, string Value, string? Hint = null);

public sealed record AdminDashboardDto(
    IReadOnlyList<StatDto> Stats,
    IReadOnlyList<SessionListItemDto> UpcomingSessions,
    AttendanceSummaryDto AttendanceLast30Days,
    IReadOnlyList<SessionTypeCountDto> SessionsByType,
    IReadOnlyList<AlertDto> Alerts,
    IReadOnlyList<ActivityDto> RecentActivity);

public sealed record TeacherDashboardDto(
    SessionListItemDto? NextSession,
    IReadOnlyList<SessionListItemDto> TodaySessions,
    IReadOnlyList<SessionListItemDto> UpcomingSessions,
    IReadOnlyList<SessionListItemDto> PendingAttendance,
    IReadOnlyList<DoubtListItemDto> UnresolvedDoubts,
    IReadOnlyList<SubmissionToGradeDto> SubmissionsToGrade,
    IReadOnlyList<AiDraftSummaryWithSessionDto> PendingAiApprovals,
    IReadOnlyList<StatDto> Stats);

public sealed record SubmissionToGradeDto(Guid SubmissionId, Guid AssignmentId, string AssignmentTitle, string StudentName, DateTime? SubmittedAtUtc, bool IsLate);
public sealed record AiDraftSummaryWithSessionDto(Guid DraftId, Guid SessionId, string SessionTitle, DateTime CreatedAtUtc);

public sealed record StudentDashboardDto(
    string FirstName,
    SessionListItemDto? NextSession,
    IReadOnlyList<SessionListItemDto> UpcomingSessions,
    IReadOnlyList<AssignmentListItemDto> DueAssignments,
    IReadOnlyList<DoubtListItemDto> OpenDoubts,
    IReadOnlyList<RecentContentDto> RecentContent,
    ProgressDto Progress,
    IReadOnlyList<NotificationDto> Notifications);

public sealed record RecentContentDto(string Kind, Guid Id, Guid SessionId, string Title, string SessionTitle, DateTime AtUtc);

public sealed record ParentDashboardDto(IReadOnlyList<LookupItem> Children, Guid? SelectedChildId, ChildOverviewDto? Child);

public sealed record ChildOverviewDto(
    Guid StudentId, string Name,
    IReadOnlyList<SessionListItemDto> UpcomingSessions,
    IReadOnlyList<AttendanceItemDto> RecentAttendance,
    IReadOnlyList<AssignmentListItemDto> Assignments,
    IReadOnlyList<ExamResultItemDto> Results,
    ProgressDto Progress);

public sealed record AttendanceItemDto(Guid SessionId, string SessionTitle, DateTime StartUtc, SessionType SessionType, AttendanceStatus Status);
public sealed record ExamResultItemDto(Guid ExamId, string ExamTitle, DateTime? SubmittedAtUtc, decimal? Score, decimal TotalMarks);

public sealed record AttendanceSummaryDto(int Present, int Late, int Absent, int Excused, int Unmarked, double? AttendanceRatePercent);
public sealed record SessionTypeCountDto(SessionType SessionType, int Sessions, int StudentPlaces);
public sealed record AlertDto(string Severity, string Message, string? Link);
public sealed record ActivityDto(DateTime AtUtc, string Summary, string? UserName);

// ---------- Progress & reports ----------

/// <summary>Evidence-based progress. Each metric documents how it is calculated; no composite score is invented.</summary>
public sealed record ProgressDto(
    Guid StudentId,
    MetricDto Attendance,
    MetricDto AssignmentsSubmitted,
    MetricDto AverageAssignmentMark,
    MetricDto AverageExamScore,
    MetricDto OpenDoubts,
    MetricDto LessonsAttended,
    MetricDto ActiveWeeks);

public sealed record MetricDto(string Label, string Value, string HowCalculated, double? Percent = null);

public sealed record ReportQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public Guid? TeacherId { get; init; }
    public Guid? BatchId { get; init; }
    public SessionType? SessionType { get; init; }
}

public sealed record InstitutionReportDto(
    DateOnly From, DateOnly To,
    IReadOnlyList<SessionTypeCountDto> SessionsByType,
    AttendanceSummaryDto Attendance,
    IReadOnlyList<TeacherReportRowDto> Teachers,
    IReadOnlyList<BatchReportRowDto> Batches,
    DoubtReportDto Doubts,
    AssignmentReportDto Assignments);

public sealed record TeacherReportRowDto(Guid TeacherId, string TeacherName, int SessionsDelivered, int SessionsCancelled, double? AttendanceRatePercent, int DoubtsResolved, double? AvgDoubtResolutionHours, int SubmissionsGraded);
public sealed record BatchReportRowDto(Guid BatchId, string BatchName, int Students, int Sessions, double? AttendanceRatePercent);
public sealed record DoubtReportDto(int Raised, int Resolved, int Open, double? AvgResolutionHours);
public sealed record AssignmentReportDto(int Published, int ExpectedSubmissions, int Submitted, int Graded, int Late);
