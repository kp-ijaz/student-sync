using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Application.Reporting;
using StudentSync.Contracts;
using StudentSync.Domain.Academics;
using StudentSync.Domain.Common;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;
using StudentSync.Domain.Scheduling;
using StudentSync.Domain.Tenancy;

namespace StudentSync.Application.People;

public sealed class PeopleService(
    IAppDbContext db, ICurrentUser user, ITenantContext tenant, AccessPolicy access, IUserAccountService accounts, IClock clock, AuditService audit, ProgressService progress, PhotoUrls photos)
{
    private async Task<Tenant> TenantAsync(CancellationToken ct) => await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId, ct);

    private async Task EnsureCapacityAsync(UserRole role, CancellationToken ct)
    {
        var sub = await db.Subscriptions.Include(s => s.Plan).FirstOrDefaultAsync(ct);
        if (sub is null || !sub.IsUsable) throw new DomainException("Your institution's subscription is not active.", "subscription_inactive");
        if (role == UserRole.Student && await db.Students.CountAsync(s => s.Status == PersonStatus.Active, ct) >= sub.Plan!.MaxStudents)
            throw new DomainException($"Your plan allows {sub.Plan.MaxStudents} active students. Upgrade to add more.", "plan_limit");
        if (role == UserRole.Teacher && await db.Teachers.CountAsync(s => s.Status == PersonStatus.Active, ct) >= sub.Plan!.MaxTeachers)
            throw new DomainException($"Your plan allows {sub.Plan.MaxTeachers} active teachers. Upgrade to add more.", "plan_limit");
    }

    private static string TimeZoneOr(string? requested, string fallback)
    {
        if (string.IsNullOrWhiteSpace(requested)) return fallback;
        if (!ZonedTime.TryFind(requested, out _)) throw RequestValidationException.For("timeZoneId", "Choose a valid time zone.");
        return requested;
    }

    // ---------------- Teachers ----------------

    public async Task<PagedResult<TeacherDto>> TeachersAsync(PersonQuery q, CancellationToken ct)
    {
        access.Require(Permissions.UsersView);
        var items = db.Teachers.AsQueryable();
        if (q.Status is { } st) items = items.Where(t => t.Status == st);
        if (!string.IsNullOrWhiteSpace(q.Search)) items = items.Where(t => t.User!.FullName.Contains(q.Search) || t.User.Email!.Contains(q.Search));
        var total = await items.CountAsync(ct);
        var rows = await items.OrderBy(t => t.User!.FullName).Skip((q.SafePage - 1) * q.SafePageSize).Take(q.SafePageSize)
            .Select(t => new
            {
                Dto = new TeacherDto(t.Id, t.User!.FullName, t.User.Email!, t.EmployeeCode, t.Specialisations, t.Bio, t.Status, t.User.TimeZoneId,
                    db.TeacherSubjects.Where(x => x.TeacherId == t.Id).Select(x => new LookupItem(x.SubjectId, x.Subject!.Name, x.Subject.Course!.Name)).ToList(), null),
                t.User.PhotoFileId,
            })
            .ToListAsync(ct);
        // Staff photos may be shown to anyone signed in at the institution.
        var page = rows.Select(r => r.Dto with { PhotoUrl = photos.For(r.PhotoFileId) }).ToList();
        return new PagedResult<TeacherDto>(page, q.SafePage, q.SafePageSize, total);
    }

    public async Task<CreatedPersonResponse> CreateTeacherAsync(CreateTeacherRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        await EnsureCapacityAsync(UserRole.Teacher, ct);
        var t = await TenantAsync(ct);
        // Account and profile are created atomically.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var account = await accounts.CreateAsync(t.Id, r.Email, Text.Required(r.FullName, "fullName", 150), UserRole.Teacher, TimeZoneOr(r.TimeZoneId, t.DefaultTimeZoneId), null, false, ct);
        db.Teachers.Add(new Teacher { Id = account.UserId, EmployeeCode = Text.Optional(r.EmployeeCode, "employeeCode", 50), Specialisations = Text.Optional(r.Specialisations, "specialisations", 500), Bio = Text.Optional(r.Bio, "bio", 2000) });
        audit.Record("teacher.created", "Teacher", account.UserId, r.FullName);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new CreatedPersonResponse(account.UserId, account.TemporaryPassword!);
    }

    public async Task UpdateTeacherAsync(Guid id, UpdateTeacherRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var t = await db.Teachers.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Teacher");
        if (r.Status == PersonStatus.Active && t.Status != PersonStatus.Active) await EnsureCapacityAsync(UserRole.Teacher, ct);
        t.User!.FullName = Text.Required(r.FullName, "fullName", 150);
        t.User.TimeZoneId = TimeZoneOr(r.TimeZoneId, t.User.TimeZoneId);
        t.EmployeeCode = Text.Optional(r.EmployeeCode, "employeeCode", 50);
        t.Specialisations = Text.Optional(r.Specialisations, "specialisations", 500);
        t.Bio = Text.Optional(r.Bio, "bio", 2000);
        if (t.Status != r.Status)
        {
            t.Status = r.Status;
            await accounts.SetActiveAsync(t.Id, r.Status == PersonStatus.Active, ct);
            audit.Record("teacher.status", "Teacher", t.Id, r.Status.ToString());
        }
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Students ----------------

    public async Task<PagedResult<StudentDto>> StudentsAsync(PersonQuery q, CancellationToken ct)
    {
        access.Require(Permissions.UsersView);
        var visible = await access.VisibleStudentIdsAsync(ct);
        var items = db.Students.Where(s => visible.Contains(s.Id));
        if (q.Status is { } st) items = items.Where(s => s.Status == st);
        if (!string.IsNullOrWhiteSpace(q.Search)) items = items.Where(s => s.User!.FullName.Contains(q.Search) || s.StudentCode.Contains(q.Search) || s.User.Email!.Contains(q.Search));
        var total = await items.CountAsync(ct);
        var rows = await items.OrderBy(s => s.User!.FullName).Skip((q.SafePage - 1) * q.SafePageSize).Take(q.SafePageSize)
            .Select(s => new
            {
                Dto = new StudentDto(s.Id, s.User!.FullName, s.User.Email!, s.StudentCode, s.GradeLevelId,
                    db.GradeLevels.Where(g => g.Id == s.GradeLevelId).Select(g => g.Name).FirstOrDefault(), s.SchoolName, s.Status, s.User.TimeZoneId, null,
                    s.CurriculumId, db.Curricula.Where(c => c.Id == s.CurriculumId).Select(c => c.Name).FirstOrDefault(), s.AcademicYearId, db.AcademicYears.Where(y => y.Id == s.AcademicYearId).Select(y => y.Name).FirstOrDefault()),
                s.User.PhotoFileId,
            })
            .ToListAsync(ct);
        // Child safety: student photos only go to admins and to teachers for students they teach (the visible set above).
        var showPhotos = access.IsAdmin || access.IsTeacher;
        var page = rows.Select(r => r.Dto with { PhotoUrl = showPhotos ? photos.For(r.PhotoFileId) : null }).ToList();
        return new PagedResult<StudentDto>(page, q.SafePage, q.SafePageSize, total);
    }

    /// <summary>Minimal directory (name and code only) so staff can pick any active student when scheduling.</summary>
    public async Task<IReadOnlyList<LookupItem>> StudentLookupAsync(string? search, CancellationToken ct)
    {
        if (!user.Has(Permissions.SessionsManageOwn)) throw new ForbiddenException();
        var q = db.Students.Where(s => s.Status == PersonStatus.Active);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(s => s.User!.FullName.Contains(search) || s.StudentCode.Contains(search));
        return await q.OrderBy(s => s.User!.FullName).Take(200).Select(s => new LookupItem(s.Id, s.User!.FullName, s.StudentCode)).ToListAsync(ct);
    }

    public async Task<StudentProfileDto> StudentProfileAsync(Guid id, CancellationToken ct)
    {
        await access.EnsureCanViewStudentAsync(id, ct);
        var row = await db.Students.Where(x => x.Id == id).Select(x => new
        {
            Dto = new StudentDto(x.Id, x.User!.FullName, x.User.Email!, x.StudentCode, x.GradeLevelId,
                db.GradeLevels.Where(g => g.Id == x.GradeLevelId).Select(g => g.Name).FirstOrDefault(), x.SchoolName, x.Status, x.User.TimeZoneId, null,
                x.CurriculumId, db.Curricula.Where(c => c.Id == x.CurriculumId).Select(c => c.Name).FirstOrDefault(), x.AcademicYearId, db.AcademicYears.Where(y => y.Id == x.AcademicYearId).Select(y => y.Name).FirstOrDefault()),
            x.User.PhotoFileId,
        }).FirstAsync(ct);
        // Parents may view the profile (when the portal is on) but never receive the photo; teachers here already teach the student.
        var s = row.Dto with { PhotoUrl = access.IsAdmin || access.IsTeacher || id == user.UserId ? photos.For(row.PhotoFileId) : null };
        var courses = await db.CourseEnrolments.Where(e => e.StudentId == id && e.Status == EnrolmentStatus.Active).Select(e => new LookupItem(e.CourseId, e.Course!.Name, e.Course.Code)).ToListAsync(ct);
        var batches = await db.BatchEnrolments.Where(e => e.StudentId == id && e.Status == EnrolmentStatus.Active).Select(e => new LookupItem(e.BatchId, e.Batch!.Name, e.Batch.Code)).ToListAsync(ct);
        var arrangements = await db.ArrangementStudents.Where(a => a.StudentId == id && a.Status == EnrolmentStatus.Active)
            .Join(db.Arrangements, a => a.ArrangementId, x => x.Id, (a, x) => new LookupItem(x.Id, x.Title, x.Type.ToString())).ToListAsync(ct);
        var parents = access.IsAdmin
            ? await db.ParentLinks.Where(l => l.StudentId == id).Select(l => new LookupItem(l.ParentId, l.Parent!.User!.FullName, l.Relationship.ToString())).ToListAsync(ct)
            : [];
        return new StudentProfileDto(s, courses, batches, arrangements, parents, await progress.ForStudentAsync(id, ct));
    }

    public async Task<CreatedPersonResponse> CreateStudentAsync(CreateStudentRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        await EnsureCapacityAsync(UserRole.Student, ct);
        var code = Text.Required(r.StudentCode, "studentCode", 30);
        if (await db.Students.AnyAsync(s => s.StudentCode == code, ct)) throw RequestValidationException.For("studentCode", "This student code is already in use.");
        if (r.GradeLevelId is { } g && !await db.GradeLevels.AnyAsync(x => x.Id == g, ct)) throw RequestValidationException.For("gradeLevelId", "Grade not found.");
        await EnsureAcademicRefsAsync(r.CurriculumId, r.AcademicYearId, ct);
        var t = await TenantAsync(ct);
        // Account and profile are created atomically.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var account = await accounts.CreateAsync(t.Id, r.Email, Text.Required(r.FullName, "fullName", 150), UserRole.Student, TimeZoneOr(r.TimeZoneId, t.DefaultTimeZoneId), null, false, ct);
        db.Students.Add(new Student { Id = account.UserId, StudentCode = code, GradeLevelId = r.GradeLevelId, SchoolName = Text.Optional(r.SchoolName, "schoolName", 200), CurriculumId = r.CurriculumId, AcademicYearId = r.AcademicYearId });
        audit.Record("student.created", "Student", account.UserId, code);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new CreatedPersonResponse(account.UserId, account.TemporaryPassword!);
    }

    public async Task UpdateStudentAsync(Guid id, UpdateStudentRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var s = await db.Students.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student");
        var code = Text.Required(r.StudentCode, "studentCode", 30);
        if (code != s.StudentCode && await db.Students.AnyAsync(x => x.StudentCode == code, ct)) throw RequestValidationException.For("studentCode", "This student code is already in use.");
        if (r.GradeLevelId is { } g && !await db.GradeLevels.AnyAsync(x => x.Id == g, ct)) throw RequestValidationException.For("gradeLevelId", "Grade not found.");
        await EnsureAcademicRefsAsync(r.CurriculumId, r.AcademicYearId, ct);
        if (r.Status == PersonStatus.Active && s.Status != PersonStatus.Active) await EnsureCapacityAsync(UserRole.Student, ct);
        s.User!.FullName = Text.Required(r.FullName, "fullName", 150);
        s.User.TimeZoneId = TimeZoneOr(r.TimeZoneId, s.User.TimeZoneId);
        s.StudentCode = code;
        s.GradeLevelId = r.GradeLevelId;
        s.SchoolName = Text.Optional(r.SchoolName, "schoolName", 200);
        s.CurriculumId = r.CurriculumId;
        s.AcademicYearId = r.AcademicYearId;
        if (s.Status != r.Status)
        {
            s.Status = r.Status;
            await accounts.SetActiveAsync(s.Id, r.Status == PersonStatus.Active, ct);
            audit.Record("student.status", "Student", s.Id, r.Status.ToString());
        }
        await db.SaveChangesAsync(ct);
    }

    // Tenant filters make another institution's ids look missing, so these are plain validation errors.
    private async Task EnsureAcademicRefsAsync(Guid? curriculumId, Guid? academicYearId, CancellationToken ct)
    {
        if (curriculumId is { } c && !await db.Curricula.AnyAsync(x => x.Id == c, ct)) throw RequestValidationException.For("curriculumId", "Curriculum not found.");
        if (academicYearId is { } y && !await db.AcademicYears.AnyAsync(x => x.Id == y, ct)) throw RequestValidationException.For("academicYearId", "Academic year not found.");
    }

    /// <summary>Admin-only credential re-issue. The temporary password is returned once and audited without its value.</summary>
    public async Task<TemporaryPasswordResponse> ResetStudentPasswordAsync(Guid id, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var s = await db.Students.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student");
        var password = await accounts.ResetPasswordAsync(s.Id, ct);
        audit.Record("student.password_reset", "Student", s.Id, s.StudentCode);
        await db.SaveChangesAsync(ct);
        return new TemporaryPasswordResponse(password);
    }

    public async Task<TemporaryPasswordResponse> ResetTeacherPasswordAsync(Guid id, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var t = await db.Teachers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Teacher");
        var password = await accounts.ResetPasswordAsync(t.Id, ct);
        audit.Record("teacher.password_reset", "Teacher", t.Id, t.EmployeeCode);
        await db.SaveChangesAsync(ct);
        return new TemporaryPasswordResponse(password);
    }

    // ---------------- Parents ----------------

    public async Task<PagedResult<ParentDto>> ParentsAsync(PersonQuery q, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var items = db.Parents.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q.Search)) items = items.Where(p => p.User!.FullName.Contains(q.Search) || p.User.Email!.Contains(q.Search));
        var total = await items.CountAsync(ct);
        var page = await items.OrderBy(p => p.User!.FullName).Skip((q.SafePage - 1) * q.SafePageSize).Take(q.SafePageSize)
            .Select(p => new ParentDto(p.Id, p.User!.FullName, p.User.Email!, p.Status,
                db.ParentLinks.Where(l => l.ParentId == p.Id).Select(l => new ChildLinkDto(l.Id, l.StudentId, l.Student!.User!.FullName, l.Relationship, l.IsVerified)).ToList()))
            .ToListAsync(ct);
        return new PagedResult<ParentDto>(page, q.SafePage, q.SafePageSize, total);
    }

    public async Task<CreatedPersonResponse> CreateParentAsync(CreateParentRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var t = await TenantAsync(ct);
        // Account and profile are created atomically.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var account = await accounts.CreateAsync(t.Id, r.Email, Text.Required(r.FullName, "fullName", 150), UserRole.Parent, TimeZoneOr(r.TimeZoneId, t.DefaultTimeZoneId), null, false, ct);
        db.Parents.Add(new Parent { Id = account.UserId });
        audit.Record("parent.created", "Parent", account.UserId, r.FullName);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new CreatedPersonResponse(account.UserId, account.TemporaryPassword!);
    }

    public async Task<Guid> LinkChildAsync(Guid parentId, LinkChildRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        if (!await db.Parents.AnyAsync(p => p.Id == parentId, ct)) throw new NotFoundException("Parent");
        if (!await db.Students.AnyAsync(s => s.Id == r.StudentId, ct)) throw RequestValidationException.For("studentId", "Student not found.");
        var link = await db.ParentLinks.FirstOrDefaultAsync(l => l.ParentId == parentId && l.StudentId == r.StudentId, ct);
        if (link is null) db.ParentLinks.Add(link = new ParentStudentLink { ParentId = parentId, StudentId = r.StudentId });
        link.Relationship = r.Relationship;
        if (r.Verified && !link.IsVerified) { link.IsVerified = true; link.VerifiedAtUtc = clock.UtcNow; link.VerifiedByUserId = user.UserId; }
        if (!r.Verified) { link.IsVerified = false; link.VerifiedAtUtc = null; }
        audit.Record("parent.link", "ParentStudentLink", link.Id, $"{parentId} -> {r.StudentId} verified={r.Verified}");
        await db.SaveChangesAsync(ct);
        return link.Id;
    }

    public async Task UnlinkChildAsync(Guid parentId, Guid linkId, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var link = await db.ParentLinks.FirstOrDefaultAsync(l => l.Id == linkId && l.ParentId == parentId, ct) ?? throw new NotFoundException("Link");
        db.ParentLinks.Remove(link);
        audit.Record("parent.unlink", "ParentStudentLink", link.Id, $"{parentId} -/-> {link.StudentId}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LookupItem>> MyChildrenAsync(CancellationToken ct)
    {
        var ids = await access.ChildIdsAsync(ct);
        return await db.Students.Where(s => ids.Contains(s.Id)).OrderBy(s => s.User!.FullName).Select(s => new LookupItem(s.Id, s.User!.FullName, s.StudentCode)).ToListAsync(ct);
    }
}
