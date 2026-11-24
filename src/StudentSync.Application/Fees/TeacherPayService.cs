using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Common;
using StudentSync.Domain.Fees;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Fees;

/// <summary>
/// Teacher salary (owner decision 2026-11-16): each teacher has a rate per hour from a date; pay = hours of completed
/// classes they taught × the rate in force that day (a class the student missed still counts — the teacher was there;
/// a class cancelled because the teacher was absent does not). Salary payments are recorded, never edited, and voided
/// with a reason. Admins manage everything; a teacher can read only their own pay. Every change is audited (no amounts
/// of other people in the audit summary beyond the teacher's own record).
/// </summary>
public sealed class TeacherPayService(IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, AuditService audit, CentreService centre)
{
    public async Task<TeacherPayDto> PayAsync(Guid teacherId, CancellationToken ct)
    {
        if (!(access.IsTeacher && teacherId == user.UserId)) access.Require(Permissions.UsersManage);
        if (!await db.Teachers.AnyAsync(t => t.Id == teacherId, ct)) throw new NotFoundException("Teacher");
        var (currency, tz) = await centre.GetAsync(ct);

        var rates = await db.TeacherPayRates.Where(r => r.TeacherId == teacherId).OrderByDescending(r => r.EffectiveFrom)
            .Select(r => new TeacherPayRateDto(r.HourlyRate, r.EffectiveFrom, r.Id)).ToListAsync(ct);
        var ruleRates = rates.Select(r => new TeacherPayRules.Rate(r.EffectiveFrom, r.HourlyRate)).ToList();

        var taught = (await db.Sessions.Where(s => s.TeacherId == teacherId && s.Status == SessionStatus.Completed)
                .Select(s => new { s.StartUtc, s.EndUtc }).ToListAsync(ct))
            .Select(s => new TeacherPayRules.TaughtClass(DateOnly.FromDateTime(ZonedTime.ToLocal(s.StartUtc, tz)), TeacherPayRules.Hours(s.StartUtc, s.EndUtc)))
            .ToList();
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz));
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var thisMonth = taught.Where(c => c.Date >= monthStart).ToList();
        var firstRate = ruleRates.Count == 0 ? (DateOnly?)null : ruleRates.Min(r => r.EffectiveFrom);
        var unrated = taught.Count(c => firstRate is null || c.Date < firstRate);

        var payments = await db.TeacherPayments.Where(p => p.TeacherId == teacherId)
            .OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.CreatedAtUtc).Take(200)
            .Select(p => new TeacherPaymentDto(p.Id, p.Amount, p.PaidOn, p.PeriodFrom, p.PeriodTo, p.Method, p.Reference, p.Note,
                db.Users.Where(u => u.Id == p.RecordedByUserId).Select(u => u.FullName).FirstOrDefault(), p.VoidedAtUtc != null, p.VoidReason))
            .ToListAsync(ct);
        var paid = await db.TeacherPayments.Where(p => p.TeacherId == teacherId && p.VoidedAtUtc == null).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var earned = TeacherPayRules.Earned(taught, ruleRates);

        return new TeacherPayDto(teacherId, currency, rates.Count == 0 ? null : TeacherPayRules.RateOn(ruleRates, today), rates,
            thisMonth.Sum(c => c.Hours), TeacherPayRules.Earned(thisMonth, ruleRates), taught.Sum(c => c.Hours), earned, paid, earned - paid, unrated, payments);
    }

    /// <summary>Sets the rate from a day (default today). The same day again replaces that day's rate.</summary>
    public async Task SetRateAsync(Guid teacherId, SetTeacherRateRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        if (!await db.Teachers.AnyAsync(t => t.Id == teacherId, ct)) throw new NotFoundException("Teacher");
        try { TeacherPayRate.Validate(r.HourlyRate); }
        catch (DomainException e) { throw RequestValidationException.For("hourlyRate", e.Message); }
        var (currency, tz) = await centre.GetAsync(ct);
        var from = r.EffectiveFrom ?? DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz));
        var row = await db.TeacherPayRates.FirstOrDefaultAsync(x => x.TeacherId == teacherId && x.EffectiveFrom == from, ct);
        if (row is null) db.TeacherPayRates.Add(row = new TeacherPayRate { TeacherId = teacherId, EffectiveFrom = from });
        (row.HourlyRate, row.CurrencyCode) = (decimal.Round(r.HourlyRate, 2), currency);
        audit.Record("teacher.pay_rate_set", "Teacher", teacherId, $"{currency} {row.HourlyRate}/h from {from:yyyy-MM-dd}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Removes a rate entered by mistake. Classes on its days fall back to the previous rate (or count as unrated), so pay
    /// figures are recalculated; salary payments already made are not touched. Audited with the removed value.
    /// </summary>
    public async Task RemoveRateAsync(Guid teacherId, Guid rateId, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var row = await db.TeacherPayRates.FirstOrDefaultAsync(x => x.Id == rateId && x.TeacherId == teacherId, ct) ?? throw new NotFoundException("Rate");
        db.TeacherPayRates.Remove(row);
        audit.Record("teacher.pay_rate_removed", "Teacher", teacherId, $"{row.CurrencyCode} {row.HourlyRate}/h from {row.EffectiveFrom:yyyy-MM-dd}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> AddPaymentAsync(Guid teacherId, AddTeacherPaymentRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        if (!await db.Teachers.AnyAsync(t => t.Id == teacherId, ct)) throw new NotFoundException("Teacher");
        try { TeacherPayment.Validate(r.Amount, r.PeriodFrom, r.PeriodTo); }
        catch (DomainException e) { throw RequestValidationException.For(e.Code == "invalid_period" ? "periodTo" : "amount", e.Message); }
        if (!Enum.IsDefined(r.Method) || r.Method == PaymentMethod.Online) throw RequestValidationException.For("method", "Choose how it was paid.");
        var (currency, tz) = await centre.GetAsync(ct);
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz));
        if (r.PaidOn > today.AddDays(1)) throw RequestValidationException.For("paidOn", "The payment date cannot be in the future.");
        var payment = new TeacherPayment
        {
            TeacherId = teacherId,
            Amount = decimal.Round(r.Amount, 2),
            CurrencyCode = currency,
            PaidOn = r.PaidOn,
            Method = r.Method,
            PeriodFrom = r.PeriodFrom,
            PeriodTo = r.PeriodTo,
            Reference = Text.Optional(r.Reference, "reference", 100),
            Note = Text.Optional(r.Note, "note", 300),
            RecordedByUserId = user.UserId,
        };
        db.TeacherPayments.Add(payment);
        audit.Record("teacher.salary_paid", "Teacher", teacherId, $"{currency} {payment.Amount} on {payment.PaidOn:yyyy-MM-dd} ({payment.Method})");
        await db.SaveChangesAsync(ct);
        return payment.Id;
    }

    public async Task VoidPaymentAsync(Guid teacherId, Guid paymentId, VoidTeacherPaymentRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var reason = Text.Required(r.Reason, "reason", 300);
        var p = await db.TeacherPayments.FirstOrDefaultAsync(x => x.Id == paymentId && x.TeacherId == teacherId, ct) ?? throw new NotFoundException("Payment");
        if (p.VoidedAtUtc is not null) throw new ConflictException("This payment was already removed.", "already_voided");
        (p.VoidedAtUtc, p.VoidedByUserId, p.VoidReason) = (clock.UtcNow, user.UserId, reason);
        audit.Record("teacher.salary_voided", "Teacher", teacherId, $"{p.CurrencyCode} {p.Amount} of {p.PaidOn:yyyy-MM-dd} removed: {reason}");
        await db.SaveChangesAsync(ct);
    }

}
