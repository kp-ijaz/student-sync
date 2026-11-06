using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Common;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;

namespace StudentSync.Application.Academics;

public sealed class AcademicsService(IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, AuditService audit)
{
    private void Manage() => access.Require(Permissions.AcademicsManage);
    private void View() => access.Require(Permissions.AcademicsView);

    // ---------------- Years, terms, grades ----------------

    public async Task<IReadOnlyList<AcademicYearDto>> YearsAsync(CancellationToken ct)
    {
        View();
        return await db.AcademicYears.OrderByDescending(y => y.StartDate).Select(y => new AcademicYearDto(y.Id, y.Name, y.StartDate, y.EndDate, y.IsCurrent,
            y.Terms.OrderBy(t => t.StartDate).Select(t => new TermDto(t.Id, t.Name, t.StartDate, t.EndDate)).ToList())).ToListAsync(ct);
    }

    public async Task<Guid> SaveYearAsync(Guid? id, SaveAcademicYearRequest r, CancellationToken ct)
    {
        Manage();
        var name = Text.Required(r.Name, "name", 50);
        if (r.EndDate <= r.StartDate) throw RequestValidationException.For("endDate", "The year must end after it starts.");
        if (await db.AcademicYears.AnyAsync(y => y.Name == name && y.Id != id, ct)) throw RequestValidationException.For("name", "An academic year with this name exists.");
        var y = id is { } yid ? await db.AcademicYears.FirstOrDefaultAsync(x => x.Id == yid, ct) ?? throw new NotFoundException("Academic year") : db.AcademicYears.Add(new AcademicYear()).Entity;
        y.Name = name; y.StartDate = r.StartDate; y.EndDate = r.EndDate;
        if (r.IsCurrent) await db.AcademicYears.Where(x => x.IsCurrent && x.Id != y.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsCurrent, false), ct);
        y.IsCurrent = r.IsCurrent;
        audit.Record("academic_year.saved", "AcademicYear", y.Id, name);
        await db.SaveChangesAsync(ct);
        return y.Id;
    }

    public async Task<Guid> AddTermAsync(Guid yearId, SaveTermRequest r, CancellationToken ct)
    {
        Manage();
        var y = await db.AcademicYears.Include(x => x.Terms).FirstOrDefaultAsync(x => x.Id == yearId, ct) ?? throw new NotFoundException("Academic year");
        if (r.StartDate < y.StartDate || r.EndDate > y.EndDate || r.EndDate <= r.StartDate)
            throw RequestValidationException.For("startDate", "Terms must fall within the academic year.");
        if (y.Terms.Any(t => t.StartDate < r.EndDate && r.StartDate < t.EndDate)) throw RequestValidationException.For("startDate", "Terms cannot overlap.");
        var term = new Term { AcademicYearId = y.Id, Name = Text.Required(r.Name, "name", 50), StartDate = r.StartDate, EndDate = r.EndDate };
        db.Terms.Add(term);
        await db.SaveChangesAsync(ct);
        return term.Id;
    }

    public async Task<IReadOnlyList<GradeLevelDto>> GradesAsync(CancellationToken ct)
    {
        View();
        return await db.GradeLevels.OrderBy(g => g.SortOrder).Select(g => new GradeLevelDto(g.Id, g.Name, g.SortOrder)).ToListAsync(ct);
    }

    public async Task<Guid> SaveGradeAsync(Guid? id, SaveGradeLevelRequest r, CancellationToken ct)
    {
        Manage();
        var name = Text.Required(r.Name, "name", 50);
        if (await db.GradeLevels.AnyAsync(g => g.Name == name && g.Id != id, ct)) throw RequestValidationException.For("name", "This grade already exists.");
        var g = id is { } gid ? await db.GradeLevels.FirstOrDefaultAsync(x => x.Id == gid, ct) ?? throw new NotFoundException("Grade") : db.GradeLevels.Add(new GradeLevel()).Entity;
        g.Name = name; g.SortOrder = r.SortOrder;
        await db.SaveChangesAsync(ct);
        return g.Id;
    }

    // ---------------- Curricula (syllabi) ----------------

    public async Task<IReadOnlyList<CurriculumDto>> CurriculaAsync(CancellationToken ct)
    {
        View();
        return await db.Curricula.OrderBy(c => c.Name)
            .Select(c => new CurriculumDto(c.Id, c.Code, c.Name, c.Status, db.Students.Count(s => s.CurriculumId == c.Id)))
            .ToListAsync(ct);
    }

    public async Task<Guid> SaveCurriculumAsync(Guid? id, SaveCurriculumRequest r, CancellationToken ct)
    {
        Manage();
        var code = Text.Required(r.Code, "code", 30).ToUpperInvariant();
        var name = Text.Required(r.Name, "name", 100);
        if (await db.Curricula.AnyAsync(c => c.Code == code && c.Id != id, ct)) throw RequestValidationException.For("code", "This curriculum code already exists.");
        if (await db.Curricula.AnyAsync(c => c.Name == name && c.Id != id, ct)) throw RequestValidationException.For("name", "This curriculum already exists.");
        var c = id is { } cid ? await db.Curricula.FirstOrDefaultAsync(x => x.Id == cid, ct) ?? throw new NotFoundException("Curriculum") : db.Curricula.Add(new Curriculum()).Entity;
        c.Code = code; c.Name = name; c.Status = r.Status;
        audit.Record(id is null ? "curriculum.created" : "curriculum.updated", "Curriculum", c.Id, $"{code} {name}");
        await db.SaveChangesAsync(ct);
        return c.Id;
    }

    // ---------------- Courses, subjects, chapters ----------------

    public async Task<IReadOnlyList<CourseDto>> CoursesAsync(CancellationToken ct)
    {
        View();
        var q = db.Courses.AsQueryable();
        if (access.IsStudent) q = q.Where(c => db.CourseEnrolments.Any(e => e.CourseId == c.Id && e.StudentId == user.UserId && e.Status == EnrolmentStatus.Active));
        return await q.OrderBy(c => c.Name).Select(c => new CourseDto(c.Id, c.Code, c.Name, c.Description, c.GradeLevelId, c.GradeLevel != null ? c.GradeLevel.Name : null, c.Status,
            c.Subjects.Count, db.CourseEnrolments.Count(e => e.CourseId == c.Id && e.Status == EnrolmentStatus.Active))).ToListAsync(ct);
    }

    public async Task<Guid> SaveCourseAsync(Guid? id, SaveCourseRequest r, CancellationToken ct)
    {
        Manage();
        var code = Text.Required(r.Code, "code", 30).ToUpperInvariant();
        if (await db.Courses.AnyAsync(c => c.Code == code && c.Id != id, ct)) throw RequestValidationException.For("code", "This course code is already in use.");
        if (r.GradeLevelId is { } g && !await db.GradeLevels.AnyAsync(x => x.Id == g, ct)) throw RequestValidationException.For("gradeLevelId", "Grade not found.");
        if (r.AcademicYearId is { } y && !await db.AcademicYears.AnyAsync(x => x.Id == y, ct)) throw RequestValidationException.For("academicYearId", "Academic year not found.");
        var c = id is { } cid ? await db.Courses.FirstOrDefaultAsync(x => x.Id == cid, ct) ?? throw new NotFoundException("Course") : db.Courses.Add(new Course()).Entity;
        c.Code = code; c.Name = Text.Required(r.Name, "name", 150); c.Description = Text.Optional(r.Description, "description", 2000);
        c.GradeLevelId = r.GradeLevelId; c.AcademicYearId = r.AcademicYearId; c.Status = r.Status;
        audit.Record("course.saved", "Course", c.Id, code);
        await db.SaveChangesAsync(ct);
        return c.Id;
    }

    public async Task<IReadOnlyList<SubjectDto>> SubjectsAsync(Guid? courseId, CancellationToken ct)
    {
        View();
        var q = db.Subjects.AsQueryable();
        if (courseId is { } cid) q = q.Where(s => s.CourseId == cid);
        // Students see only their own subjects: core subjects of their courses, their language choices and assigned extras.
        if (access.IsStudent)
        {
            var mine = StudentCurriculum.SubjectsOf(user.UserId, db.Subjects, db.CourseEnrolments, db.StudentSubjects).Select(x => x.Id);
            q = q.Where(s => mine.Contains(s.Id));
        }
        return await q.OrderBy(s => s.Course!.Name).ThenBy(s => s.Name).Select(s => new SubjectDto(s.Id, s.CourseId, s.Course!.Name, s.Code, s.Name, s.Description, s.Status,
            s.Chapters.OrderBy(c => c.SortOrder).Select(c => new ChapterDto(c.Id, c.Title, c.SortOrder, c.Topics.OrderBy(t => t.SortOrder).Select(t => new TopicDto(t.Id, t.Title, t.SortOrder)).ToList())).ToList(),
            db.TeacherSubjects.Where(t => t.SubjectId == s.Id).Select(t => new LookupItem(t.TeacherId, t.Teacher!.User!.FullName, null)).ToList(),
            s.Kind, s.ChoiceGroup)).ToListAsync(ct);
    }

    public async Task<Guid> SaveSubjectAsync(Guid? id, SaveSubjectRequest r, CancellationToken ct)
    {
        Manage();
        if (!await db.Courses.AnyAsync(c => c.Id == r.CourseId, ct)) throw RequestValidationException.For("courseId", "Course not found.");
        var code = Text.Required(r.Code, "code", 30).ToUpperInvariant();
        if (await db.Subjects.AnyAsync(s => s.Code == code && s.Id != id, ct)) throw RequestValidationException.For("code", "This subject code is already in use.");
        var s = id is { } sid ? await db.Subjects.FirstOrDefaultAsync(x => x.Id == sid, ct) ?? throw new NotFoundException("Subject") : db.Subjects.Add(new Subject()).Entity;
        s.CourseId = r.CourseId; s.Code = code; s.Name = Text.Required(r.Name, "name", 150); s.Description = Text.Optional(r.Description, "description", 2000); s.Status = r.Status;
        try { if (r.Kind is { } kind) s.SetKind(kind, r.ChoiceGroup); }
        catch (DomainException ex) { throw RequestValidationException.For(ex.Code == "subject_kind" ? "kind" : "choiceGroup", ex.Message); }
        audit.Record("subject.saved", "Subject", s.Id, $"{code} ({s.Kind}{(s.ChoiceGroup is null ? "" : $": {s.ChoiceGroup}")})");
        await db.SaveChangesAsync(ct);
        return s.Id;
    }

    public async Task<Guid> AddChapterAsync(Guid subjectId, SaveChapterRequest r, CancellationToken ct)
    {
        Manage();
        if (!await db.Subjects.AnyAsync(s => s.Id == subjectId, ct)) throw new NotFoundException("Subject");
        var c = new Chapter { SubjectId = subjectId, Title = Text.Required(r.Title, "title", 200), SortOrder = r.SortOrder };
        db.Chapters.Add(c);
        await db.SaveChangesAsync(ct);
        return c.Id;
    }

    public async Task<Guid> AddTopicAsync(Guid chapterId, SaveTopicRequest r, CancellationToken ct)
    {
        Manage();
        if (!await db.Chapters.AnyAsync(c => c.Id == chapterId, ct)) throw new NotFoundException("Chapter");
        var t = new Topic { ChapterId = chapterId, Title = Text.Required(r.Title, "title", 200), SortOrder = r.SortOrder };
        db.Topics.Add(t);
        await db.SaveChangesAsync(ct);
        return t.Id;
    }

    public async Task AssignTeacherAsync(Guid subjectId, AssignTeacherRequest r, CancellationToken ct)
    {
        Manage();
        if (!await db.Subjects.AnyAsync(s => s.Id == subjectId, ct)) throw new NotFoundException("Subject");
        if (!await db.Teachers.AnyAsync(t => t.Id == r.TeacherId && t.Status == PersonStatus.Active, ct)) throw RequestValidationException.For("teacherId", "Active teacher not found.");
        if (await db.TeacherSubjects.AnyAsync(t => t.SubjectId == subjectId && t.TeacherId == r.TeacherId, ct)) return;
        db.TeacherSubjects.Add(new TeacherSubject { SubjectId = subjectId, TeacherId = r.TeacherId });
        audit.Record("subject.teacher_assigned", "Subject", subjectId, r.TeacherId.ToString());
        await db.SaveChangesAsync(ct);
    }

    public async Task UnassignTeacherAsync(Guid subjectId, Guid teacherId, CancellationToken ct)
    {
        Manage();
        var row = await db.TeacherSubjects.FirstOrDefaultAsync(t => t.SubjectId == subjectId && t.TeacherId == teacherId, ct) ?? throw new NotFoundException("Assignment");
        db.TeacherSubjects.Remove(row);
        audit.Record("subject.teacher_unassigned", "Subject", subjectId, teacherId.ToString());
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Enrolments ----------------

    public async Task<IReadOnlyList<CourseEnrolmentDto>> CourseEnrolmentsAsync(Guid courseId, CancellationToken ct)
    {
        Manage();
        return await db.CourseEnrolments.Where(e => e.CourseId == courseId).OrderBy(e => e.Student!.User!.FullName)
            .Select(e => new CourseEnrolmentDto(e.Id, e.StudentId, e.Student!.User!.FullName, e.CourseId, e.Course!.Name, e.Status, e.EnrolledAtUtc)).ToListAsync(ct);
    }

    public async Task EnrolInCourseAsync(Guid courseId, EnrolStudentRequest r, CancellationToken ct)
    {
        Manage();
        if (!await db.Courses.AnyAsync(c => c.Id == courseId && c.Status == CatalogStatus.Active, ct)) throw new NotFoundException("Course");
        if (!await db.Students.AnyAsync(s => s.Id == r.StudentId && s.Status == PersonStatus.Active, ct)) throw RequestValidationException.For("studentId", "Active student not found.");
        var e = await db.CourseEnrolments.FirstOrDefaultAsync(x => x.CourseId == courseId && x.StudentId == r.StudentId, ct);
        if (e?.Status == EnrolmentStatus.Active) throw new ConflictException("The student is already enrolled in this course.", "duplicate_enrolment");
        if (e is null) db.CourseEnrolments.Add(e = new CourseEnrolment { CourseId = courseId, StudentId = r.StudentId });
        e.Status = EnrolmentStatus.Active;
        e.EnrolledAtUtc = clock.UtcNow;
        audit.Record("course.enrolled", "CourseEnrolment", e.Id, $"{r.StudentId} -> {courseId}");
        await db.SaveChangesAsync(ct);
    }

    public async Task WithdrawFromCourseAsync(Guid courseId, Guid studentId, CancellationToken ct)
    {
        Manage();
        var e = await db.CourseEnrolments.FirstOrDefaultAsync(x => x.CourseId == courseId && x.StudentId == studentId && x.Status == EnrolmentStatus.Active, ct) ?? throw new NotFoundException("Enrolment");
        e.Status = EnrolmentStatus.Withdrawn;
        audit.Record("course.withdrawn", "CourseEnrolment", e.Id, null);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Batches ----------------

    public async Task<IReadOnlyList<BatchDto>> BatchesAsync(CancellationToken ct)
    {
        View();
        var q = db.Batches.AsQueryable();
        if (access.IsTeacher) q = q.Where(b => b.TeacherId == user.UserId);
        if (access.IsStudent) q = q.Where(b => b.Enrolments.Any(e => e.StudentId == user.UserId && e.Status == EnrolmentStatus.Active));
        return await q.OrderBy(b => b.Name).Select(b => new BatchDto(b.Id, b.Code, b.Name, b.CourseId, b.Course!.Name, b.SubjectId, b.Subject != null ? b.Subject.Name : null,
            b.TeacherId, b.Teacher!.User!.FullName, b.Capacity, b.Enrolments.Count(e => e.Status == EnrolmentStatus.Active), b.StartDate, b.EndDate, b.Status)).ToListAsync(ct);
    }

    public async Task<Guid> SaveBatchAsync(Guid? id, SaveBatchRequest r, CancellationToken ct)
    {
        Manage();
        var code = Text.Required(r.Code, "code", 30).ToUpperInvariant();
        if (await db.Batches.AnyAsync(b => b.Code == code && b.Id != id, ct)) throw RequestValidationException.For("code", "This batch code is already in use.");
        if (!await db.Courses.AnyAsync(c => c.Id == r.CourseId, ct)) throw RequestValidationException.For("courseId", "Course not found.");
        if (r.SubjectId is { } sid && !await db.Subjects.AnyAsync(s => s.Id == sid && s.CourseId == r.CourseId, ct)) throw RequestValidationException.For("subjectId", "Subject not found in this course.");
        if (!await db.Teachers.AnyAsync(t => t.Id == r.TeacherId && t.Status == PersonStatus.Active, ct)) throw RequestValidationException.For("teacherId", "Active teacher not found.");
        if (r.Capacity is < 1 or > 1000) throw RequestValidationException.For("capacity", "Capacity must be between 1 and 1000.");
        if (r.EndDate is { } end && end < r.StartDate) throw RequestValidationException.For("endDate", "End date must be after the start date.");
        var b = id is { } bid ? await db.Batches.Include(x => x.Enrolments).FirstOrDefaultAsync(x => x.Id == bid, ct) ?? throw new NotFoundException("Batch") : db.Batches.Add(new Batch()).Entity;
        if (b.Enrolments.Count(e => e.Status == EnrolmentStatus.Active) > r.Capacity) throw RequestValidationException.For("capacity", "Capacity is below the current number of students.");
        b.Code = code; b.Name = Text.Required(r.Name, "name", 150); b.CourseId = r.CourseId; b.SubjectId = r.SubjectId; b.TeacherId = r.TeacherId;
        b.Capacity = r.Capacity; b.StartDate = r.StartDate; b.EndDate = r.EndDate; b.Status = r.Status;
        audit.Record("batch.saved", "Batch", b.Id, code);
        await db.SaveChangesAsync(ct);
        return b.Id;
    }

    public async Task<IReadOnlyList<BatchStudentDto>> BatchStudentsAsync(Guid batchId, CancellationToken ct)
    {
        View();
        if (!await db.Batches.AnyAsync(b => b.Id == batchId && (access.IsAdmin || b.TeacherId == user.UserId), ct)) throw new NotFoundException("Batch");
        return await db.BatchEnrolments.Where(e => e.BatchId == batchId).OrderBy(e => e.Student!.User!.FullName)
            .Select(e => new BatchStudentDto(e.Id, e.StudentId, e.Student!.User!.FullName, e.Student.StudentCode, e.Status, e.JoinedAtUtc)).ToListAsync(ct);
    }

    public async Task AddToBatchAsync(Guid batchId, EnrolStudentRequest r, CancellationToken ct)
    {
        Manage();
        var b = await db.Batches.Include(x => x.Enrolments).FirstOrDefaultAsync(x => x.Id == batchId, ct) ?? throw new NotFoundException("Batch");
        if (!await db.Students.AnyAsync(s => s.Id == r.StudentId && s.Status == PersonStatus.Active, ct)) throw RequestValidationException.For("studentId", "Active student not found.");
        var e = b.Enrolments.FirstOrDefault(x => x.StudentId == r.StudentId);
        if (e?.Status == EnrolmentStatus.Active) throw new ConflictException("The student is already in this batch.", "duplicate_enrolment");
        if (b.Enrolments.Count(x => x.Status == EnrolmentStatus.Active) >= b.Capacity) throw new DomainException("This batch is full.", "batch_full");
        if (e is null) b.Enrolments.Add(e = new BatchEnrolment { StudentId = r.StudentId });
        e.Status = EnrolmentStatus.Active; e.JoinedAtUtc = clock.UtcNow; e.LeftAtUtc = null;
        // Batch membership implies course enrolment.
        if (!await db.CourseEnrolments.AnyAsync(x => x.CourseId == b.CourseId && x.StudentId == r.StudentId && x.Status == EnrolmentStatus.Active, ct))
        {
            var ce = await db.CourseEnrolments.FirstOrDefaultAsync(x => x.CourseId == b.CourseId && x.StudentId == r.StudentId, ct);
            if (ce is null) db.CourseEnrolments.Add(new CourseEnrolment { CourseId = b.CourseId, StudentId = r.StudentId, EnrolledAtUtc = clock.UtcNow });
            else { ce.Status = EnrolmentStatus.Active; ce.EnrolledAtUtc = clock.UtcNow; }
        }
        audit.Record("batch.student_added", "Batch", b.Id, r.StudentId.ToString());
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveFromBatchAsync(Guid batchId, Guid studentId, CancellationToken ct)
    {
        Manage();
        var e = await db.BatchEnrolments.FirstOrDefaultAsync(x => x.BatchId == batchId && x.StudentId == studentId && x.Status == EnrolmentStatus.Active, ct) ?? throw new NotFoundException("Enrolment");
        e.Status = EnrolmentStatus.Withdrawn;
        e.LeftAtUtc = clock.UtcNow;
        audit.Record("batch.student_removed", "Batch", batchId, studentId.ToString());
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Private arrangements ----------------

    public async Task<IReadOnlyList<ArrangementDto>> ArrangementsAsync(CancellationToken ct)
    {
        View();
        var q = db.Arrangements.AsQueryable();
        if (access.IsTeacher) q = q.Where(a => a.TeacherId == user.UserId);
        if (access.IsStudent) q = q.Where(a => a.Students.Any(s => s.StudentId == user.UserId && s.Status == EnrolmentStatus.Active));
        return await q.OrderBy(a => a.Title).Select(a => new ArrangementDto(a.Id, a.Type, a.Title, a.TeacherId, a.Teacher!.User!.FullName, a.SubjectId,
            a.Subject != null ? a.Subject.Name : null, a.Status, a.StartDate, a.Notes,
            a.Students.Where(s => s.Status == EnrolmentStatus.Active).Select(s => new LookupItem(s.StudentId, s.Student!.User!.FullName, null)).ToList())).ToListAsync(ct);
    }

    /// <summary>Creates or updates a one-to-one or private-group arrangement. Teachers may create their own.</summary>
    public async Task<Guid> SaveArrangementAsync(Guid? id, SaveArrangementRequest r, CancellationToken ct)
    {
        var isAdmin = user.Has(Permissions.AcademicsManage);
        if (!isAdmin && !(access.IsTeacher && r.TeacherId == user.UserId)) throw new ForbiddenException();
        if (!await db.Teachers.AnyAsync(t => t.Id == r.TeacherId && t.Status == PersonStatus.Active, ct)) throw RequestValidationException.For("teacherId", "Active teacher not found.");
        if (r.SubjectId is { } sid && !await db.Subjects.AnyAsync(s => s.Id == sid, ct)) throw RequestValidationException.For("subjectId", "Subject not found.");
        if (r.CourseId is { } cid && !await db.Courses.AnyAsync(c => c.Id == cid, ct)) throw RequestValidationException.For("courseId", "Course not found.");
        var studentIds = r.StudentIds.Distinct().ToList();
        if (await db.Students.CountAsync(s => studentIds.Contains(s.Id) && s.Status == PersonStatus.Active, ct) != studentIds.Count)
            throw RequestValidationException.For("studentIds", "One or more active students were not found.");

        var a = id is { } aid ? await db.Arrangements.Include(x => x.Students).FirstOrDefaultAsync(x => x.Id == aid, ct) ?? throw new NotFoundException("Arrangement")
            : db.Arrangements.Add(new TeachingArrangement()).Entity;
        if (id is not null && !isAdmin && a.TeacherId != user.UserId) throw new NotFoundException("Arrangement");
        if (id is not null && a.Type != r.Type) throw RequestValidationException.For("type", "The arrangement type cannot be changed.");
        try { a.Type = r.Type; a.ValidateStudentCount(studentIds.Count); }
        catch (DomainException ex) { throw RequestValidationException.For("studentIds", ex.Message); }
        a.Title = Text.Required(r.Title, "title", 150); a.TeacherId = r.TeacherId; a.SubjectId = r.SubjectId; a.CourseId = r.CourseId;
        a.StartDate = r.StartDate; a.Notes = Text.Optional(r.Notes, "notes", 2000); a.Status = r.Status;
        foreach (var existing in a.Students)
            existing.Status = studentIds.Contains(existing.StudentId) ? EnrolmentStatus.Active : EnrolmentStatus.Withdrawn;
        foreach (var sid2 in studentIds.Where(x => a.Students.All(s => s.StudentId != x)))
            a.Students.Add(new TeachingArrangementStudent { StudentId = sid2 });
        audit.Record("arrangement.saved", "TeachingArrangement", a.Id, $"{a.Type} '{a.Title}' ({studentIds.Count} student(s))");
        await db.SaveChangesAsync(ct);
        return a.Id;
    }
}
