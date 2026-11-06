using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using StudentSync.Domain.AI;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Assessments;
using StudentSync.Domain.Assignments;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Content;
using StudentSync.Domain.Doubts;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;
using StudentSync.Domain.Platform;
using StudentSync.Domain.Scheduling;
using StudentSync.Domain.Tenancy;

namespace StudentSync.Application.Common;

/// <summary>
/// Persistence boundary used by application services. Tenant-owned sets are automatically filtered to the
/// current tenant; anything not visible through these sets does not exist for the caller.
/// </summary>
public interface IAppDbContext
{
    DbSet<AppUser> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Tenant> Tenants { get; }
    DbSet<InstitutionHoliday> Holidays { get; }
    DbSet<SubscriptionPlan> Plans { get; }
    DbSet<PlanFeature> PlanFeatures { get; }
    DbSet<TenantSubscription> Subscriptions { get; }
    DbSet<SubscriptionEvent> SubscriptionEvents { get; }
    DbSet<Teacher> Teachers { get; }
    DbSet<Student> Students { get; }
    DbSet<Parent> Parents { get; }
    DbSet<ParentStudentLink> ParentLinks { get; }
    DbSet<AcademicYear> AcademicYears { get; }
    DbSet<Term> Terms { get; }
    DbSet<GradeLevel> GradeLevels { get; }
    DbSet<Course> Courses { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<Chapter> Chapters { get; }
    DbSet<Topic> Topics { get; }
    DbSet<TeacherSubject> TeacherSubjects { get; }
    DbSet<CourseEnrolment> CourseEnrolments { get; }
    DbSet<StudentSubject> StudentSubjects { get; }
    DbSet<Curriculum> Curricula { get; }
    DbSet<StudentLearningNeed> LearningNeeds { get; }
    DbSet<StudentNote> StudentNotes { get; }
    DbSet<Batch> Batches { get; }
    DbSet<BatchEnrolment> BatchEnrolments { get; }
    DbSet<TeachingArrangement> Arrangements { get; }
    DbSet<TeachingArrangementStudent> ArrangementStudents { get; }
    DbSet<SessionSeries> SessionSeries { get; }
    DbSet<LearningSession> Sessions { get; }
    DbSet<SessionParticipant> SessionParticipants { get; }
    DbSet<SessionScheduleChange> SessionChanges { get; }
    DbSet<SessionAgendaItem> AgendaItems { get; }
    DbSet<AttendanceRecord> Attendance { get; }
    DbSet<TeacherAvailability> TeacherAvailability { get; }
    DbSet<StoredFile> Files { get; }
    DbSet<SessionNote> Notes { get; }
    DbSet<LearningMaterial> Materials { get; }
    DbSet<ClassRecording> Recordings { get; }
    DbSet<RecordingProgress> RecordingProgress { get; }
    DbSet<PrerecordedVideo> PrerecordedVideos { get; }
    DbSet<ConsentRecord> Consents { get; }
    DbSet<ClassTranscript> Transcripts { get; }
    DbSet<TranscriptSegment> TranscriptSegments { get; }
    DbSet<Assignment> Assignments { get; }
    DbSet<AssignmentRecipient> AssignmentRecipients { get; }
    DbSet<AssignmentAttachment> AssignmentAttachments { get; }
    DbSet<AssignmentSubmission> Submissions { get; }
    DbSet<SubmissionFile> SubmissionFiles { get; }
    DbSet<Question> Questions { get; }
    DbSet<QuestionOption> QuestionOptions { get; }
    DbSet<Exam> Exams { get; }
    DbSet<ExamQuestion> ExamQuestions { get; }
    DbSet<ExamRecipient> ExamRecipients { get; }
    DbSet<ExamAttempt> ExamAttempts { get; }
    DbSet<ExamAnswer> ExamAnswers { get; }
    DbSet<Doubt> Doubts { get; }
    DbSet<DoubtMessage> DoubtMessages { get; }
    DbSet<DoubtAttachment> DoubtAttachments { get; }
    DbSet<DoubtStatusChange> DoubtStatusChanges { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<ConversationParticipant> ConversationParticipants { get; }
    DbSet<Message> Messages { get; }
    DbSet<Announcement> Announcements { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<NotificationDelivery> NotificationDeliveries { get; }
    DbSet<NotificationPreference> NotificationPreferences { get; }
    DbSet<DeviceRegistration> Devices { get; }
    DbSet<AiProcessingJob> AiJobs { get; }
    DbSet<AiLessonDraft> AiDrafts { get; }
    DbSet<AiLessonDraftVersion> AiDraftVersions { get; }
    DbSet<AiUsageRecord> AiUsage { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<IntegrationConnection> Integrations { get; }
    DbSet<FeatureFlag> FeatureFlags { get; }

    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    Guid? TenantId { get; }
    UserRole Role { get; }
    string TimeZoneId { get; }
    string? IpAddress { get; }
    bool Has(string permission);
}

/// <summary>Tenant scope for the current unit of work. Background jobs set it explicitly per tenant.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    IDisposable BeginScope(Guid tenantId);
}

public interface IClock { DateTime UtcNow { get; } }

public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken ct);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

/// <summary>Issues and validates short-lived, user-bound download tokens for private files.</summary>
public interface IFileAccessTokens
{
    string Create(Guid fileId, Guid userId, Guid tenantId, TimeSpan lifetime);
    bool TryValidate(string token, out Guid fileId, out Guid userId, out Guid tenantId);
}

public interface IBackgroundJobs
{
    string Enqueue<T>(Expression<Func<T, Task>> job);
    string Schedule<T>(Expression<Func<T, Task>> job, TimeSpan delay);
}

public interface IRealtimeNotifier
{
    Task SendToUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload, CancellationToken ct = default);
}

public sealed record EmailMessage(string ToAddress, string ToName, string Subject, string TextBody);
public sealed record DeliveryResult(bool Accepted, string? ProviderMessageId, string? Error);

public interface IEmailSender
{
    bool IsConfigured { get; }
    Task<DeliveryResult> SendAsync(EmailMessage message, CancellationToken ct);
}

public interface IPushSender
{
    bool IsConfigured { get; }
    Task<DeliveryResult> SendAsync(string deviceToken, string title, string body, string? link, CancellationToken ct);
}

public sealed record LlmResult(bool Success, string? Text, int InputTokens, int OutputTokens, string? Error, decimal EstimatedCostUsd = 0);

/// <summary>Language-model provider. Implementations must report IsConfigured honestly and never fabricate output.</summary>
public interface ILanguageModel
{
    bool IsConfigured { get; }
    string ProviderName { get; }
    string ModelName { get; }
    Task<LlmResult> CompleteAsync(string systemPrompt, string userPrompt, int maxTokens, CancellationToken ct);
}

public sealed record TranscriptionResult(bool Success, string? Language, IReadOnlyList<(double Start, double End, string? Speaker, string Text, double? Confidence)> Segments, string? Error);

public interface ITranscriptionProvider
{
    bool IsConfigured { get; }
    string ProviderName { get; }
    Task<TranscriptionResult> TranscribeAsync(Stream audio, string contentType, string? languageHint, CancellationToken ct);
}

public sealed record CreatedAccount(Guid UserId, string? TemporaryPassword);

/// <summary>Account creation goes through ASP.NET Core Identity (password hashing, normalisation, lockout).</summary>
public interface IUserAccountService
{
    Task<CreatedAccount> CreateAsync(Guid? tenantId, string email, string fullName, UserRole role, string timeZoneId, string? password, bool isDemo, CancellationToken ct);
    Task SetActiveAsync(Guid userId, bool active, CancellationToken ct);
    /// <summary>Permanently removes an account that has no other records.</summary>
    Task DeleteAsync(Guid userId, CancellationToken ct);
    /// <summary>Admin-initiated reset: issues a new temporary password (returned once, never stored in clear), forces a change at next sign-in and signs out every session.</summary>
    Task<string> ResetPasswordAsync(Guid userId, CancellationToken ct);
}

/// <summary>Receives notifications after they are committed (real-time push and external delivery scheduling).</summary>
public interface INotificationDispatcher
{
    void Dispatch(IReadOnlyList<StudentSync.Domain.Communication.Notification> committed);
}
