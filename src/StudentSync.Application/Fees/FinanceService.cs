using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Common;
using StudentSync.Domain.Fees;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Fees;

/// <summary>
/// The centre's money (owner request 2026-11-24): income (fee payments), salaries paid, other expenses and profit for a
/// period, a 12-month trend, one list of every payment in and out, and balances left open by people who are no longer
/// active. Cash basis: money counts on the day it was received or paid. Admin only.
/// </summary>
public sealed class FinanceService(IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, AuditService audit, FeeService fees, SalaryService salaries)
{
    public const int MaxEntries = 300;
    private const int MaxRangeDays = 366 * 3;

    // ---------------- Summary ----------------

    public async Task<FinanceSummaryDto> SummaryAsync(DateOnly? fromDay, DateOnly? toDay, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var (currency, tz) = await CentreAsync(ct);
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz));
        var from = fromDay ?? new DateOnly(today.Year, today.Month, 1);
        var to = toDay ?? from.AddMonths(1).AddDays(-1);
        if (to < from) throw RequestValidationException.For("to", "The end day is before the start day.");
        if (to.DayNumber - from.DayNumber > MaxRangeDays) throw RequestValidationException.For("to", "Choose a period of at most three years.");

        // Trend: the 12 months ending with the month of "to". The period itself is read from the same rows when it fits.
        var trendStart = new DateOnly(to.Year, to.Month, 1).AddMonths(-11);
        var trendEnd = new DateOnly(to.Year, to.Month, 1).AddMonths(1).AddDays(-1);
        var lo = from < trendStart ? from : trendStart;
        var hi = to > trendEnd ? to : trendEnd;

        var income = await db.FeePayments.Where(p => p.VoidedAtUtc == null && p.PaidOn >= lo && p.PaidOn <= hi)
            .Select(p => new { p.PaidOn, p.Amount }).ToListAsync(ct);
        var paid = await db.TeacherPayments.Where(p => p.VoidedAtUtc == null && p.PaidOn >= lo && p.PaidOn <= hi)
            .Select(p => new { p.PaidOn, p.Amount }).ToListAsync(ct);
        var spent = await db.Expenses.Where(e => e.SpentOn >= lo && e.SpentOn <= hi)
            .Select(e => new { e.SpentOn, e.Amount, e.Category }).ToListAsync(ct);

        bool In(DateOnly d) => d >= from && d <= to;
        var incomeTotal = income.Where(x => In(x.PaidOn)).Sum(x => x.Amount);
        var salaryTotal = paid.Where(x => In(x.PaidOn)).Sum(x => x.Amount);
        var expenseTotal = spent.Where(x => In(x.SpentOn)).Sum(x => x.Amount);
        var profit = FinanceRules.Profit(incomeTotal, salaryTotal, expenseTotal);

        var byCategory = spent.Where(x => In(x.SpentOn)).GroupBy(x => x.Category)
            .Select(g => new CategoryTotalDto(g.Key, g.Sum(x => x.Amount))).OrderByDescending(x => x.Amount).ToList();
        var months = Enumerable.Range(0, 12).Select(i =>
        {
            var m = trendStart.AddMonths(i);
            var end = m.AddMonths(1);
            bool InMonth(DateOnly d) => d >= m && d < end;
            var inc = income.Where(x => InMonth(x.PaidOn)).Sum(x => x.Amount);
            var sal = paid.Where(x => InMonth(x.PaidOn)).Sum(x => x.Amount);
            var exp = spent.Where(x => InMonth(x.SpentOn)).Sum(x => x.Amount);
            return new FinanceMonthDto(m, inc, sal, exp, FinanceRules.Profit(inc, sal, exp));
        }).ToList();

        var (total, entries) = await EntriesAsync(from, to, ct);
        var toCollect = (await fees.DueAsync(1, ct)).TotalDue;
        var owed = (await salaries.MonthAsync(today, ct)).Sum(r => r.DueNow);

        return new FinanceSummaryDto(from, to, currency, incomeTotal, salaryTotal, expenseTotal, profit, FinanceRules.MarginPercent(incomeTotal, profit),
            toCollect, owed, byCategory, months, total, entries);
    }

    /// <summary>Every payment received, salary paid and expense in the period, newest first (the newest <see cref="MaxEntries"/>).</summary>
    private async Task<(int Total, List<FinanceEntryDto> Items)> EntriesAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var income = db.FeePayments.Where(p => p.VoidedAtUtc == null && p.PaidOn >= from && p.PaidOn <= to);
        var salary = db.TeacherPayments.Where(p => p.VoidedAtUtc == null && p.PaidOn >= from && p.PaidOn <= to);
        var spent = db.Expenses.Where(e => e.SpentOn >= from && e.SpentOn <= to);
        var total = await income.CountAsync(ct) + await salary.CountAsync(ct) + await spent.CountAsync(ct);

        var inc = await income.OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.CreatedAtUtc).Take(MaxEntries)
            .Select(p => new FinanceEntryDto(p.PaidOn, FinanceEntryKind.Income,
                db.Students.Where(s => s.Id == p.StudentId).Select(s => s.User!.FullName).FirstOrDefault() ?? "Student",
                db.StudentPackages.Where(k => k.Id == p.PackageId).Select(k => k.Name).FirstOrDefault(), p.Amount, p.StudentId, null)).ToListAsync(ct);
        var sal = await salary.OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.CreatedAtUtc).Take(MaxEntries)
            .Select(p => new FinanceEntryDto(p.PaidOn, FinanceEntryKind.Salary,
                db.Teachers.Where(t => t.Id == p.TeacherId).Select(t => t.User!.FullName).FirstOrDefault() ?? "Teacher",
                p.Note, p.Amount, p.TeacherId, null)).ToListAsync(ct);
        var exp = await spent.OrderByDescending(e => e.SpentOn).ThenByDescending(e => e.CreatedAtUtc).Take(MaxEntries)
            .Select(e => new { e.Id, e.SpentOn, e.Category, e.Description, e.PaidTo, e.Amount }).ToListAsync(ct);
        var items = inc.Concat(sal)
            .Concat(exp.Select(e => new FinanceEntryDto(e.SpentOn, FinanceEntryKind.Expense, e.Description, CategoryLabel(e.Category) + (e.PaidTo is { } to2 ? $" · {to2}" : ""), e.Amount, null, e.Id)))
            .OrderByDescending(x => x.Date).ThenBy(x => x.Kind).Take(MaxEntries).ToList();
        return (total, items);
    }

    public static string CategoryLabel(ExpenseCategory c) => c switch
    {
        ExpenseCategory.Utilities => "Electricity & water",
        ExpenseCategory.InternetPhone => "Internet & phone",
        ExpenseCategory.Supplies => "Books & supplies",
        ExpenseCategory.StaffSalaries => "Other staff pay",
        _ => c.ToString(),
    };

    // ---------------- Balances of people who left ----------------

    /// <summary>
    /// Students who are no longer active but still owe fees, and teachers who are no longer active with salary still owed
    /// to them (or paid in advance). Shown apart from the current lists so these amounts are never forgotten.
    /// </summary>
    public async Task<FormerBalancesDto> FormerAsync(CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var (currency, tz) = await CentreAsync(ct);
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz));

        var (balances, _) = await fees.BalancesAsync(today, includeInactive: true, ct);
        var owing = balances.Where(b => !b.Active && b.Due > 0).ToList();
        var ids = owing.Select(b => b.StudentId).ToList();
        var people = await db.Students.Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.User!.FullName, s.StudentCode, s.Status }).ToDictionaryAsync(s => s.Id, ct);
        var students = owing.Where(b => people.ContainsKey(b.StudentId))
            .Select(b => { var p = people[b.StudentId]; return new FormerStudentBalanceDto(b.StudentId, p.FullName, p.StudentCode, p.Status, b.Due, b.DueOn, b.OverdueDays); })
            .OrderByDescending(x => x.DaysOverdue).ThenByDescending(x => x.Due).ToList();

        var former = await db.Teachers.Where(t => t.Status != PersonStatus.Active).Select(t => new { t.Id, t.User!.FullName, t.Status }).ToListAsync(ct);
        var tids = former.Select(t => t.Id).ToList();
        var rates = (await db.TeacherPayRates.Where(r => tids.Contains(r.TeacherId)).Select(r => new { r.TeacherId, r.EffectiveFrom, r.HourlyRate }).ToListAsync(ct))
            .GroupBy(r => r.TeacherId).ToDictionary(g => g.Key, g => (IReadOnlyList<TeacherPayRules.Rate>)g.Select(r => new TeacherPayRules.Rate(r.EffectiveFrom, r.HourlyRate)).ToList());
        var taught = (await db.Sessions.Where(s => tids.Contains(s.TeacherId) && s.Status == SessionStatus.Completed).Select(s => new { s.TeacherId, s.StartUtc, s.EndUtc }).ToListAsync(ct))
            .GroupBy(s => s.TeacherId)
            .ToDictionary(g => g.Key, g => g.Select(s => new TeacherPayRules.TaughtClass(DateOnly.FromDateTime(ZonedTime.ToLocal(s.StartUtc, tz)), TeacherPayRules.Hours(s.StartUtc, s.EndUtc))).ToList());
        var paidBy = (await db.TeacherPayments.Where(p => tids.Contains(p.TeacherId) && p.VoidedAtUtc == null).Select(p => new { p.TeacherId, p.Amount }).ToListAsync(ct))
            .GroupBy(p => p.TeacherId).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var teachers = former.Select(t =>
        {
            var earned = TeacherPayRules.Earned(taught.GetValueOrDefault(t.Id) ?? [], rates.GetValueOrDefault(t.Id) ?? []);
            var paidTotal = paidBy.GetValueOrDefault(t.Id);
            return new FormerTeacherBalanceDto(t.Id, t.FullName, t.Status, earned, paidTotal, FinanceRules.TeacherBalance(earned, paidTotal));
        }).Where(x => x.Balance != 0).OrderByDescending(x => Math.Abs(x.Balance)).ToList();

        return new FormerBalancesDto(currency, students.Sum(s => s.Due), teachers.Where(t => t.Balance > 0).Sum(t => t.Balance),
            -teachers.Where(t => t.Balance < 0).Sum(t => t.Balance), students, teachers);
    }

    // ---------------- Expenses ----------------

    public async Task<ExpenseDto> ExpenseAsync(Guid id, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        return await db.Expenses.Where(e => e.Id == id)
            .Select(e => new ExpenseDto(e.Id, e.SpentOn, e.Category, e.Amount, e.CurrencyCode, e.Description, e.PaidTo, e.Method, e.Reference,
                db.Users.Where(u => u.Id == e.RecordedByUserId).Select(u => u.FullName).FirstOrDefault()))
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Expense");
    }

    public async Task<Guid> SaveExpenseAsync(Guid? id, SaveExpenseRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        try { Expense.Validate(r.Amount); }
        catch (DomainException ex) { throw RequestValidationException.For("amount", ex.Message); }
        if (!Enum.IsDefined(r.Category)) throw RequestValidationException.For("category", "Choose a category.");
        if (!Enum.IsDefined(r.Method) || r.Method == PaymentMethod.Online) throw RequestValidationException.For("method", "Choose how it was paid.");
        var (currency, tz) = await CentreAsync(ct);
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz));
        if (r.SpentOn > today) throw RequestValidationException.For("spentOn", "The day cannot be in the future.");
        if (r.SpentOn < today.AddYears(-5)) throw RequestValidationException.For("spentOn", "The day must be within the last five years.");

        Expense e;
        string? before = null;
        if (id is { } eid)
        {
            e = await db.Expenses.FirstOrDefaultAsync(x => x.Id == eid, ct) ?? throw new NotFoundException("Expense");
            before = $"{e.SpentOn:yyyy-MM-dd} {e.Category} {e.Amount:0.##}";
        }
        else db.Expenses.Add(e = new Expense { RecordedByUserId = user.UserId, CurrencyCode = currency });
        e.SpentOn = r.SpentOn;
        e.Category = r.Category;
        e.Amount = decimal.Round(r.Amount, 2);
        e.Description = Text.Required(r.Description, "description", 300);
        e.PaidTo = Text.Optional(r.PaidTo, "paidTo", 150);
        e.Method = r.Method;
        e.Reference = Text.Optional(r.Reference, "reference", 100);
        var now = $"{e.SpentOn:yyyy-MM-dd} {e.Category} {e.Amount:0.##} {currency} · {e.Description}";
        audit.Record(id is null ? "finance.expense_added" : "finance.expense_updated", "Expense", e.Id, before is null ? now : $"{before} -> {now}");
        await db.SaveChangesAsync(ct);
        return e.Id;
    }

    public async Task DeleteExpenseAsync(Guid id, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var e = await db.Expenses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Expense");
        db.Expenses.Remove(e);
        audit.Record("finance.expense_deleted", "Expense", e.Id, $"{e.SpentOn:yyyy-MM-dd} {e.Category} {e.Amount:0.##} {e.CurrencyCode} · {e.Description}");
        await db.SaveChangesAsync(ct);
    }

    private async Task<(string Currency, string TimeZoneId)> CentreAsync(CancellationToken ct)
    {
        var t = await db.Tenants.Where(x => x.Id == user.TenantId).Select(x => new { x.CurrencyCode, x.DefaultTimeZoneId }).FirstOrDefaultAsync(ct);
        return (t?.CurrencyCode ?? "USD", t?.DefaultTimeZoneId ?? "UTC");
    }
}
