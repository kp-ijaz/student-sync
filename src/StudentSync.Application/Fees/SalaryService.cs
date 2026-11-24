using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Fees;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Fees;

/// <summary>
/// All teachers' pay for one month (Fees ▸ Salaries, and the month-end automation): hours of completed classes in the
/// month, what that earned at each day's rate, payments made in the month, and what is still owed overall. "Mark paid"
/// records one salary payment of what is owed, for that month, through <see cref="TeacherPayService"/> rules.
/// </summary>
public sealed class SalaryService(IAppDbContext db, ITenantContext tenant, AccessPolicy access, IClock clock, TeacherPayService pay)
{
    public sealed record Row(Guid TeacherId, string TeacherName, decimal Hours, int Classes, decimal Earned, decimal PaidInMonth, decimal DueNow, decimal? Rate, int UnratedClasses);

    /// <summary>No permission check: the caller (page or job) checks.</summary>
    public async Task<List<Row>> MonthAsync(DateOnly month, CancellationToken ct)
    {
        month = new DateOnly(month.Year, month.Month, 1);
        var next = month.AddMonths(1);
        var tz = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.DefaultTimeZoneId).FirstOrDefaultAsync(ct) ?? "UTC";
        // Deleted teachers are not on the salary list; what is still open with them is on their record (Teachers ▸ Deleted).
        var teachers = await db.Teachers.Where(t => t.Status != PersonStatus.Archived
                && (t.Status == PersonStatus.Active || db.Sessions.Any(s => s.TeacherId == t.Id && s.Status == SessionStatus.Completed)))
            .Select(t => new { t.Id, t.User!.FullName }).ToListAsync(ct);
        var ids = teachers.Select(t => t.Id).ToList();
        var rates = (await db.TeacherPayRates.Where(r => ids.Contains(r.TeacherId)).Select(r => new { r.TeacherId, r.EffectiveFrom, r.HourlyRate }).ToListAsync(ct))
            .GroupBy(r => r.TeacherId).ToDictionary(g => g.Key, g => (IReadOnlyList<TeacherPayRules.Rate>)g.Select(r => new TeacherPayRules.Rate(r.EffectiveFrom, r.HourlyRate)).ToList());
        var taught = (await db.Sessions.Where(s => ids.Contains(s.TeacherId) && s.Status == SessionStatus.Completed).Select(s => new { s.TeacherId, s.StartUtc, s.EndUtc }).ToListAsync(ct))
            .Select(s => (s.TeacherId, Class: new TeacherPayRules.TaughtClass(DateOnly.FromDateTime(ZonedTime.ToLocal(s.StartUtc, tz)), TeacherPayRules.Hours(s.StartUtc, s.EndUtc))))
            .ToList();
        var payments = await db.TeacherPayments.Where(p => ids.Contains(p.TeacherId) && p.VoidedAtUtc == null).Select(p => new { p.TeacherId, p.Amount, p.PaidOn }).ToListAsync(ct);
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz));

        return teachers.Select(t =>
        {
            var r = rates.GetValueOrDefault(t.Id) ?? [];
            var all = taught.Where(x => x.TeacherId == t.Id).Select(x => x.Class).ToList();
            var inMonth = all.Where(c => c.Date >= month && c.Date < next).ToList();
            var paidAll = payments.Where(p => p.TeacherId == t.Id).Sum(p => p.Amount);
            var first = r.Count == 0 ? (DateOnly?)null : r.Min(x => x.EffectiveFrom);
            return new Row(t.Id, t.FullName, inMonth.Sum(c => c.Hours), inMonth.Count, TeacherPayRules.Earned(inMonth, r),
                payments.Where(p => p.TeacherId == t.Id && p.PaidOn >= month && p.PaidOn < next).Sum(p => p.Amount),
                Math.Max(0, TeacherPayRules.Earned(all, r) - paidAll), r.Count == 0 ? null : TeacherPayRules.RateOn(r, today),
                inMonth.Count(c => first is null || c.Date < first));
        }).OrderBy(x => x.TeacherName).ToList();
    }

    public async Task<SalaryMonthDto> ForMonthAsync(DateOnly month, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        month = new DateOnly(month.Year, month.Month, 1);
        var currency = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.CurrencyCode).FirstAsync(ct);
        var rows = await MonthAsync(month, ct);
        return new SalaryMonthDto(month, currency, rows.Sum(r => r.Hours), rows.Sum(r => r.Earned), rows.Sum(r => r.PaidInMonth), rows.Sum(r => r.DueNow),
            rows.Select(r => new SalaryRowDto(r.TeacherId, r.TeacherName, r.Hours, r.Classes, r.Earned, r.PaidInMonth, r.DueNow, r.Rate, r.UnratedClasses)).ToList());
    }

    /// <summary>One tap: pays what the teacher is owed now, recorded for the chosen month.</summary>
    public async Task<Guid> PayAsync(Guid teacherId, PaySalaryRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var month = new DateOnly(r.Month.Year, r.Month.Month, 1);
        // Two admins (or two tabs) pressing "Mark paid" together must not both pay what is owed. The teacher row is touched
        // in the same save as the payment: its row version (xmin) makes the second save fail as a conflict, recording nothing.
        var teacher = await db.Teachers.FirstOrDefaultAsync(t => t.Id == teacherId, ct) ?? throw new NotFoundException("Teacher");
        var row = (await MonthAsync(month, ct)).FirstOrDefault(x => x.TeacherId == teacherId) ?? throw new NotFoundException("Teacher");
        if (row.DueNow <= 0) throw new ConflictException("Nothing is owed to this teacher right now.", "nothing_due");
        var tz = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.DefaultTimeZoneId).FirstAsync(ct);
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz));
        teacher.UpdatedAtUtc = clock.UtcNow;
        return await pay.AddPaymentAsync(teacherId, new AddTeacherPaymentRequest(row.DueNow, today, r.Method, r.Reference,
            $"Salary for {month:MMMM yyyy}", month, month.AddMonths(1).AddDays(-1)), ct);
    }
}
