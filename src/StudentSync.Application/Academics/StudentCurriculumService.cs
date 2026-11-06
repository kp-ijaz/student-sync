using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Identity;

namespace StudentSync.Application.Academics;

/// <summary>
/// A student's subjects (rule: <see cref="StudentCurriculum"/>) and the admin view/edit of their language choices and
/// additional subjects. Tenant isolation comes from the filtered <see cref="IAppDbContext"/>; ids from another institution are 404.
/// </summary>
public sealed class StudentCurriculumService(IAppDbContext db, ICurrentUser user, AccessPolicy access, AuditService audit)
{
    public const int MaxAssignedSubjects = 100;

    public sealed record StudentSubjectKind(string Kind, string? Group);

    /// <summary>The subjects a student takes.</summary>
    public IQueryable<Subject> SubjectsOf(Guid studentId) => StudentCurriculum.SubjectsOf(studentId, db.Subjects, db.CourseEnrolments, db.StudentSubjects);

    /// <summary>The student's subjects with how each reaches them ("core", "language", "additional") and its choice group.</summary>
    public async Task<Dictionary<Guid, StudentSubjectKind>> KindsAsync(Guid studentId, CancellationToken ct)
    {
        var courses = ActiveCourseIds(studentId);
        var rows = await SubjectsOf(studentId)
            .Select(s => new { s.Id, s.Kind, s.ChoiceGroup, Enrolled = courses.Contains(s.CourseId) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => new StudentSubjectKind(StudentCurriculum.KindFor(r.Kind, r.Enrolled), r.ChoiceGroup));
    }

    private IQueryable<Guid> ActiveCourseIds(Guid studentId) =>
        db.CourseEnrolments.Where(e => e.StudentId == studentId && e.Status == EnrolmentStatus.Active).Select(e => e.CourseId);

    // ---------------- Admin: GET/PUT /students/{id}/subjects ----------------

    private sealed record Row(Guid Id, string Code, string Name, Guid CourseId, string CourseName, SubjectKind Kind, string? ChoiceGroup, CatalogStatus Status)
    {
        public SubjectSummaryDto Dto => new(Id, Code, Name, CourseId, CourseName, Kind, ChoiceGroup, Status);
    }

    private static IQueryable<Row> Project(IQueryable<Subject> q) =>
        q.Select(s => new Row(s.Id, s.Code, s.Name, s.CourseId, s.Course!.Name, s.Kind, s.ChoiceGroup, s.Status));

    private static string GroupKey(string group) => group.Trim().ToUpperInvariant();

    public async Task<StudentSubjectsDto> GetForStudentAsync(Guid studentId, CancellationToken ct)
    {
        access.Require(Permissions.AcademicsManage);
        await EnsureStudentAsync(studentId, ct);
        var courses = await ActiveCourseIds(studentId).ToListAsync(ct);
        var assignedIds = await db.StudentSubjects.Where(x => x.StudentId == studentId).Select(x => x.SubjectId).ToListAsync(ct);
        var courseSubjects = await Project(db.Subjects.Where(s => courses.Contains(s.CourseId)
            && (s.Status == CatalogStatus.Active || assignedIds.Contains(s.Id)))).ToListAsync(ct);
        var assigned = await Project(db.Subjects.Where(s => assignedIds.Contains(s.Id))).ToListAsync(ct);

        static List<SubjectSummaryDto> Sorted(IEnumerable<Row> rows) =>
            rows.OrderBy(r => r.CourseName, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(r => r.Code, StringComparer.Ordinal).Select(r => r.Dto).ToList();

        var core = Sorted(courseSubjects.Where(s => s.Kind == SubjectKind.Core && s.Status == CatalogStatus.Active));
        var groups = courseSubjects.Where(s => s.Kind == SubjectKind.LanguageOption && s.ChoiceGroup is not null)
            .GroupBy(s => (s.CourseId, Key: GroupKey(s.ChoiceGroup!)))
            .Select(g => new SubjectChoiceGroupDto(g.First().ChoiceGroup!.Trim(), Sorted(g),
                g.Where(s => assignedIds.Contains(s.Id)).Select(s => s.Id).ToList(), g.Key.CourseId, g.First().CourseName))
            .OrderBy(g => g.CourseName, StringComparer.CurrentCultureIgnoreCase).ThenBy(g => g.Group, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var additional = Sorted(assigned.Where(s => !(courses.Contains(s.CourseId) && s.Kind == SubjectKind.LanguageOption)));
        return new StudentSubjectsDto(core, groups, additional);
    }

    /// <summary>Replaces the student's language choices and additional subjects in one transaction.</summary>
    public async Task<StudentSubjectsDto> SaveForStudentAsync(Guid studentId, SaveStudentSubjectsRequest r, CancellationToken ct)
    {
        access.Require(Permissions.AcademicsManage);
        await EnsureStudentAsync(studentId, ct);
        var choices = (r.Choices ?? []).Where(c => c is not null).ToList();
        var additionalIds = (r.AdditionalSubjectIds ?? []).Distinct().ToList();
        if (choices.Count + additionalIds.Count > MaxAssignedSubjects) throw RequestValidationException.For("additionalSubjectIds", "Too many subjects.");

        var courses = await ActiveCourseIds(studentId).ToListAsync(ct);
        var existing = await db.StudentSubjects.Where(x => x.StudentId == studentId).ToListAsync(ct);
        var requestedIds = choices.Select(c => c.SubjectId).Concat(additionalIds).Concat(existing.Select(x => x.SubjectId)).Distinct().ToList();
        var subjects = await Project(db.Subjects.Where(s => requestedIds.Contains(s.Id))).ToDictionaryAsync(s => s.Id, ct);

        var errors = new Dictionary<string, string[]>();
        void Error(string field, string message) => errors.TryAdd(field, [message]);

        var chosen = new HashSet<Guid>();
        var perGroup = new HashSet<(Guid, string)>();
        for (var i = 0; i < choices.Count; i++)
        {
            var c = choices[i];
            var field = $"choices[{i}]";
            if (string.IsNullOrWhiteSpace(c.Group)) { Error(field, "Choose the group for this choice."); continue; }
            if (!subjects.TryGetValue(c.SubjectId, out var s)) { Error(field, "Subject not found."); continue; }
            if (s.Kind != SubjectKind.LanguageOption || s.ChoiceGroup is null || GroupKey(s.ChoiceGroup) != GroupKey(c.Group))
            { Error(field, $"{s.Name} is not an option of \"{c.Group.Trim()}\"."); continue; }
            if (!courses.Contains(s.CourseId)) { Error(field, $"{s.Name} belongs to {s.CourseName}, which the student is not enrolled in."); continue; }
            if (s.Status != CatalogStatus.Active && existing.All(x => x.SubjectId != s.Id)) { Error(field, $"{s.Name} is not active."); continue; }
            if (!perGroup.Add((s.CourseId, GroupKey(s.ChoiceGroup)))) { Error(field, $"Choose only one option for \"{s.ChoiceGroup}\" in {s.CourseName}."); continue; }
            chosen.Add(s.Id);
        }
        for (var i = 0; i < additionalIds.Count; i++)
        {
            var field = $"additionalSubjectIds[{i}]";
            if (!subjects.TryGetValue(additionalIds[i], out var s)) { Error(field, "Subject not found."); continue; }
            var ownCourse = courses.Contains(s.CourseId);
            if (s.Kind == SubjectKind.Core && ownCourse) { Error(field, $"{s.Name} is already a core subject of {s.CourseName}."); continue; }
            if (s.Kind == SubjectKind.LanguageOption && ownCourse) { Error(field, $"{s.Name} is a choice of \"{s.ChoiceGroup}\"; choose it there instead."); continue; }
            if (s.Status != CatalogStatus.Active && existing.All(x => x.SubjectId != s.Id)) { Error(field, $"{s.Name} is not active."); continue; }
            if (chosen.Contains(s.Id)) { Error(field, $"{s.Name} is already chosen as a language."); continue; }
        }
        if (errors.Count > 0) throw new RequestValidationException(errors);

        var desired = chosen.Concat(additionalIds).ToHashSet();
        var removed = existing.Where(x => !desired.Contains(x.SubjectId)).ToList();
        var added = desired.Where(id => existing.All(x => x.SubjectId != id)).ToList();
        if (removed.Count + added.Count > 0)
        {
            db.StudentSubjects.RemoveRange(removed);
            foreach (var id in added) db.StudentSubjects.Add(new StudentSubject { StudentId = studentId, SubjectId = id, AddedByUserId = user.UserId });
            string Codes(IEnumerable<Guid> ids) => string.Join(", ", ids.Select(id => subjects.TryGetValue(id, out var s) ? s.Code : id.ToString()));
            audit.Record("student.subjects_updated", "Student", studentId,
                $"added: [{Codes(added)}]; removed: [{Codes(removed.Select(x => x.SubjectId))}]");
            // One SaveChanges is one transaction: all rows change or none do.
            await db.SaveChangesAsync(ct);
        }
        return await GetForStudentAsync(studentId, ct);
    }

    private async Task EnsureStudentAsync(Guid studentId, CancellationToken ct)
    {
        if (!await db.Students.AnyAsync(s => s.Id == studentId, ct)) throw new NotFoundException("Student");
    }
}
