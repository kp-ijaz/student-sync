namespace StudentSync.Domain.Academics;

/// <summary>
/// The single rule for which subjects a student takes:
/// the active Core subjects of the courses they are actively enrolled in, plus every active subject explicitly given to them
/// (<see cref="StudentSubject"/>: language choices and additional subjects). Language options and additional subjects that were
/// not given to the student are never included. Written as a query so EF Core translates it and LINQ-to-objects can test it.
/// </summary>
public static class StudentCurriculum
{
    public const string CoreKind = "core";
    public const string LanguageKind = "language";
    public const string AdditionalKind = "additional";

    public static IQueryable<Subject> SubjectsOf(Guid studentId, IQueryable<Subject> subjects, IQueryable<CourseEnrolment> enrolments, IQueryable<StudentSubject> assigned) =>
        subjects.Where(s => s.Status == CatalogStatus.Active && (
            (s.Kind == SubjectKind.Core && enrolments.Any(e => e.StudentId == studentId && e.CourseId == s.CourseId && e.Status == EnrolmentStatus.Active))
            || assigned.Any(a => a.StudentId == studentId && a.SubjectId == s.Id)));

    /// <summary>
    /// How a subject in the student's list reaches them: "core" (core subject of an enrolled course), "language" (a language
    /// option of an enrolled course chosen for them) or "additional" (anything else assigned to them, from any course).
    /// </summary>
    public static string KindFor(SubjectKind kind, bool enrolledInSubjectCourse) => (kind, enrolledInSubjectCourse) switch
    {
        (SubjectKind.Core, true) => CoreKind,
        (SubjectKind.LanguageOption, true) => LanguageKind,
        _ => AdditionalKind,
    };

    /// <summary>The catalogue kind as shown to staff (no per-student context).</summary>
    public static string KindFor(SubjectKind kind) => kind switch
    {
        SubjectKind.Core => CoreKind,
        SubjectKind.LanguageOption => LanguageKind,
        _ => AdditionalKind,
    };

    public static int SortRank(string kind) => kind switch { CoreKind => 0, LanguageKind => 1, _ => 2 };
}
