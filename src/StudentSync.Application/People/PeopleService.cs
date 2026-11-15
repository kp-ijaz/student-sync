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
    IAppDbContext db, ICurrentUser user, ITenantContext tenant, AccessPolicy access, IUserAccountService accounts, IClock clock, AuditService audit, ProgressService progress, PhotoUrls photos,
    SchoolReportService schoolReports)
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
                    db.TeacherSubjects.Where(x => x.TeacherId == t.Id).Select(x => new LookupItem(x.SubjectId, x.Subject!.Name, x.Subject.Course!.Name)).ToList(), null,
                    t.Phone, t.ExperienceYears, t.Languages, t.JoinedOn,
                    t.Qualifications.OrderBy(x => x.SortOrder).Select(x => new TeacherQualificationDto(x.Title, x.Institution, x.Year)).ToList()),
                t.User.PhotoFileId,
            })
            .ToListAsync(ct);
        // Staff photos may be shown to anyone signed in at the institution; the phone number only to admins.
        var admin = user.Has(Permissions.UsersManage);
        var page = rows.Select(r => r.Dto with { PhotoUrl = photos.For(r.PhotoFileId), Phone = admin ? r.Dto.Phone : null }).ToList();
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
        var teacher = new Teacher { Id = account.UserId, EmployeeCode = Text.Optional(r.EmployeeCode, "employeeCode", 50), Specialisations = Text.Optional(r.Specialisations, "specialisations", 500), Bio = Text.Optional(r.Bio, "bio", 2000) };
        ApplyTeacherDetails(teacher, r.Phone, r.ExperienceYears, r.Languages, r.JoinedOn);
        db.Teachers.Add(teacher);
        if (r.Qualifications is not null) ReplaceQualifications(teacher, [], r.Qualifications);
        if (r.SubjectIds is not null) await ReplaceTeacherSubjectsAsync(teacher.Id, r.SubjectIds, ct);
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
        ApplyTeacherDetails(t, r.Phone, r.ExperienceYears, r.Languages, r.JoinedOn);
        if (r.Qualifications is not null)
            ReplaceQualifications(t, await db.TeacherQualifications.Where(x => x.TeacherId == t.Id).ToListAsync(ct), r.Qualifications);
        if (r.SubjectIds is not null) await ReplaceTeacherSubjectsAsync(t.Id, r.SubjectIds, ct);
        if (t.Status != r.Status)
        {
            t.Status = r.Status;
            await accounts.SetActiveAsync(t.Id, r.Status == PersonStatus.Active, ct);
            audit.Record("teacher.status", "Teacher", t.Id, r.Status.ToString());
        }
        await db.SaveChangesAsync(ct);
    }

    public const int MaxTeacherSubjects = 50, MaxQualifications = 20;

    private static string? Phone(string? value, string field)
    {
        var phone = Text.Optional(value, field, 30);
        if (phone is not null && !System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+?[0-9 ()\-]{6,30}$"))
            throw RequestValidationException.For(field, "Enter a phone number with digits only (an optional + at the start).");
        return phone;
    }

    private void ApplyStudentContact(Student s, string? phone, string? guardianName, string? guardianPhone, DateOnly? joinedOn)
    {
        s.Phone = Phone(phone, "phone");
        s.GuardianName = Text.Optional(guardianName, "guardianName", 150);
        s.GuardianPhone = Phone(guardianPhone, "guardianPhone");
        if (joinedOn is { } j && j > DateOnly.FromDateTime(clock.UtcNow).AddYears(1)) throw RequestValidationException.For("joinedOn", "The joining date is too far in the future.");
        s.JoinedOn = joinedOn;
    }

    private void ApplyTeacherDetails(Teacher t, string? phone, int? experienceYears, string? languages, DateOnly? joinedOn)
    {
        t.Phone = Phone(phone, "phone");
        if (experienceYears is < 0 or > 60) throw RequestValidationException.For("experienceYears", "Experience must be between 0 and 60 years.");
        t.ExperienceYears = experienceYears;
        t.Languages = Text.Optional(languages, "languages", 200);
        if (joinedOn is { } j && j > DateOnly.FromDateTime(clock.UtcNow).AddYears(1)) throw RequestValidationException.For("joinedOn", "The joining date is too far in the future.");
        t.JoinedOn = joinedOn;
    }

    private void ReplaceQualifications(Teacher t, List<TeacherQualification> existing, List<TeacherQualificationDto> wanted)
    {
        if (wanted.Count > MaxQualifications) throw RequestValidationException.For("qualifications", $"Add at most {MaxQualifications} qualifications.");
        var year = clock.UtcNow.Year;
        var rows = wanted.Select((q, i) =>
        {
            if (q.Year is { } y && (y < 1950 || y > year + 1)) throw RequestValidationException.For("qualifications", $"Check the year of “{q.Title}”.");
            return new TeacherQualification { TeacherId = t.Id, Title = Text.Required(q.Title, "qualifications", 150), Institution = Text.Optional(q.Institution, "qualifications", 150), Year = q.Year, SortOrder = i };
        }).ToList();
        db.TeacherQualifications.RemoveRange(existing);
        db.TeacherQualifications.AddRange(rows);
        if (existing.Count > 0 || rows.Count > 0) audit.Record("teacher.qualifications_updated", "Teacher", t.Id, $"{rows.Count} qualification(s)");
    }

    /// <summary>The subjects a teacher teaches: replaces the teacher's <see cref="TeacherSubject"/> links (same links as Academics).</summary>
    private async Task ReplaceTeacherSubjectsAsync(Guid teacherId, List<Guid> subjectIds, CancellationToken ct)
    {
        var wanted = subjectIds.Distinct().ToList();
        if (wanted.Count > MaxTeacherSubjects) throw RequestValidationException.For("subjectIds", $"Choose at most {MaxTeacherSubjects} subjects.");
        // Tenant-filtered: another institution's subject ids are simply not found.
        if (await db.Subjects.CountAsync(x => wanted.Contains(x.Id), ct) != wanted.Count)
            throw RequestValidationException.For("subjectIds", "One or more subjects were not found.");
        var current = await db.TeacherSubjects.Where(x => x.TeacherId == teacherId).ToListAsync(ct);
        var removed = current.Where(x => !wanted.Contains(x.SubjectId)).ToList();
        var added = wanted.Where(id => current.All(x => x.SubjectId != id)).ToList();
        db.TeacherSubjects.RemoveRange(removed);
        db.TeacherSubjects.AddRange(added.Select(id => new TeacherSubject { TeacherId = teacherId, SubjectId = id }));
        if (removed.Count > 0 || added.Count > 0)
            audit.Record("teacher.subjects_updated", "Teacher", teacherId, $"+{added.Count} −{removed.Count} subject(s)");
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
                    s.CurriculumId, db.Curricula.Where(c => c.Id == s.CurriculumId).Select(c => c.Name).FirstOrDefault(), s.AcademicYearId, db.AcademicYears.Where(y => y.Id == s.AcademicYearId).Select(y => y.Name).FirstOrDefault(),
                    s.Phone, s.GuardianName, s.GuardianPhone, s.JoinedOn),
                s.User.PhotoFileId,
            })
            .ToListAsync(ct);
        // Child safety: student photos only go to admins and to teachers for students they teach (the visible set above).
        var showPhotos = access.IsAdmin || access.IsTeacher;
        var page = rows.Select(r => HideContactUnlessAdmin(r.Dto) with { PhotoUrl = showPhotos ? photos.For(r.PhotoFileId) : null }).ToList();
        return new PagedResult<StudentDto>(page, q.SafePage, q.SafePageSize, total);
    }

    /// <summary>Phone numbers and the guardian's name are for the centre's admins only (least privilege).</summary>
    private StudentDto HideContactUnlessAdmin(StudentDto d) =>
        user.Has(Permissions.UsersManage) ? d : d with { Phone = null, GuardianName = null, GuardianPhone = null };

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
                x.CurriculumId, db.Curricula.Where(c => c.Id == x.CurriculumId).Select(c => c.Name).FirstOrDefault(), x.AcademicYearId, db.AcademicYears.Where(y => y.Id == x.AcademicYearId).Select(y => y.Name).FirstOrDefault(),
                x.Phone, x.GuardianName, x.GuardianPhone, x.JoinedOn),
            x.User.PhotoFileId,
        }).FirstAsync(ct);
        // Parents may view the profile (when the portal is on) but never receive the photo; teachers here already teach the student.
        var s = HideContactUnlessAdmin(row.Dto) with { PhotoUrl = access.IsAdmin || access.IsTeacher || id == user.UserId ? photos.For(row.PhotoFileId) : null };
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
        var student = new Student { Id = account.UserId, StudentCode = code, GradeLevelId = r.GradeLevelId, SchoolName = Text.Optional(r.SchoolName, "schoolName", 200), CurriculumId = r.CurriculumId, AcademicYearId = r.AcademicYearId };
        ApplyStudentContact(student, r.Phone, r.GuardianName, r.GuardianPhone, r.JoinedOn);
        db.Students.Add(student);
        audit.Record("student.created", "Student", account.UserId, code);

        // Admission extras, saved in the same transaction: tuition subjects, the goal they described, the last school result.
        if (r.SubjectIds is { Count: > 0 } subjectIds)
        {
            var wanted = subjectIds.Distinct().ToList();
            if (wanted.Count > Academics.StudentCurriculumService.MaxAssignedSubjects) throw RequestValidationException.For("subjectIds", "Too many subjects.");
            if (await db.Subjects.CountAsync(x => wanted.Contains(x.Id) && x.Status == CatalogStatus.Active, ct) != wanted.Count)
                throw RequestValidationException.For("subjectIds", "One or more subjects were not found.");
            db.StudentSubjects.AddRange(wanted.Select(id => new StudentSubject { StudentId = student.Id, SubjectId = id, AddedByUserId = user.UserId }));
            audit.Record("student.subjects_updated", "Student", student.Id, $"{wanted.Count} tuition subject(s) at admission");
        }
        if (Text.Optional(r.Goals, "goals", 2000) is { } goals)
            db.StudentNotes.Add(new StudentNote { StudentId = student.Id, Kind = StudentNoteKind.Goal, Text = goals, AuthorUserId = user.UserId });
        if (r.SchoolReport is { } report) await schoolReports.BuildAsync(student.Id, report, ct);
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
        ApplyStudentContact(s, r.Phone, r.GuardianName, r.GuardianPhone, r.JoinedOn);
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
