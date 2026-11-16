using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Fees;

/// <summary>
/// Printable fee receipts and salary slips (owner request 2026-11-17). A receipt is visible to admins and to the student it
/// belongs to; a slip to admins and to its teacher. Anyone else gets "not found". Removed (voided) payments still print,
/// clearly marked, so a printed copy can be checked.
/// </summary>
public sealed class ReceiptService(IAppDbContext db, ICurrentUser user, ITenantContext tenant)
{
    public async Task<ReceiptDto> ReceiptAsync(Guid paymentId, CancellationToken ct)
    {
        var p = await db.FeePayments.Where(x => x.Id == paymentId).Select(x => new
        {
            x.Id,
            x.StudentId,
            x.Amount,
            x.PaidOn,
            x.Method,
            x.Reference,
            x.VoidedAtUtc,
            x.VoidReason,
            x.PackageId,
            RecordedBy = db.Users.Where(u => u.Id == x.RecordedByUserId).Select(u => u.FullName).FirstOrDefault(),
        }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Receipt");
        if (!user.Has(Permissions.UsersManage) && !(user.Role == UserRole.Student && user.UserId == p.StudentId)) throw new NotFoundException("Receipt");
        var pkg = await db.StudentPackages.Where(x => x.Id == p.PackageId).Select(x => new
        {
            x.Name,
            x.Hours,
            x.Price,
            x.CurrencyCode,
            Paid = x.Payments.Where(y => y.VoidedAtUtc == null).Sum(y => (decimal?)y.Amount) ?? 0,
        }).FirstAsync(ct);
        var student = await db.Students.Where(s => s.Id == p.StudentId).Select(s => new { s.User!.FullName, s.StudentCode }).FirstAsync(ct);
        var centre = await CentreAsync(ct);
        return new ReceiptDto(p.Id, Number("R", p.PaidOn, p.Id), centre.Name, centre.ContactEmail, student.FullName, student.StudentCode, pkg.Name, pkg.Hours,
            p.Amount, pkg.CurrencyCode, p.PaidOn, p.Method.ToString(), p.Reference, p.RecordedBy, pkg.Price, pkg.Paid, Math.Max(0, pkg.Price - pkg.Paid),
            p.VoidedAtUtc is not null, p.VoidReason);
    }

    public async Task<SalarySlipDto> SlipAsync(Guid teacherId, Guid paymentId, CancellationToken ct)
    {
        if (!user.Has(Permissions.UsersManage) && !(user.Role == UserRole.Teacher && user.UserId == teacherId)) throw new NotFoundException("Salary slip");
        var p = await db.TeacherPayments.FirstOrDefaultAsync(x => x.Id == paymentId && x.TeacherId == teacherId, ct) ?? throw new NotFoundException("Salary slip");
        var name = await db.Users.Where(u => u.Id == teacherId).Select(u => u.FullName).FirstAsync(ct);
        var centre = await CentreAsync(ct);
        decimal? hours = null;
        if (p.PeriodFrom is { } from && p.PeriodTo is { } to)
        {
            var start = ZonedTime.ToUtc(from, TimeOnly.MinValue, centre.Tz).Utc;
            var end = ZonedTime.ToUtc(to.AddDays(1), TimeOnly.MinValue, centre.Tz).Utc;
            hours = (await db.Sessions.Where(s => s.TeacherId == teacherId && s.Status == SessionStatus.Completed && s.StartUtc >= start && s.StartUtc < end)
                .Select(s => new { s.StartUtc, s.EndUtc }).ToListAsync(ct)).Sum(s => FeeService.HoursOf(s.StartUtc, s.EndUtc));
        }
        return new SalarySlipDto(p.Id, Number("S", p.PaidOn, p.Id), centre.Name, centre.ContactEmail, name, p.Amount, p.CurrencyCode, p.PaidOn,
            p.PeriodFrom, p.PeriodTo, p.Method.ToString(), p.Reference, p.Note, hours, p.VoidedAtUtc is not null, p.VoidReason);
    }

    /// <summary>"R-202611-3F9A1C": type, month paid, first characters of the record id (stable, never reused).</summary>
    public static string Number(string prefix, DateOnly paidOn, Guid id) => $"{prefix}-{paidOn:yyyyMM}-{id.ToString("N")[^6..].ToUpperInvariant()}";

    private async Task<(string Name, string? ContactEmail, string Tz)> CentreAsync(CancellationToken ct)
    {
        var t = await db.Tenants.Where(x => x.Id == tenant.TenantId).Select(x => new { x.Name, x.ContactEmail, x.DefaultTimeZoneId }).FirstAsync(ct);
        return (t.Name, t.ContactEmail, t.DefaultTimeZoneId);
    }
}
