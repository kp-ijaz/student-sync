using Microsoft.EntityFrameworkCore;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Common;

/// <summary>
/// Resource-level authorization shared by all modules. Tenant isolation is already applied by query filters;
/// these rules restrict access further to the records a user is entitled to within their institution.
/// </summary>
public sealed class AccessPolicy(IAppDbContext db, ICurrentUser user)
{
    private HashSet<Guid>? _children;

    public bool IsAdmin => user.Role == UserRole.InstitutionAdmin;
    public bool IsTeacher => user.Role == UserRole.Teacher;
    public bool IsStudent => user.Role == UserRole.Student;
    public bool IsParent => user.Role == UserRole.Parent;
    public Guid UserId => user.UserId;

    public void Require(string permission)
    {
        if (!user.Has(permission)) throw new ForbiddenException();
    }

    /// <summary>Verified children of the current parent (empty for other roles).</summary>
    public async Task<HashSet<Guid>> ChildIdsAsync(CancellationToken ct)
    {
        if (!IsParent) return [];
        return _children ??= (await db.ParentLinks
            .Where(l => l.ParentId == user.UserId && l.IsVerified)
            .Select(l => l.StudentId).ToListAsync(ct)).ToHashSet();
    }

    /// <summary>Sessions the current user may see.</summary>
    public async Task<IQueryable<LearningSession>> VisibleSessionsAsync(CancellationToken ct)
    {
        var q = db.Sessions.AsQueryable();
        if (user.Has(Permissions.SessionsManageAll)) return q;
        if (IsTeacher) return q.Where(s => s.TeacherId == user.UserId);
        if (IsStudent)
            return q.Where(s => s.Participants.Any(p => p.StudentId == user.UserId && p.Status == ParticipantStatus.Active));
        if (IsParent)
        {
            var children = await ChildIdsAsync(ct);
            return q.Where(s => s.Participants.Any(p => children.Contains(p.StudentId) && p.Status == ParticipantStatus.Active));
        }
        return q.Where(_ => false);
    }

    public async Task<LearningSession> GetVisibleSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var sessions = await VisibleSessionsAsync(ct);
        return await sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct) ?? throw new NotFoundException("Session");
    }

    /// <summary>Loads a session the user may manage (admin: any; teacher: own). Others get 404/403.</summary>
    public async Task<LearningSession> GetManageableSessionAsync(Guid sessionId, CancellationToken ct, bool includeParticipants = false)
    {
        var q = db.Sessions.AsQueryable();
        if (includeParticipants) q = q.Include(s => s.Participants);
        var session = await q.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) throw new NotFoundException("Session");
        if (CanManageSession(session)) return session;
        // Do not reveal existence of sessions the user cannot see at all.
        var visible = await (await VisibleSessionsAsync(ct)).AnyAsync(s => s.Id == sessionId, ct);
        throw visible ? new ForbiddenException() : new NotFoundException("Session");
    }

    public bool CanManageSession(LearningSession s)
        => user.Has(Permissions.SessionsManageAll) || (user.Has(Permissions.SessionsManageOwn) && s.TeacherId == user.UserId);

    /// <summary>Students taught by a teacher through any session, batch or private arrangement, or assessed by them in a published exam/diagnostic.</summary>
    public IQueryable<Guid> StudentsTaughtBy(Guid teacherId) =>
        db.SessionParticipants.Where(p => p.Session!.TeacherId == teacherId && p.Status == ParticipantStatus.Active).Select(p => p.StudentId)
            .Union(db.ExamRecipients.Where(r => db.Exams.Any(e => e.Id == r.ExamId && e.TeacherId == teacherId && e.Status != Domain.Assessments.ExamStatus.Draft)).Select(r => r.StudentId))
            .Union(db.BatchEnrolments.Where(e => e.Batch!.TeacherId == teacherId && e.Status == Domain.Academics.EnrolmentStatus.Active).Select(e => e.StudentId))
            .Union(db.ArrangementStudents.Where(a => db.Arrangements.Any(x => x.Id == a.ArrangementId && x.TeacherId == teacherId)
                                                     && a.Status == Domain.Academics.EnrolmentStatus.Active).Select(a => a.StudentId));

    /// <summary>Students whose records the current user may view.</summary>
    public async Task<IQueryable<Guid>> VisibleStudentIdsAsync(CancellationToken ct)
    {
        if (IsAdmin) return db.Students.Select(s => s.Id);
        if (IsTeacher) return StudentsTaughtBy(user.UserId);
        if (IsStudent) return db.Students.Where(s => s.Id == user.UserId).Select(s => s.Id);
        if (IsParent)
        {
            var children = await ChildIdsAsync(ct);
            return db.Students.Where(s => children.Contains(s.Id)).Select(s => s.Id);
        }
        return db.Students.Where(_ => false).Select(s => s.Id);
    }

    public async Task EnsureCanViewStudentAsync(Guid studentId, CancellationToken ct)
    {
        var ids = await VisibleStudentIdsAsync(ct);
        if (!await ids.AnyAsync(id => id == studentId, ct)) throw new NotFoundException("Student");
    }

    public async Task<bool> TeacherTeachesStudentAsync(Guid teacherId, Guid studentId, CancellationToken ct)
        => await StudentsTaughtBy(teacherId).AnyAsync(id => id == studentId, ct);
}
