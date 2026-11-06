using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Academics;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;

namespace StudentSync.Application.People;

/// <summary>
/// A student's learning profile (CLAUDE.md addendum C): weak subjects recorded by the student, by an admin on a parent's
/// behalf, by a teacher or from a diagnostic; and goals/observations written by staff. Needs are recommendations and
/// evidence for a learning plan, never a label: staff resolve them with a note and the history is kept.
///
/// Who may do what is decided here, server-side: admins for every student, teachers for students they teach
/// (<see cref="AccessPolicy.VisibleStudentIdsAsync"/>), students only for themselves and only as <see cref="LearningNeedSource.Student"/>.
/// </summary>
public sealed class LearningProfileService(IAppDbContext db, ICurrentUser user, AccessPolicy access, StudentCurriculumService curriculum, AuditService audit, IClock clock)
{
    public const int MaxOpenNeeds = 50;
    public const int MaxNotes = 200;

    // ---------------- Weak subjects (learning needs) ----------------

    public async Task<IReadOnlyList<LearningNeedDto>> NeedsAsync(Guid studentId, bool includeResolved, CancellationToken ct)
    {
        await EnsureCanViewAsync(studentId, ct);
        var q = db.LearningNeeds.Where(n => n.StudentId == studentId);
        if (!includeResolved) q = q.Where(n => n.Status == LearningNeedStatus.Open);
        return await Project(q).ToListAsync(ct);
    }

    public async Task<LearningNeedDto> AddNeedAsync(Guid studentId, AddLearningNeedRequest r, CancellationToken ct)
    {
        var self = IsSelf(studentId);
        if (self) access.Require(Permissions.LearningNeedsSelf);
        else { access.Require(Permissions.LearningNeedsManage); await EnsureCanViewAsync(studentId, ct); }

        // A student can only speak for themselves; staff say who told them (parent, teacher, …).
        var source = self ? LearningNeedSource.Student : r.Source ?? (access.IsTeacher ? LearningNeedSource.Teacher : LearningNeedSource.Admin);
        if (source == LearningNeedSource.Diagnostic) throw RequestValidationException.For("source", "Diagnostic needs are recorded from an assessment result, not by hand.");

        var subject = await db.Subjects.Where(s => s.Id == r.SubjectId).Select(s => new { s.Id, s.Name, s.Status }).FirstOrDefaultAsync(ct)
            ?? throw RequestValidationException.For("subjectId", "Subject not found.");
        // Students pick from their own subjects; staff may record any active subject (e.g. before enrolment is set up).
        if (self && !await curriculum.SubjectsOf(studentId).AnyAsync(s => s.Id == subject.Id, ct))
            throw RequestValidationException.For("subjectId", "Choose one of your subjects.");
        if (!self && subject.Status != CatalogStatus.Active) throw RequestValidationException.For("subjectId", "This subject is not active.");
        if (r.ChapterId is { } chapterId && !await db.Chapters.AnyAsync(c => c.Id == chapterId && c.SubjectId == subject.Id, ct))
            throw RequestValidationException.For("chapterId", "Chapter not found in this subject.");
        var note = Text.Optional(r.Note, "note", 1000);

        if (await db.LearningNeeds.AnyAsync(n => n.StudentId == studentId && n.SubjectId == subject.Id && n.ChapterId == r.ChapterId && n.Status == LearningNeedStatus.Open, ct))
            throw RequestValidationException.For("subjectId", r.ChapterId is null ? $"{subject.Name} is already recorded as a weak subject." : "This chapter is already recorded.");
        if (await db.LearningNeeds.CountAsync(n => n.StudentId == studentId && n.Status == LearningNeedStatus.Open, ct) >= MaxOpenNeeds)
            throw RequestValidationException.For("subjectId", "Too many open weak subjects. Resolve some first.");

        var need = new StudentLearningNeed { StudentId = studentId, SubjectId = subject.Id, ChapterId = r.ChapterId, Source = source, Note = note, RecordedByUserId = user.UserId };
        db.LearningNeeds.Add(need);
        audit.Record("student.learning_need_added", "Student", studentId, $"{subject.Name} ({source})");
        await db.SaveChangesAsync(ct);
        return await Project(db.LearningNeeds.Where(n => n.Id == need.Id)).FirstAsync(ct);
    }

    /// <summary>Staff close a need with a reason (kept as history). Students may withdraw a need they reported themselves.</summary>
    public async Task ResolveNeedAsync(Guid studentId, Guid needId, ResolveLearningNeedRequest r, CancellationToken ct)
    {
        access.Require(Permissions.LearningNeedsManage);
        await EnsureCanViewAsync(studentId, ct);
        var need = await OpenNeedAsync(studentId, needId, ct);
        need.Status = LearningNeedStatus.Resolved;
        need.ResolvedAtUtc = clock.UtcNow;
        need.ResolvedByUserId = user.UserId;
        need.ReviewNote = Text.Optional(r.ReviewNote, "reviewNote", 1000);
        audit.Record("student.learning_need_resolved", "Student", studentId, need.ReviewNote);
        await db.SaveChangesAsync(ct);
    }

    public async Task WithdrawOwnNeedAsync(Guid needId, CancellationToken ct)
    {
        access.Require(Permissions.LearningNeedsSelf);
        var need = await OpenNeedAsync(user.UserId, needId, ct);
        // Only what the student said themselves; a parent's or teacher's record stays until staff resolve it.
        if (need.Source != LearningNeedSource.Student) throw new ForbiddenException();
        db.LearningNeeds.Remove(need);
        audit.Record("student.learning_need_withdrawn", "Student", user.UserId, null);
        await db.SaveChangesAsync(ct);
    }

    private async Task<StudentLearningNeed> OpenNeedAsync(Guid studentId, Guid needId, CancellationToken ct) =>
        await db.LearningNeeds.FirstOrDefaultAsync(n => n.Id == needId && n.StudentId == studentId && n.Status == LearningNeedStatus.Open, ct)
        ?? throw new NotFoundException("Learning need");

    private IQueryable<LearningNeedDto> Project(IQueryable<StudentLearningNeed> q) =>
        q.OrderByDescending(n => n.Status == LearningNeedStatus.Open).ThenByDescending(n => n.CreatedAtUtc)
         .Select(n => new LearningNeedDto(n.Id, n.SubjectId, db.Subjects.Where(s => s.Id == n.SubjectId).Select(s => s.Name).First(),
             n.ChapterId, db.Chapters.Where(c => c.Id == n.ChapterId).Select(c => c.Title).FirstOrDefault(),
             n.Source, n.Note, n.Status, n.CreatedAtUtc, db.Users.Where(u => u.Id == n.RecordedByUserId).Select(u => u.FullName).FirstOrDefault() ?? "",
             n.ResolvedAtUtc, db.Users.Where(u => u.Id == n.ResolvedByUserId).Select(u => u.FullName).FirstOrDefault(), n.ReviewNote));

    // ---------------- Goals and observations ----------------

    /// <summary>Students receive only goals; staff see goals and observations.</summary>
    public async Task<IReadOnlyList<StudentNoteDto>> NotesAsync(Guid studentId, CancellationToken ct)
    {
        await EnsureCanViewAsync(studentId, ct);
        var q = db.StudentNotes.Where(n => n.StudentId == studentId);
        if (IsSelf(studentId) || !user.Has(Permissions.LearningNeedsManage)) q = q.Where(n => n.Kind == StudentNoteKind.Goal);
        var canDeleteAny = access.IsAdmin;
        var me = user.UserId;
        return await q.OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => new StudentNoteDto(n.Id, n.Kind, n.Text, n.SubjectId, db.Subjects.Where(s => s.Id == n.SubjectId).Select(s => s.Name).FirstOrDefault(),
                db.Users.Where(u => u.Id == n.AuthorUserId).Select(u => u.FullName).FirstOrDefault() ?? "", n.CreatedAtUtc, canDeleteAny || n.AuthorUserId == me))
            .ToListAsync(ct);
    }

    public async Task<StudentNoteDto> AddNoteAsync(Guid studentId, AddStudentNoteRequest r, CancellationToken ct)
    {
        access.Require(Permissions.LearningNeedsManage);
        await EnsureCanViewAsync(studentId, ct);
        var text = Text.Required(r.Text, "text", 2000);
        if (r.SubjectId is { } sid && !await db.Subjects.AnyAsync(s => s.Id == sid, ct)) throw RequestValidationException.For("subjectId", "Subject not found.");
        if (await db.StudentNotes.CountAsync(n => n.StudentId == studentId, ct) >= MaxNotes) throw RequestValidationException.For("text", "Too many notes for this student.");
        var note = new StudentNote { StudentId = studentId, Kind = r.Kind, Text = text, SubjectId = r.SubjectId, AuthorUserId = user.UserId };
        db.StudentNotes.Add(note);
        audit.Record(r.Kind == StudentNoteKind.Goal ? "student.goal_added" : "student.observation_added", "Student", studentId, null);
        await db.SaveChangesAsync(ct);
        return (await NotesAsync(studentId, ct)).First(n => n.Id == note.Id);
    }

    public async Task DeleteNoteAsync(Guid studentId, Guid noteId, CancellationToken ct)
    {
        access.Require(Permissions.LearningNeedsManage);
        await EnsureCanViewAsync(studentId, ct);
        var note = await db.StudentNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.StudentId == studentId, ct) ?? throw new NotFoundException("Note");
        if (!access.IsAdmin && note.AuthorUserId != user.UserId) throw new ForbiddenException();
        db.StudentNotes.Remove(note);
        audit.Record("student.note_deleted", "Student", studentId, note.Kind.ToString());
        await db.SaveChangesAsync(ct);
    }

    // ---------------- The student's own learning plan ----------------

    public async Task<LearningPlanDto> MyPlanAsync(CancellationToken ct)
    {
        access.Require(Permissions.LearningNeedsSelf);
        var me = user.UserId;
        var s = await db.Students.Where(x => x.Id == me).Select(x => new
        {
            Curriculum = db.Curricula.Where(c => c.Id == x.CurriculumId).Select(c => c.Name).FirstOrDefault(),
            Grade = db.GradeLevels.Where(g => g.Id == x.GradeLevelId).Select(g => g.Name).FirstOrDefault(),
            Year = db.AcademicYears.Where(y => y.Id == x.AcademicYearId).Select(y => y.Name).FirstOrDefault(),
        }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Student");
        var subjects = await curriculum.SubjectsOf(me).OrderBy(x => x.Name).Select(x => new LookupItem(x.Id, x.Name, x.Code)).ToListAsync(ct);
        return new LearningPlanDto(s.Curriculum, s.Grade, s.Year, await NeedsAsync(me, false, ct), await NotesAsync(me, ct), subjects);
    }

    private bool IsSelf(Guid studentId) => access.IsStudent && studentId == user.UserId;

    private async Task EnsureCanViewAsync(Guid studentId, CancellationToken ct)
    {
        if (IsSelf(studentId)) return;
        if (!user.Has(Permissions.LearningNeedsManage)) throw new ForbiddenException();
        await access.EnsureCanViewStudentAsync(studentId, ct);
    }
}
