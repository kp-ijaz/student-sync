using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Application.Content;
using StudentSync.Domain.Content;
using StudentSync.Domain.Identity;

namespace StudentSync.Application.People;

/// <summary>
/// Profile photos. Everyone signed in at an institution may manage their own photo; admins may manage teachers' and
/// students' photos (audited). Replacing or removing a photo soft-deletes the previous file so existing links stop working.
/// </summary>
public sealed class ProfilePhotoService(IAppDbContext db, ICurrentUser user, AccessPolicy access, FileService files, IClock clock, AuditService audit)
{
    public async Task SetOwnAsync(Stream content, string fileName, string contentType, long length, CancellationToken ct)
        => await ReplaceAsync(await OwnAccountAsync(ct), content, fileName, contentType, length, auditEntity: null, ct);

    public async Task RemoveOwnAsync(CancellationToken ct) => await RemoveAsync(await OwnAccountAsync(ct), auditEntity: null, ct);

    public async Task SetTeacherAsync(Guid teacherId, Stream content, string fileName, string contentType, long length, CancellationToken ct)
        => await ReplaceAsync(await ManagedAccountAsync(teacherId, isTeacher: true, ct), content, fileName, contentType, length, "Teacher", ct);

    public async Task RemoveTeacherAsync(Guid teacherId, CancellationToken ct) => await RemoveAsync(await ManagedAccountAsync(teacherId, isTeacher: true, ct), "Teacher", ct);

    public async Task SetStudentAsync(Guid studentId, Stream content, string fileName, string contentType, long length, CancellationToken ct)
        => await ReplaceAsync(await ManagedAccountAsync(studentId, isTeacher: false, ct), content, fileName, contentType, length, "Student", ct);

    public async Task RemoveStudentAsync(Guid studentId, CancellationToken ct) => await RemoveAsync(await ManagedAccountAsync(studentId, isTeacher: false, ct), "Student", ct);

    private async Task<AppUser> OwnAccountAsync(CancellationToken ct)
    {
        if (user.TenantId is not { } tenantId) throw new ForbiddenException("Profile photos belong to an institution account.");
        return await db.Users.FirstOrDefaultAsync(u => u.Id == user.UserId && u.TenantId == tenantId, ct) ?? throw new NotFoundException("User");
    }

    /// <summary>Same permission as editing the person; people outside the caller's institution are not found (tenant query filters).</summary>
    private async Task<AppUser> ManagedAccountAsync(Guid id, bool isTeacher, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var exists = isTeacher ? await db.Teachers.AnyAsync(t => t.Id == id, ct) : await db.Students.AnyAsync(s => s.Id == id, ct);
        if (!exists) throw new NotFoundException(isTeacher ? "Teacher" : "Student");
        return await db.Users.FirstAsync(u => u.Id == id && u.TenantId == user.TenantId, ct);
    }

    private async Task ReplaceAsync(AppUser account, Stream content, string fileName, string contentType, long length, string? auditEntity, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var uploaded = await files.UploadAsync(content, fileName, contentType, length, FilePurpose.Avatar, ct);
        await RetireCurrentAsync(account, ct);
        account.PhotoFileId = uploaded.Id;
        if (auditEntity is not null) audit.Record("person.photo.updated", auditEntity, account.Id, account.FullName);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task RemoveAsync(AppUser account, string? auditEntity, CancellationToken ct)
    {
        if (account.PhotoFileId is null) return;
        await RetireCurrentAsync(account, ct);
        account.PhotoFileId = null;
        if (auditEntity is not null) audit.Record("person.photo.removed", auditEntity, account.Id, account.FullName);
        await db.SaveChangesAsync(ct);
    }

    private async Task RetireCurrentAsync(AppUser account, CancellationToken ct)
    {
        if (account.PhotoFileId is not { } oldId) return;
        var old = await db.Files.FirstOrDefaultAsync(f => f.Id == oldId && f.DeletedAtUtc == null, ct);
        if (old is not null) old.DeletedAtUtc = clock.UtcNow;
    }
}
