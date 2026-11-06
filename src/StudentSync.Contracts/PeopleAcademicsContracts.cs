using StudentSync.Domain.Academics;
using StudentSync.Domain.People;

namespace StudentSync.Contracts;

// ---------- People ----------

public sealed record PersonQuery : PageQuery
{
    public PersonStatus? Status { get; init; }
}

public sealed record TeacherDto(Guid Id, string FullName, string Email, string? EmployeeCode, string? Specialisations, string? Bio, PersonStatus Status, string TimeZoneId, IReadOnlyList<LookupItem> Subjects, string? PhotoUrl = null);
public sealed record CreateTeacherRequest(string FullName, string Email, string? EmployeeCode, string? Specialisations, string? Bio, string? TimeZoneId);
public sealed record UpdateTeacherRequest(string FullName, string? EmployeeCode, string? Specialisations, string? Bio, PersonStatus Status, string TimeZoneId);

/// <summary>PhotoUrl is only returned to the student, admins and teachers who teach them — never to other students or parents.</summary>
public sealed record StudentDto(Guid Id, string FullName, string Email, string StudentCode, Guid? GradeLevelId, string? GradeLevelName, string? SchoolName, PersonStatus Status, string TimeZoneId, string? PhotoUrl = null,
    Guid? CurriculumId = null, string? CurriculumName = null, Guid? AcademicYearId = null, string? AcademicYearName = null);
public sealed record CreateStudentRequest(string FullName, string Email, string StudentCode, Guid? GradeLevelId, string? SchoolName, string? TimeZoneId, Guid? CurriculumId = null, Guid? AcademicYearId = null);
public sealed record UpdateStudentRequest(string FullName, string StudentCode, Guid? GradeLevelId, string? SchoolName, PersonStatus Status, string TimeZoneId, Guid? CurriculumId = null, Guid? AcademicYearId = null);

public sealed record ParentDto(Guid Id, string FullName, string Email, PersonStatus Status, IReadOnlyList<ChildLinkDto> Children);
public sealed record ChildLinkDto(Guid LinkId, Guid StudentId, string StudentName, GuardianRelationship Relationship, bool IsVerified);
public sealed record CreateParentRequest(string FullName, string Email, string? TimeZoneId);
public sealed record LinkChildRequest(Guid StudentId, GuardianRelationship Relationship, bool Verified);

public sealed record CreatedPersonResponse(Guid Id, string TemporaryPassword);
/// <summary>Returned once by an admin password reset; the plaintext is never stored.</summary>
public sealed record TemporaryPasswordResponse(string TemporaryPassword);

public sealed record StudentProfileDto(
    StudentDto Student,
    IReadOnlyList<LookupItem> Courses,
    IReadOnlyList<LookupItem> Batches,
    IReadOnlyList<LookupItem> Arrangements,
    IReadOnlyList<LookupItem> Parents,
    ProgressDto Progress);

// ---------- Academics ----------

public sealed record AcademicYearDto(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, bool IsCurrent, IReadOnlyList<TermDto> Terms);
public sealed record TermDto(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate);
public sealed record SaveAcademicYearRequest(string Name, DateOnly StartDate, DateOnly EndDate, bool IsCurrent);
public sealed record SaveTermRequest(string Name, DateOnly StartDate, DateOnly EndDate);

public sealed record GradeLevelDto(Guid Id, string Name, int SortOrder);
public sealed record SaveGradeLevelRequest(string Name, int SortOrder);

public sealed record CurriculumDto(Guid Id, string Code, string Name, CatalogStatus Status, int StudentCount);
public sealed record SaveCurriculumRequest(string Code, string Name, CatalogStatus Status);

public sealed record CourseDto(Guid Id, string Code, string Name, string? Description, Guid? GradeLevelId, string? GradeLevelName, CatalogStatus Status, int SubjectCount, int StudentCount);
public sealed record SaveCourseRequest(string Code, string Name, string? Description, Guid? GradeLevelId, Guid? AcademicYearId, CatalogStatus Status);

public sealed record SubjectDto(Guid Id, Guid CourseId, string CourseName, string Code, string Name, string? Description, CatalogStatus Status, IReadOnlyList<ChapterDto> Chapters, IReadOnlyList<LookupItem> Teachers,
    SubjectKind Kind = SubjectKind.Core, string? ChoiceGroup = null);
/// <summary>
/// Kind: omitted/null keeps the current kind and group on update (Core on create). ChoiceGroup is required for LanguageOption
/// (e.g. "Second language"), an optional label for Additional, and ignored for Core.
/// </summary>
public sealed record SaveSubjectRequest(Guid CourseId, string Code, string Name, string? Description, CatalogStatus Status,
    SubjectKind? Kind = null, string? ChoiceGroup = null);
public sealed record ChapterDto(Guid Id, string Title, int SortOrder, IReadOnlyList<TopicDto> Topics);
public sealed record TopicDto(Guid Id, string Title, int SortOrder);
public sealed record SaveChapterRequest(string Title, int SortOrder);
public sealed record SaveTopicRequest(string Title, int SortOrder);
public sealed record AssignTeacherRequest(Guid TeacherId);

public sealed record EnrolStudentRequest(Guid StudentId);
public sealed record CourseEnrolmentDto(Guid Id, Guid StudentId, string StudentName, Guid CourseId, string CourseName, EnrolmentStatus Status, DateTime EnrolledAtUtc);

public sealed record BatchDto(
    Guid Id, string Code, string Name, Guid CourseId, string CourseName, Guid? SubjectId, string? SubjectName,
    Guid TeacherId, string TeacherName, int Capacity, int ActiveStudents, DateOnly StartDate, DateOnly? EndDate, CatalogStatus Status);
public sealed record SaveBatchRequest(string Code, string Name, Guid CourseId, Guid? SubjectId, Guid TeacherId, int Capacity, DateOnly StartDate, DateOnly? EndDate, CatalogStatus Status);
public sealed record BatchStudentDto(Guid EnrolmentId, Guid StudentId, string StudentName, string StudentCode, EnrolmentStatus Status, DateTime JoinedAtUtc);

public sealed record ArrangementDto(
    Guid Id, ArrangementType Type, string Title, Guid TeacherId, string TeacherName, Guid? SubjectId, string? SubjectName,
    CatalogStatus Status, DateOnly StartDate, string? Notes, IReadOnlyList<LookupItem> Students);
public sealed record SaveArrangementRequest(ArrangementType Type, string Title, Guid TeacherId, Guid? CourseId, Guid? SubjectId, DateOnly StartDate, string? Notes, List<Guid> StudentIds, CatalogStatus Status);

// ---------- A student's subjects (curriculum) ----------

public sealed record SubjectSummaryDto(Guid Id, string Code, string Name, Guid CourseId, string CourseName, SubjectKind Kind, string? ChoiceGroup, CatalogStatus Status);
/// <summary>The language options of one choice group in one of the student's courses, and the option(s) chosen for the student.</summary>
public sealed record SubjectChoiceGroupDto(string Group, IReadOnlyList<SubjectSummaryDto> Options, IReadOnlyList<Guid> ChosenSubjectIds, Guid CourseId, string CourseName);
/// <summary>
/// Core: core subjects of the student's active courses (read-only here). ChoiceGroups: language options of those courses.
/// Additional: subjects assigned to the student that are not language choices of their courses.
/// </summary>
public sealed record StudentSubjectsDto(IReadOnlyList<SubjectSummaryDto> Core, IReadOnlyList<SubjectChoiceGroupDto> ChoiceGroups, IReadOnlyList<SubjectSummaryDto> Additional);
public sealed record SubjectChoiceRequest(string Group, Guid SubjectId);
/// <summary>Replaces all language choices and additional subjects of a student. At most one choice per group and course.</summary>
public sealed record SaveStudentSubjectsRequest(List<SubjectChoiceRequest>? Choices, List<Guid>? AdditionalSubjectIds);

// ---------- Learning profile: weak subjects (learning needs), goals and observations (addendum C) ----------

public sealed record LearningNeedDto(Guid Id, Guid SubjectId, string SubjectName, Guid? ChapterId, string? ChapterTitle, LearningNeedSource Source, string? Note,
    LearningNeedStatus Status, DateTime RecordedAtUtc, string RecordedBy, DateTime? ResolvedAtUtc, string? ResolvedBy, string? ReviewNote);
/// <summary>Source is ignored for students (always Student). Chapter is optional and must belong to the subject.</summary>
public sealed record AddLearningNeedRequest(Guid SubjectId, Guid? ChapterId, LearningNeedSource? Source, string? Note);
public sealed record ResolveLearningNeedRequest(string? ReviewNote);
public sealed record LearningNeedQuery(bool IncludeResolved = false);

public sealed record StudentNoteDto(Guid Id, StudentNoteKind Kind, string Text, Guid? SubjectId, string? SubjectName, string AuthorName, DateTime CreatedAtUtc, bool CanDelete);
public sealed record AddStudentNoteRequest(StudentNoteKind Kind, string Text, Guid? SubjectId);

/// <summary>The student's own view: syllabus, grade, year, open weak subjects and goals (no staff observations).</summary>
public sealed record LearningPlanDto(string? CurriculumName, string? GradeLevelName, string? AcademicYearName, IReadOnlyList<LearningNeedDto> Needs, IReadOnlyList<StudentNoteDto> Goals,
    IReadOnlyList<LookupItem> Subjects);
