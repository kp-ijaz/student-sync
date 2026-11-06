using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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

namespace StudentSync.Infrastructure.Persistence;

/// <summary>
/// Relational model. Delete behaviour is Restrict everywhere except pure child rows of one aggregate
/// (join rows, options, segments, versions), so academic, attendance and audit history is never cascaded away.
/// </summary>
internal static class ModelConfiguration
{
    private const DeleteBehavior R = DeleteBehavior.Restrict;
    private const DeleteBehavior C = DeleteBehavior.Cascade;

    private static ReferenceCollectionBuilder<TP, TD> Fk<TD, TP>(ModelBuilder b, Expression<Func<TD, object?>> fk, DeleteBehavior behavior = R)
        where TD : class where TP : class
        => b.Entity<TD>().HasOne<TP>().WithMany().HasForeignKey(fk).OnDelete(behavior);

    private static void Max<T>(ModelBuilder b, params Expression<Func<T, string?>>[] props) where T : class
    {
        foreach (var p in props) b.Entity<T>().Property(p).HasColumnType("nvarchar(max)");
    }

    public static void Configure(ModelBuilder b)
    {
        // ---------- Identity & platform ----------
        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(150);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
            e.Property(x => x.PreferredLanguage).HasMaxLength(10);
            e.HasIndex(x => new { x.TenantId, x.NormalizedEmail }).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(R);
            e.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.PhotoFileId).OnDelete(R);
        });
        b.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.FamilyId });
            e.Property(x => x.TokenHash).HasMaxLength(100);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(C);
        });
        b.Entity<Tenant>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Slug).HasMaxLength(40);
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.CountryCode).HasMaxLength(2);
            e.Property(x => x.CurrencyCode).HasMaxLength(3);
            e.Property(x => x.DefaultTimeZoneId).HasMaxLength(64);
            e.Property(x => x.BrandColorHex).HasMaxLength(7);
            e.ToTable(t => t.HasCheckConstraint("CK_Tenants_Retention", "[RecordingRetentionDays] BETWEEN 7 AND 3650"));
        });
        b.Entity<InstitutionHoliday>().HasIndex(x => new { x.TenantId, x.Date }).IsUnique();
        b.Entity<SubscriptionPlan>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasMany(x => x.Features).WithOne().HasForeignKey(x => x.PlanId).OnDelete(C);
            e.ToTable(t => t.HasCheckConstraint("CK_Plans_Limits", "[MonthlyPrice] >= 0 AND [MaxStudents] > 0 AND [MaxTeachers] > 0"));
        });
        b.Entity<PlanFeature>().HasIndex(x => new { x.PlanId, x.FeatureKey }).IsUnique();
        b.Entity<TenantSubscription>(e =>
        {
            e.HasIndex(x => x.TenantId).IsUnique();
            e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(R);
        });
        Fk<SubscriptionEvent, TenantSubscription>(b, x => x.SubscriptionId);
        b.Entity<FeatureFlag>().HasIndex(x => x.Key).IsUnique();
        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
            e.Property(x => x.Summary).HasMaxLength(1000);
            e.Property(x => x.ChangedFields).HasMaxLength(2000);
        });
        b.Entity<IdempotencyRecord>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Key }).IsUnique();
            e.Property(x => x.Key).HasMaxLength(100);
        });
        Max<IdempotencyRecord>(b, x => x.ResponseBody);
        b.Entity<IntegrationConnection>().HasIndex(x => new { x.UserId, x.Provider }).IsUnique();
        Max<IntegrationConnection>(b, x => x.EncryptedTokens);
        Fk<IntegrationConnection, AppUser>(b, x => x.UserId);

        // ---------- People ----------
        foreach (var (type, nav) in new[] { (typeof(Teacher), nameof(Teacher.User)), (typeof(Student), nameof(Student.User)), (typeof(Parent), nameof(Parent.User)) })
        {
            b.Entity(type).Property("Id").ValueGeneratedNever();
            b.Entity(type).HasOne(typeof(AppUser), nav).WithOne().HasForeignKey(type, "Id").OnDelete(R);
        }
        b.Entity<Teacher>().HasIndex(x => new { x.TenantId, x.EmployeeCode }).IsUnique().HasFilter("[EmployeeCode] IS NOT NULL");
        b.Entity<Teacher>().Property(x => x.Bio).HasMaxLength(2000);
        b.Entity<Student>().HasIndex(x => new { x.TenantId, x.StudentCode }).IsUnique();
        b.Entity<Student>().Property(x => x.StudentCode).HasMaxLength(30);
        Fk<Student, GradeLevel>(b, x => x.GradeLevelId);
        Fk<Student, Curriculum>(b, x => x.CurriculumId);
        Fk<Student, AcademicYear>(b, x => x.AcademicYearId);
        b.Entity<StudentLearningNeed>(e =>
        {
            // One open need per student/subject/chapter (NULL chapter = the whole subject; SQL Server treats NULLs as equal in a unique index).
            e.HasIndex(x => new { x.StudentId, x.SubjectId, x.ChapterId }).IsUnique().HasFilter("[Status] = 'Open'");
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(R);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.Property(x => x.ReviewNote).HasMaxLength(1000);
        });
        Fk<StudentLearningNeed, Subject>(b, x => x.SubjectId);
        Fk<StudentLearningNeed, Chapter>(b, x => x.ChapterId);
        b.Entity<StudentNote>(e =>
        {
            e.HasIndex(x => new { x.StudentId, x.Kind });
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(R);
            e.Property(x => x.Text).HasMaxLength(2000);
        });
        Fk<StudentNote, Subject>(b, x => x.SubjectId);
        b.Entity<ParentStudentLink>(e =>
        {
            e.HasIndex(x => new { x.ParentId, x.StudentId }).IsUnique();
            e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(R);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(R);
        });

        // ---------- Academics ----------
        b.Entity<AcademicYear>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasMany(x => x.Terms).WithOne().HasForeignKey(x => x.AcademicYearId).OnDelete(R);
            e.ToTable(t => t.HasCheckConstraint("CK_AcademicYears_Dates", "[EndDate] > [StartDate]"));
        });
        b.Entity<GradeLevel>().HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        b.Entity<Curriculum>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.Name).HasMaxLength(100);
        });
        b.Entity<Course>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasOne(x => x.GradeLevel).WithMany().HasForeignKey(x => x.GradeLevelId).OnDelete(R);
            e.HasMany(x => x.Subjects).WithOne(x => x.Course).HasForeignKey(x => x.CourseId).OnDelete(R);
            e.Property(x => x.Description).HasMaxLength(2000);
        });
        Fk<Course, AcademicYear>(b, x => x.AcademicYearId);
        b.Entity<Subject>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasMany(x => x.Chapters).WithOne().HasForeignKey(x => x.SubjectId).OnDelete(R);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.ChoiceGroup).HasMaxLength(Subject.MaxChoiceGroupLength);
            e.HasIndex(x => new { x.TenantId, x.CourseId, x.Kind });
            // A language option always belongs to a choice group; core subjects never do.
            e.ToTable(t => t.HasCheckConstraint("CK_Subjects_ChoiceGroup",
                "([Kind] <> 'LanguageOption' OR [ChoiceGroup] IS NOT NULL) AND ([Kind] <> 'Core' OR [ChoiceGroup] IS NULL)"));
        });
        b.Entity<StudentSubject>(e =>
        {
            e.HasIndex(x => new { x.StudentId, x.SubjectId }).IsUnique();
            e.HasIndex(x => x.SubjectId);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(R);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(R);
        });
        b.Entity<Chapter>().HasMany(x => x.Topics).WithOne().HasForeignKey(x => x.ChapterId).OnDelete(R);
        b.Entity<TeacherSubject>(e =>
        {
            e.HasIndex(x => new { x.TeacherId, x.SubjectId }).IsUnique();
            e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(R);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(R);
        });
        b.Entity<CourseEnrolment>(e =>
        {
            e.HasIndex(x => new { x.CourseId, x.StudentId }).IsUnique();
            e.HasIndex(x => x.StudentId);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(R);
            e.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId).OnDelete(R);
        });
        b.Entity<Batch>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId).OnDelete(R);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(R);
            e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(R);
            e.HasMany(x => x.Enrolments).WithOne(x => x.Batch).HasForeignKey(x => x.BatchId).OnDelete(R);
            e.ToTable(t => t.HasCheckConstraint("CK_Batches_Capacity", "[Capacity] > 0"));
        });
        b.Entity<BatchEnrolment>(e =>
        {
            e.HasIndex(x => new { x.BatchId, x.StudentId }).IsUnique();
            e.HasIndex(x => x.StudentId);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(R);
        });
        b.Entity<TeachingArrangement>(e =>
        {
            e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(R);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(R);
            e.HasMany(x => x.Students).WithOne().HasForeignKey(x => x.ArrangementId).OnDelete(R);
            e.Property(x => x.Notes).HasMaxLength(2000);
        });
        Fk<TeachingArrangement, Course>(b, x => x.CourseId);
        b.Entity<TeachingArrangementStudent>(e =>
        {
            e.HasIndex(x => new { x.ArrangementId, x.StudentId }).IsUnique();
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(R);
        });

        // ---------- Scheduling ----------
        b.Entity<SessionSeries>(e =>
        {
            e.Property(x => x.Days).HasConversion<int>();
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
            e.Property(x => x.MeetingUrl).HasMaxLength(1000);
        });
        Fk<SessionSeries, Teacher>(b, x => x.TeacherId);
        Fk<SessionSeries, Batch>(b, x => x.BatchId);
        Fk<SessionSeries, TeachingArrangement>(b, x => x.ArrangementId);
        Fk<SessionSeries, Course>(b, x => x.CourseId);
        Fk<SessionSeries, Subject>(b, x => x.SubjectId);
        b.Entity<LearningSession>(e =>
        {
            e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(R);
            e.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(R);
            e.HasOne(x => x.Arrangement).WithMany().HasForeignKey(x => x.ArrangementId).OnDelete(R);
            e.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId).OnDelete(R);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(R);
            e.HasOne(x => x.Chapter).WithMany().HasForeignKey(x => x.ChapterId).OnDelete(R);
            e.HasOne<SessionSeries>().WithMany().HasForeignKey(x => x.SeriesId).OnDelete(R);
            e.HasMany(x => x.Participants).WithOne(x => x.Session).HasForeignKey(x => x.SessionId).OnDelete(R);
            e.HasIndex(x => new { x.SeriesId, x.OccurrenceDate }).IsUnique().HasFilter("[SeriesId] IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.StartUtc });
            e.HasIndex(x => new { x.TeacherId, x.StartUtc });
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
            e.Property(x => x.MeetingUrl).HasMaxLength(1000);
            e.Property(x => x.Objectives).HasMaxLength(4000);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Sessions_TimeRange", "[EndUtc] > [StartUtc]");
                // Only batch sessions require a batch; one-to-one and private groups never have one.
                t.HasCheckConstraint("CK_Sessions_BatchRequired", "[SessionType] <> 'Batch' OR [BatchId] IS NOT NULL");
                t.HasCheckConstraint("CK_Sessions_NoBatchForPrivate", "[SessionType] NOT IN ('OneToOne','PrivateGroup') OR [BatchId] IS NULL");
            });
        });
        b.Entity<SessionParticipant>(e =>
        {
            e.HasIndex(x => new { x.SessionId, x.StudentId }).IsUnique();
            e.HasIndex(x => new { x.StudentId, x.Status });
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(R);
        });
        Fk<SessionScheduleChange, LearningSession>(b, x => x.SessionId);
        Fk<SessionScheduleChange, Student>(b, x => x.StudentId);
        Fk<SessionScheduleChange, AppUser>(b, x => x.ChangedByUserId);
        b.Entity<SessionAgendaItem>().HasIndex(x => new { x.SessionId, x.DoubtId }).IsUnique().HasFilter("[DoubtId] IS NOT NULL");
        Fk<SessionAgendaItem, LearningSession>(b, x => x.SessionId);
        Fk<SessionAgendaItem, Doubt>(b, x => x.DoubtId);
        b.Entity<AttendanceRecord>(e =>
        {
            e.HasIndex(x => new { x.SessionId, x.StudentId }).IsUnique();
            e.HasIndex(x => x.StudentId);
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId).OnDelete(R);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(R);
        });
        Fk<AttendanceRecord, AppUser>(b, x => x.MarkedByUserId);
        Fk<TeacherAvailability, Teacher>(b, x => x.TeacherId);
        b.Entity<TeacherAvailability>().ToTable(t => t.HasCheckConstraint("CK_Availability_Range", "[EndLocalTime] > [StartLocalTime]"));

        // ---------- Content ----------
        b.Entity<StoredFile>(e =>
        {
            e.HasIndex(x => x.StorageKey).IsUnique();
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.ContentType).HasMaxLength(150);
        });
        Fk<StoredFile, AppUser>(b, x => x.UploadedByUserId);
        Max<SessionNote>(b, x => x.BodyMarkdown);
        Fk<SessionNote, LearningSession>(b, x => x.SessionId);
        Fk<SessionNote, AppUser>(b, x => x.AuthorUserId);
        Fk<SessionNote, AiLessonDraft>(b, x => x.AiDraftId);
        b.Entity<LearningMaterial>(e =>
        {
            e.HasOne(x => x.File).WithMany().HasForeignKey(x => x.FileId).OnDelete(R);
            e.Property(x => x.Url).HasMaxLength(1000);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.ToTable(t => t.HasCheckConstraint("CK_Materials_Kind", "([Kind] = 'File' AND [FileId] IS NOT NULL) OR ([Kind] = 'Link' AND [Url] IS NOT NULL)"));
        });
        Fk<LearningMaterial, LearningSession>(b, x => x.SessionId);
        Fk<LearningMaterial, Subject>(b, x => x.SubjectId);
        Fk<LearningMaterial, Chapter>(b, x => x.ChapterId);
        Fk<LearningMaterial, Batch>(b, x => x.BatchId);
        Fk<LearningMaterial, AppUser>(b, x => x.CreatedByTeacherOrAdminId);
        b.Entity<ClassRecording>().HasOne(x => x.File).WithMany().HasForeignKey(x => x.FileId).OnDelete(R);
        Fk<ClassRecording, LearningSession>(b, x => x.SessionId);
        Fk<ClassRecording, AppUser>(b, x => x.UploadedByUserId);
        b.Entity<PrerecordedVideo>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.HasOne(x => x.File).WithMany().HasForeignKey(x => x.FileId).OnDelete(R);
            e.HasIndex(x => new { x.SessionId, x.DeletedAtUtc });
            e.HasIndex(x => x.FileId).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_PrerecordedVideos_Offset", "[UnlockOffsetMinutes] >= 0 AND [UnlockOffsetMinutes] <= 20160"));
        });
        Fk<PrerecordedVideo, LearningSession>(b, x => x.SessionId);
        Fk<PrerecordedVideo, AppUser>(b, x => x.UploadedByUserId);
        b.Entity<RecordingProgress>().HasIndex(x => new { x.RecordingId, x.StudentId }).IsUnique();
        Fk<RecordingProgress, ClassRecording>(b, x => x.RecordingId);
        Fk<RecordingProgress, Student>(b, x => x.StudentId);
        b.Entity<ConsentRecord>().HasIndex(x => new { x.SessionId, x.StudentId, x.Type, x.RecordedAtUtc });
        Fk<ConsentRecord, LearningSession>(b, x => x.SessionId);
        Fk<ConsentRecord, Student>(b, x => x.StudentId);
        Fk<ConsentRecord, AppUser>(b, x => x.GivenByUserId);
        b.Entity<ClassTranscript>(e =>
        {
            e.HasMany(x => x.Segments).WithOne().HasForeignKey(x => x.TranscriptId).OnDelete(C);
            e.HasOne<ClassTranscript>().WithMany().HasForeignKey(x => x.CorrectsTranscriptId).OnDelete(R);
        });
        Max<ClassTranscript>(b, x => x.FullText);
        Fk<ClassTranscript, LearningSession>(b, x => x.SessionId);
        Fk<ClassTranscript, ClassRecording>(b, x => x.RecordingId);
        b.Entity<TranscriptSegment>().HasIndex(x => new { x.TranscriptId, x.Sequence }).IsUnique();
        b.Entity<TranscriptSegment>().Property(x => x.Text).HasMaxLength(10_000);

        // ---------- Assignments ----------
        b.Entity<Assignment>(e =>
        {
            e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(R);
            e.HasMany(x => x.Recipients).WithOne().HasForeignKey(x => x.AssignmentId).OnDelete(C);
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(x => x.AssignmentId).OnDelete(C);
            e.ToTable(t => t.HasCheckConstraint("CK_Assignments_MaxMarks", "[MaxMarks] > 0"));
        });
        Max<Assignment>(b, x => x.InstructionsMarkdown);
        Fk<Assignment, LearningSession>(b, x => x.SessionId);
        Fk<Assignment, Batch>(b, x => x.BatchId);
        Fk<Assignment, Subject>(b, x => x.SubjectId);
        b.Entity<AssignmentRecipient>(e =>
        {
            e.HasIndex(x => new { x.AssignmentId, x.StudentId }).IsUnique();
            e.HasIndex(x => x.StudentId);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(R);
        });
        b.Entity<AssignmentAttachment>().HasIndex(x => new { x.AssignmentId, x.FileId }).IsUnique();
        Fk<AssignmentAttachment, StoredFile>(b, x => x.FileId);
        b.Entity<AssignmentSubmission>(e =>
        {
            e.HasIndex(x => new { x.AssignmentId, x.StudentId }).IsUnique();
            e.HasOne(x => x.Assignment).WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(R);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(R);
            e.HasMany(x => x.Files).WithOne().HasForeignKey(x => x.SubmissionId).OnDelete(C);
            e.ToTable(t => t.HasCheckConstraint("CK_Submissions_Marks", "[Marks] IS NULL OR [Marks] >= 0"));
        });
        Max<AssignmentSubmission>(b, x => x.TextAnswer, x => x.Feedback);
        Fk<AssignmentSubmission, AppUser>(b, x => x.GradedByUserId);
        b.Entity<SubmissionFile>().HasIndex(x => new { x.SubmissionId, x.FileId }).IsUnique();
        Fk<SubmissionFile, StoredFile>(b, x => x.FileId);

        // ---------- Assessments ----------
        b.Entity<Question>(e =>
        {
            e.HasMany(x => x.Options).WithOne().HasForeignKey(x => x.QuestionId).OnDelete(C);
            e.Property(x => x.NumericAnswer).HasPrecision(18, 6);
            e.Property(x => x.NumericTolerance).HasPrecision(18, 6);
            e.Property(x => x.AcceptedAnswers).HasMaxLength(1000);
            e.HasIndex(x => new { x.TenantId, x.SubjectId });
        });
        Max<Question>(b, x => x.Stem, x => x.Explanation);
        Fk<Question, Subject>(b, x => x.SubjectId);
        Fk<Question, Chapter>(b, x => x.ChapterId);
        b.Entity<QuestionOption>().Property(x => x.Text).HasMaxLength(1000);
        b.Entity<QuestionOption>().Property(x => x.MatchText).HasMaxLength(1000);
        b.Entity<Exam>(e =>
        {
            e.HasMany(x => x.Questions).WithOne().HasForeignKey(x => x.ExamId).OnDelete(C);
            e.HasMany(x => x.Recipients).WithOne().HasForeignKey(x => x.ExamId).OnDelete(C);
            e.Property(x => x.Instructions).HasMaxLength(10_000);
            e.ToTable(t => t.HasCheckConstraint("CK_Exams_Window", "[ClosesAtUtc] > [OpensAtUtc] AND [DurationMinutes] > 0 AND [MaxAttempts] >= 1"));
        });
        Fk<Exam, Teacher>(b, x => x.TeacherId);
        Fk<Exam, Subject>(b, x => x.SubjectId);
        Fk<Exam, GradeLevel>(b, x => x.GradeLevelId);
        Fk<Exam, Curriculum>(b, x => x.CurriculumId);
        Fk<Question, StoredFile>(b, x => x.ImageFileId);
        Fk<ExamAttempt, AppUser>(b, x => x.EnteredByUserId);
        Fk<Exam, LearningSession>(b, x => x.SessionId);
        Fk<Exam, Batch>(b, x => x.BatchId);
        b.Entity<ExamQuestion>(e =>
        {
            e.HasIndex(x => new { x.ExamId, x.QuestionId }).IsUnique();
            e.HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId).OnDelete(R);
        });
        b.Entity<ExamRecipient>().HasIndex(x => new { x.ExamId, x.StudentId }).IsUnique();
        Fk<ExamRecipient, Student>(b, x => x.StudentId);
        b.Entity<ExamAttempt>(e =>
        {
            e.HasIndex(x => new { x.ExamId, x.StudentId, x.AttemptNumber }).IsUnique();
            e.HasOne(x => x.Exam).WithMany().HasForeignKey(x => x.ExamId).OnDelete(R);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(R);
            e.HasMany(x => x.Answers).WithOne().HasForeignKey(x => x.AttemptId).OnDelete(C);
            e.Property(x => x.QuestionOrder).HasColumnType("nvarchar(max)");
            e.ToTable(t => t.HasCheckConstraint("CK_Attempts_Number", "[AttemptNumber] >= 1"));
        });
        b.Entity<ExamAnswer>().HasIndex(x => new { x.AttemptId, x.ExamQuestionId }).IsUnique();
        Max<ExamAnswer>(b, x => x.ResponseJson, x => x.Feedback);
        Fk<ExamAnswer, ExamQuestion>(b, x => x.ExamQuestionId);
        Fk<ExamAnswer, AppUser>(b, x => x.GradedByUserId);

        // ---------- Doubts ----------
        b.Entity<Doubt>(e =>
        {
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(R);
            e.HasOne(x => x.AssignedTeacher).WithMany().HasForeignKey(x => x.AssignedTeacherId).OnDelete(R);
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.DoubtId).OnDelete(R);
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasIndex(x => new { x.AssignedTeacherId, x.Status });
            e.HasIndex(x => new { x.StudentId, x.Status });
        });
        Max<Doubt>(b, x => x.Description);
        Fk<Doubt, Subject>(b, x => x.SubjectId);
        Fk<Doubt, Chapter>(b, x => x.ChapterId);
        Fk<Doubt, LearningSession>(b, x => x.SessionId);
        Fk<Doubt, ClassRecording>(b, x => x.RecordingId);
        b.Entity<Doubt>().HasOne<LearningSession>().WithMany().HasForeignKey(x => x.FollowUpSessionId).OnDelete(R);
        b.Entity<DoubtMessage>().HasMany(x => x.Attachments).WithOne().HasForeignKey(x => x.MessageId).OnDelete(R);
        Max<DoubtMessage>(b, x => x.Body);
        Fk<DoubtMessage, AppUser>(b, x => x.AuthorUserId);
        Fk<DoubtAttachment, Doubt>(b, x => x.DoubtId);
        Fk<DoubtAttachment, StoredFile>(b, x => x.FileId);
        Fk<DoubtStatusChange, Doubt>(b, x => x.DoubtId);
        Fk<DoubtStatusChange, AppUser>(b, x => x.ChangedByUserId);

        // ---------- Communication ----------
        b.Entity<Conversation>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.DirectKey }).IsUnique().HasFilter("[DirectKey] IS NOT NULL");
            e.HasMany(x => x.Participants).WithOne().HasForeignKey(x => x.ConversationId).OnDelete(C);
        });
        b.Entity<ConversationParticipant>().HasIndex(x => new { x.ConversationId, x.UserId }).IsUnique();
        b.Entity<ConversationParticipant>().HasIndex(x => x.UserId);
        Fk<ConversationParticipant, AppUser>(b, x => x.UserId);
        b.Entity<Message>().HasIndex(x => new { x.ConversationId, x.CreatedAtUtc });
        b.Entity<Message>().Property(x => x.Body).HasMaxLength(5000);
        Fk<Message, Conversation>(b, x => x.ConversationId);
        Fk<Message, AppUser>(b, x => x.SenderUserId);
        Fk<Message, StoredFile>(b, x => x.AttachmentFileId);
        Max<Announcement>(b, x => x.Body);
        Fk<Announcement, Batch>(b, x => x.BatchId);
        Fk<Announcement, AppUser>(b, x => x.AuthorUserId);
        b.Entity<Notification>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.DedupeKey }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.ReadAtUtc, x.CreatedAtUtc });
            e.Property(x => x.Body).HasMaxLength(1000);
            e.Property(x => x.DedupeKey).HasMaxLength(200);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(C);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(R);
        });
        b.Entity<NotificationDelivery>().HasIndex(x => new { x.NotificationId, x.Channel }).IsUnique();
        b.Entity<NotificationDelivery>().HasIndex(x => new { x.Status, x.CreatedAtUtc });
        Fk<NotificationDelivery, Notification>(b, x => x.NotificationId, C);
        b.Entity<NotificationPreference>().HasIndex(x => x.UserId).IsUnique();
        Fk<NotificationPreference, AppUser>(b, x => x.UserId, C);
        b.Entity<DeviceRegistration>().HasIndex(x => x.Token).IsUnique();
        Fk<DeviceRegistration, AppUser>(b, x => x.UserId, C);

        // ---------- AI ----------
        b.Entity<AiProcessingJob>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.SessionId, x.CreatedAtUtc });
            e.Property(x => x.FailureReason).HasMaxLength(1000);
        });
        Fk<AiProcessingJob, LearningSession>(b, x => x.SessionId);
        Fk<AiProcessingJob, ClassRecording>(b, x => x.RecordingId);
        Fk<AiProcessingJob, ClassTranscript>(b, x => x.TranscriptId);
        Fk<AiProcessingJob, AiLessonDraft>(b, x => x.DraftId);
        Fk<AiProcessingJob, AppUser>(b, x => x.RequestedByUserId);
        b.Entity<AiLessonDraft>().HasMany(x => x.Versions).WithOne().HasForeignKey(x => x.DraftId).OnDelete(C);
        Max<AiLessonDraft>(b, x => x.ContentJson);
        Fk<AiLessonDraft, LearningSession>(b, x => x.SessionId);
        Fk<AiLessonDraft, ClassTranscript>(b, x => x.TranscriptId);
        Fk<AiLessonDraft, AiProcessingJob>(b, x => x.JobId);
        Fk<AiLessonDraft, AppUser>(b, x => x.ReviewedByUserId);
        Fk<AiLessonDraft, SessionNote>(b, x => x.PublishedNoteId);
        b.Entity<AiLessonDraftVersion>().HasIndex(x => new { x.DraftId, x.VersionNumber }).IsUnique();
        Max<AiLessonDraftVersion>(b, x => x.ContentJson);
        Fk<AiLessonDraftVersion, AppUser>(b, x => x.EditedByUserId);
        b.Entity<AiUsageRecord>().Property(x => x.EstimatedCostUsd).HasPrecision(18, 6);
        Fk<AiUsageRecord, AiProcessingJob>(b, x => x.JobId);
    }
}
