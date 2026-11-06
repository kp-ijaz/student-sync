using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using StudentSync.Application.Common;
using StudentSync.Domain.AI;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Assessments;
using StudentSync.Domain.Assignments;
using StudentSync.Domain.Common;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Content;
using StudentSync.Domain.Doubts;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;
using StudentSync.Domain.Platform;
using StudentSync.Domain.Scheduling;
using StudentSync.Domain.Tenancy;

namespace StudentSync.Infrastructure.Persistence;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options, ITenantContext tenant, ICurrentUser currentUser, IClock clock, INotificationDispatcher? dispatcher = null)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    /// <summary>Evaluated per query by the tenant query filters. Null (platform context) matches no tenant-owned rows.</summary>
    public Guid? CurrentTenantId => tenant.TenantId;

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<InstitutionHoliday> Holidays => Set<InstitutionHoliday>();
    public DbSet<SubscriptionPlan> Plans => Set<SubscriptionPlan>();
    public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
    public DbSet<TenantSubscription> Subscriptions => Set<TenantSubscription>();
    public DbSet<SubscriptionEvent> SubscriptionEvents => Set<SubscriptionEvent>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Parent> Parents => Set<Parent>();
    public DbSet<ParentStudentLink> ParentLinks => Set<ParentStudentLink>();
    public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
    public DbSet<Term> Terms => Set<Term>();
    public DbSet<GradeLevel> GradeLevels => Set<GradeLevel>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<TeacherSubject> TeacherSubjects => Set<TeacherSubject>();
    public DbSet<CourseEnrolment> CourseEnrolments => Set<CourseEnrolment>();
    public DbSet<StudentSubject> StudentSubjects => Set<StudentSubject>();
    public DbSet<Curriculum> Curricula => Set<Curriculum>();
    public DbSet<StudentLearningNeed> LearningNeeds => Set<StudentLearningNeed>();
    public DbSet<StudentNote> StudentNotes => Set<StudentNote>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<BatchEnrolment> BatchEnrolments => Set<BatchEnrolment>();
    public DbSet<TeachingArrangement> Arrangements => Set<TeachingArrangement>();
    public DbSet<TeachingArrangementStudent> ArrangementStudents => Set<TeachingArrangementStudent>();
    public DbSet<SessionSeries> SessionSeries => Set<SessionSeries>();
    public DbSet<LearningSession> Sessions => Set<LearningSession>();
    public DbSet<SessionParticipant> SessionParticipants => Set<SessionParticipant>();
    public DbSet<SessionScheduleChange> SessionChanges => Set<SessionScheduleChange>();
    public DbSet<SessionAgendaItem> AgendaItems => Set<SessionAgendaItem>();
    public DbSet<AttendanceRecord> Attendance => Set<AttendanceRecord>();
    public DbSet<TeacherAvailability> TeacherAvailability => Set<TeacherAvailability>();
    public DbSet<StoredFile> Files => Set<StoredFile>();
    public DbSet<SessionNote> Notes => Set<SessionNote>();
    public DbSet<LearningMaterial> Materials => Set<LearningMaterial>();
    public DbSet<ClassRecording> Recordings => Set<ClassRecording>();
    public DbSet<RecordingProgress> RecordingProgress => Set<RecordingProgress>();
    public DbSet<PrerecordedVideo> PrerecordedVideos => Set<PrerecordedVideo>();
    public DbSet<ConsentRecord> Consents => Set<ConsentRecord>();
    public DbSet<ClassTranscript> Transcripts => Set<ClassTranscript>();
    public DbSet<TranscriptSegment> TranscriptSegments => Set<TranscriptSegment>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<AssignmentRecipient> AssignmentRecipients => Set<AssignmentRecipient>();
    public DbSet<AssignmentAttachment> AssignmentAttachments => Set<AssignmentAttachment>();
    public DbSet<AssignmentSubmission> Submissions => Set<AssignmentSubmission>();
    public DbSet<SubmissionFile> SubmissionFiles => Set<SubmissionFile>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionOption> QuestionOptions => Set<QuestionOption>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamQuestion> ExamQuestions => Set<ExamQuestion>();
    public DbSet<ExamRecipient> ExamRecipients => Set<ExamRecipient>();
    public DbSet<ExamAttempt> ExamAttempts => Set<ExamAttempt>();
    public DbSet<ExamAnswer> ExamAnswers => Set<ExamAnswer>();
    public DbSet<Doubt> Doubts => Set<Doubt>();
    public DbSet<DoubtMessage> DoubtMessages => Set<DoubtMessage>();
    public DbSet<DoubtAttachment> DoubtAttachments => Set<DoubtAttachment>();
    public DbSet<DoubtStatusChange> DoubtStatusChanges => Set<DoubtStatusChange>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<DeviceRegistration> Devices => Set<DeviceRegistration>();
    public DbSet<AiProcessingJob> AiJobs => Set<AiProcessingJob>();
    public DbSet<AiLessonDraft> AiDrafts => Set<AiLessonDraft>();
    public DbSet<AiLessonDraftVersion> AiDraftVersions => Set<AiLessonDraftVersion>();
    public DbSet<AiUsageRecord> AiUsage => Set<AiUsageRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<IntegrationConnection> Integrations => Set<IntegrationConnection>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
        b.Properties<decimal>().HavePrecision(18, 2);
        b.Properties<DateTime>().HaveConversion<UtcConverter>();
        b.Properties<DateTime?>().HaveConversion<NullableUtcConverter>();
        b.Properties<string>().HaveMaxLength(500);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        ModelConfiguration.Configure(b);

        foreach (var entity in b.Model.GetEntityTypes())
        {
            var clr = entity.ClrType;
            if (typeof(AuditableEntity).IsAssignableFrom(clr))
                b.Entity(clr).Property(nameof(AuditableEntity.RowVersion)).IsRowVersion();

            if (typeof(ITenantOwned).IsAssignableFrom(clr))
            {
                b.Entity(clr).HasOne(typeof(Tenant)).WithMany().HasForeignKey(nameof(ITenantOwned.TenantId)).OnDelete(DeleteBehavior.Restrict);
                b.Entity(clr).HasIndex(nameof(ITenantOwned.TenantId));
                // e => e.TenantId == this.CurrentTenantId
                var e = Expression.Parameter(clr, "e");
                var filter = Expression.Lambda(Expression.Equal(
                    Expression.Convert(Expression.Property(e, nameof(ITenantOwned.TenantId)), typeof(Guid?)),
                    Expression.Property(Expression.Constant(this), nameof(CurrentTenantId))), e);
                b.Entity(clr).HasQueryFilter(filter);
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
        => throw new NotSupportedException("Use SaveChangesAsync.");

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        Guid? userId = currentUser.IsAuthenticated ? currentUser.UserId : null;
        var current = tenant.TenantId;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is ITenantOwned owned)
            {
                if (entry.State == EntityState.Added)
                {
                    if (owned.TenantId == Guid.Empty)
                        owned.TenantId = current ?? throw new InvalidOperationException($"{entry.Entity.GetType().Name} created without a tenant context.");
                    else if (current is not null && owned.TenantId != current)
                        throw new TenantIsolationException("Cross-tenant write blocked.");
                }
                else if (entry.State is EntityState.Modified or EntityState.Deleted && current is not null && owned.TenantId != current)
                    throw new TenantIsolationException("Cross-tenant write blocked.");
            }

            if (entry.Entity is AuditableEntity a)
            {
                if (entry.State == EntityState.Added)
                {
                    if (a.CreatedAtUtc == default) a.CreatedAtUtc = now;
                    a.CreatedByUserId ??= userId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    a.UpdatedAtUtc = now;
                    a.UpdatedByUserId = userId;
                    entry.Property(nameof(AuditableEntity.CreatedAtUtc)).IsModified = false;
                    entry.Property(nameof(AuditableEntity.CreatedByUserId)).IsModified = false;
                }
            }
        }

        var newNotifications = ChangeTracker.Entries<Notification>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        if (newNotifications.Count > 0) dispatcher?.Dispatch(newNotifications);
        return result;
    }

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v == null ? v : v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc),
        v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));
}
