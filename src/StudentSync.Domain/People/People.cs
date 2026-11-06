using StudentSync.Domain.Common;
using StudentSync.Domain.Identity;

namespace StudentSync.Domain.People;

/// <summary>Admin-controlled states. Only Active people can sign in; Suspended marks access withdrawn by the institute (addendum B).</summary>
public enum PersonStatus { Active, Inactive, Archived, Suspended }

/// <summary>Teacher profile. Shares its primary key with the <see cref="AppUser"/> account (1:1).</summary>
public class Teacher : TenantEntity
{
    public AppUser? User { get; set; }
    public string? EmployeeCode { get; set; }
    public string? Bio { get; set; }
    public string? Specialisations { get; set; }
    public PersonStatus Status { get; set; } = PersonStatus.Active;
}

/// <summary>Student profile. Shares its primary key with the <see cref="AppUser"/> account (1:1).
/// Only data needed for tuition operations is collected (no date of birth, address, etc.).</summary>
public class Student : TenantEntity
{
    public AppUser? User { get; set; }
    public string StudentCode { get; set; } = "";
    public Guid? GradeLevelId { get; set; }
    public string? SchoolName { get; set; }
    /// <summary>Syllabus the student follows (NCERT, Kerala State, …); optional until the admin records it.</summary>
    public Guid? CurriculumId { get; set; }
    public Guid? AcademicYearId { get; set; }
    public PersonStatus Status { get; set; } = PersonStatus.Active;
}

public class Parent : TenantEntity
{
    public AppUser? User { get; set; }
    public PersonStatus Status { get; set; } = PersonStatus.Active;
}

public enum GuardianRelationship { Mother, Father, Guardian, Other }

/// <summary>Verified parent/guardian ↔ child relationship. Parents only ever see children linked here.</summary>
public class ParentStudentLink : TenantEntity
{
    public Guid ParentId { get; set; }
    public Parent? Parent { get; set; }
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public GuardianRelationship Relationship { get; set; }
    public bool IsVerified { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public Guid? VerifiedByUserId { get; set; }
}

/// <summary>Who told the institute that a subject or chapter is difficult for the student (addendum C).</summary>
public enum LearningNeedSource { Student, Parent, Admin, Teacher, Diagnostic }
public enum LearningNeedStatus { Open, Resolved }

/// <summary>
/// A weak subject (optionally narrowed to a chapter) recorded for a student. Works without any diagnostic test.
/// Needs are evidence for a learning plan, never a permanent label: staff resolve them with a note, keeping the history.
/// </summary>
public class StudentLearningNeed : TenantEntity
{
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public Guid SubjectId { get; set; }
    public Guid? ChapterId { get; set; }
    public LearningNeedSource Source { get; set; }
    public string? Note { get; set; }
    public LearningNeedStatus Status { get; set; } = LearningNeedStatus.Open;
    public Guid RecordedByUserId { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string? ReviewNote { get; set; }
}

/// <summary>Goal: shown to the student as part of their learning plan. Observation: staff-only teacher/admin notes.</summary>
public enum StudentNoteKind { Goal, Observation }

public class StudentNote : TenantEntity
{
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public StudentNoteKind Kind { get; set; }
    public Guid? SubjectId { get; set; }
    public string Text { get; set; } = "";
    public Guid AuthorUserId { get; set; }
}
