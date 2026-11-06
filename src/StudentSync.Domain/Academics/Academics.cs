using StudentSync.Domain.Common;
using StudentSync.Domain.People;

namespace StudentSync.Domain.Academics;

public enum CatalogStatus { Draft, Active, Archived }
public enum EnrolmentStatus { Active, Completed, Withdrawn }

/// <summary>
/// How a subject reaches students. Core: every student actively enrolled in the subject's course takes it.
/// LanguageOption: one option of a choice group (e.g. "Second language"); a student takes the option chosen for them.
/// Additional: only students it is explicitly assigned to (extra coaching, electives).
/// </summary>
public enum SubjectKind { Core, LanguageOption, Additional }

public class AcademicYear : TenantEntity
{
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public List<Term> Terms { get; set; } = [];
}

public class Term : TenantEntity
{
    public Guid AcademicYearId { get; set; }
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}

public class GradeLevel : TenantEntity
{
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

public class Course : TenantEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid? GradeLevelId { get; set; }
    public GradeLevel? GradeLevel { get; set; }
    public Guid? AcademicYearId { get; set; }
    public CatalogStatus Status { get; set; } = CatalogStatus.Active;
    public List<Subject> Subjects { get; set; } = [];
}

public class Subject : TenantEntity
{
    public Guid CourseId { get; set; }
    public Course? Course { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public CatalogStatus Status { get; set; } = CatalogStatus.Active;
    public SubjectKind Kind { get; set; } = SubjectKind.Core;
    /// <summary>Choice group a language option belongs to (e.g. "Second language"); an optional label for additional subjects; null for core subjects.</summary>
    public string? ChoiceGroup { get; set; }
    public List<Chapter> Chapters { get; set; } = [];

    public const int MaxChoiceGroupLength = 60;

    /// <summary>Normalises and validates kind/choice group: language options need a group; core subjects never have one.</summary>
    public void SetKind(SubjectKind kind, string? choiceGroup)
    {
        if (!Enum.IsDefined(kind)) throw new DomainException("Choose a valid subject kind.", "subject_kind");
        var group = string.IsNullOrWhiteSpace(choiceGroup) ? null : choiceGroup.Trim();
        if (group is { Length: > MaxChoiceGroupLength }) throw new DomainException($"The choice group can be at most {MaxChoiceGroupLength} characters.", "choice_group");
        if (kind == SubjectKind.LanguageOption && group is null)
            throw new DomainException("A language option needs a choice group, for example \"Second language\".", "choice_group_required");
        Kind = kind;
        ChoiceGroup = kind == SubjectKind.Core ? null : group;
    }
}

/// <summary>
/// A subject explicitly given to one student: a language choice (a LanguageOption subject of one of their courses) or an
/// additional subject from any course. Core subjects come from course enrolment and are never stored here.
/// </summary>
public class StudentSubject : TenantEntity
{
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public Guid SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public Guid? AddedByUserId { get; set; }
}

public class Chapter : TenantEntity
{
    public Guid SubjectId { get; set; }
    public string Title { get; set; } = "";
    public int SortOrder { get; set; }
    public List<Topic> Topics { get; set; } = [];
}

public class Topic : TenantEntity
{
    public Guid ChapterId { get; set; }
    public string Title { get; set; } = "";
    public int SortOrder { get; set; }
}

/// <summary>Authorises a teacher to teach a subject.</summary>
public class TeacherSubject : TenantEntity
{
    public Guid TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public Guid SubjectId { get; set; }
    public Subject? Subject { get; set; }
}

public class CourseEnrolment : TenantEntity
{
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public Guid CourseId { get; set; }
    public Course? Course { get; set; }
    public EnrolmentStatus Status { get; set; } = EnrolmentStatus.Active;
    public DateTime EnrolledAtUtc { get; set; }
}

/// <summary>A predefined cohort. Batches are optional: sessions never require one.</summary>
public class Batch : TenantEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid CourseId { get; set; }
    public Course? Course { get; set; }
    public Guid? SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public Guid TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public int Capacity { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public CatalogStatus Status { get; set; } = CatalogStatus.Active;
    public List<BatchEnrolment> Enrolments { get; set; } = [];
}

public class BatchEnrolment : TenantEntity
{
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public EnrolmentStatus Status { get; set; } = EnrolmentStatus.Active;
    public DateTime JoinedAtUtc { get; set; }
    public DateTime? LeftAtUtc { get; set; }
}

public enum ArrangementType { OneToOne, PrivateGroup }

/// <summary>
/// An ongoing private tuition relationship that is not a batch: one-to-one (exactly one student)
/// or a small private group. Sessions may reference it; it is never required.
/// </summary>
public class TeachingArrangement : TenantEntity
{
    public ArrangementType Type { get; set; }
    public string Title { get; set; } = "";
    public Guid TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public Guid? CourseId { get; set; }
    public Guid? SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public CatalogStatus Status { get; set; } = CatalogStatus.Active;
    public DateOnly StartDate { get; set; }
    public string? Notes { get; set; }
    public List<TeachingArrangementStudent> Students { get; set; } = [];

    public void ValidateStudentCount(int activeStudents)
    {
        if (Type == ArrangementType.OneToOne && activeStudents != 1)
            throw new DomainException("A one-to-one arrangement must have exactly one student.", "one_to_one_student_count");
        if (Type == ArrangementType.PrivateGroup && activeStudents < 1)
            throw new DomainException("A private group needs at least one student.", "group_student_count");
    }
}

public class TeachingArrangementStudent : TenantEntity
{
    public Guid ArrangementId { get; set; }
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public EnrolmentStatus Status { get; set; } = EnrolmentStatus.Active;
}

/// <summary>A syllabus/board the institute teaches (NCERT, Kerala State, …). Configurable by admins; referenced by students and, later, books.</summary>
public class Curriculum : TenantEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public CatalogStatus Status { get; set; } = CatalogStatus.Active;
}
