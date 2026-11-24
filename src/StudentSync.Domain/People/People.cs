using StudentSync.Domain.Common;
using StudentSync.Domain.Identity;

namespace StudentSync.Domain.People;

/// <summary>
/// Admin-controlled states. Active people can sign in and be booked; Suspended marks access withdrawn by the institute
/// (addendum B). OnHold (students, owner request 2026-11-24) pauses a student — fees unpaid, exams, travel: they keep
/// their sign-in and their records and stay on the fee lists, but no new classes can be booked until they are Active again.
/// </summary>
public enum PersonStatus { Active, Inactive, Archived, Suspended, OnHold }

/// <summary>Teacher profile. Shares its primary key with the <see cref="AppUser"/> account (1:1).</summary>
public class Teacher : TenantEntity
{
    public AppUser? User { get; set; }
    public string? EmployeeCode { get; set; }
    public string? Bio { get; set; }
    public string? Specialisations { get; set; }
    public PersonStatus Status { get; set; } = PersonStatus.Active;
    /// <summary>Contact number the centre uses (WhatsApp etc.). Shown to admins only.</summary>
    public string? Phone { get; set; }
    public int? ExperienceYears { get; set; }
    /// <summary>Languages the teacher can teach in, e.g. "English, Malayalam, Arabic".</summary>
    public string? Languages { get; set; }
    /// <summary>When the teacher joined the centre.</summary>
    public DateOnly? JoinedOn { get; set; }
    public List<TeacherQualification> Qualifications { get; set; } = [];
}

/// <summary>A degree or certificate of a teacher (e.g. "B.Ed", "MSc Mathematics"), in the order the admin entered them.</summary>
public class TeacherQualification : TenantEntity
{
    public Guid TeacherId { get; set; }
    public string Title { get; set; } = "";
    public string? Institution { get; set; }
    public int? Year { get; set; }
    public int SortOrder { get; set; }
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
    /// <summary>Contact details the centre uses (calls/WhatsApp). Parents do not sign in; their contact is kept here. Admins only.</summary>
    public string? Phone { get; set; }
    public string? GuardianName { get; set; }
    public string? GuardianPhone { get; set; }
    /// <summary>When the student joined the tuition centre ("with us for 3 months").</summary>
    public DateOnly? JoinedOn { get; set; }
    /// <summary>The student's fee per hour (institute currency). Prices new hour packages: hours × rate. Admins only.</summary>
    public decimal? HourlyRate { get; set; }
}

/// <summary>
/// A school result the student or parent shared (e.g. "Term 1 2026"): overall percentage, marks per school subject and
/// optionally the scanned progress report. Evidence for the learning plan, recorded by the centre.
/// </summary>
public class StudentSchoolReport : TenantEntity
{
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public string Title { get; set; } = "";
    public DateOnly? ExamDate { get; set; }
    public decimal? OverallPercent { get; set; }
    public string? Notes { get; set; }
    /// <summary>The uploaded report (PDF or photo); access is decided by this report's student.</summary>
    public Guid? FileId { get; set; }
    public Guid RecordedByUserId { get; set; }
    public List<StudentSchoolMark> Marks { get; set; } = [];
}

/// <summary>One school subject's result on a report. School subjects are free text: they need not be tuition subjects.</summary>
public class StudentSchoolMark : TenantEntity
{
    public Guid ReportId { get; set; }
    public string Subject { get; set; } = "";
    public decimal? Marks { get; set; }
    public decimal? MaxMarks { get; set; }
    public string? Grade { get; set; }
    public int SortOrder { get; set; }
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
