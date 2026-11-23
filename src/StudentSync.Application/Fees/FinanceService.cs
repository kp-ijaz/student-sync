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
    private const int MaxRangeDays = 366 * 3;

    // ---------------- Summary ----------------

    public async Task<FinanceSummaryDto> SummaryAsync(DateOnly? fromDay, DateOnly? toDay, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var (currency, tz) = await CentreAsync(ct);
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz));
        var (from, to) = Range(fromDay, toDay, today);
        var (prevFrom, prevTo) = PreviousPeriod(from, to);

        // Trend: the 12 months ending with the month of "to". The period and the previous one are read from the same rows.
        var trendStart = new DateOnly(to.Year, to.Month, 1).AddMonths(-11);
        var trendEnd = new DateOnly(to.Year, to.Month, 1).AddMonths(1).AddDays(-1);
        var lo = new[] { prevFrom, trendStart }.Min();
        var hi = to > trendEnd ? to : trendEnd;

        var income = await db.FeePayments.Where(p => p.VoidedAtUtc == null && p.PaidOn >= lo && p.PaidOn <= hi)
            .Select(p => new { p.PaidOn, p.Amount }).ToListAsync(ct);
        var paid = await db.TeacherPayments.Where(p => p.VoidedAtUtc == null && p.PaidOn >= lo && p.PaidOn <= hi)
            .Select(p => new { p.PaidOn, p.Amount }).ToListAsync(ct);
        var spent = await db.Expenses.Where(e => e.SpentOn >= lo && e.SpentOn <= hi)
            .Select(e => new { e.SpentOn, e.Amount, e.Category }).ToListAsync(ct);

        FinanceTotalsDto Totals(DateOnly a, DateOnly b)
        {
            var inc = income.Where(x => x.PaidOn >= a && x.PaidOn <= b).Sum(x => x.Amount);
            var sal = paid.Where(x => x.PaidOn >= a && x.PaidOn <= b).Sum(x => x.Amount);
            var exp = spent.Where(x => x.SpentOn >= a && x.SpentOn <= b).Sum(x => x.Amount);
            return new FinanceTotalsDto(a, b, inc, sal, exp, FinanceRules.Profit(inc, sal, exp));
        }

        var now = Totals(from, to);
        var byCategory = spent.Where(x => x.SpentOn >= from && x.SpentOn <= to).GroupBy(x => x.Category)
            .Select(g => new CategoryTotalDto(g.Key, g.Sum(x => x.Amount))).OrderByDescending(x => x.Amount).ToList();
        var months = Enumerable.Range(0, 12).Select(i =>
        {
            var m = trendStart.AddMonths(i);
            var t = Totals(m, m.AddMonths(1).AddDays(-1));
            return new FinanceMonthDto(m, t.Income, t.Salaries, t.Expenses, t.Profit);
        }).ToList();

        var toCollect = (await fees.DueAsync(1, ct)).TotalDue;
        var owed = (await salaries.MonthAsync(today, ct)).Sum(r => r.DueNow);

        return new FinanceSummaryDto(from, to, currency, now.Income, now.Salaries, now.Expenses, now.Profit, FinanceRules.MarginPercent(now.Income, now.Profit),
            toCollect, owed, byCategory, months, Totals(prevFrom, prevTo));
    }

    /// <summary>Default: this month. Validated: end not before start, at most three years.</summary>
    private static (DateOnly From, DateOnly To) Range(DateOnly? fromDay, DateOnly? toDay, DateOnly today)
    {
        var from = fromDay ?? new DateOnly(today.Year, today.Month, 1);
        var to = toDay ?? from.AddMonths(1).AddDays(-1);
        if (to < from) throw RequestValidationException.For("to", "The end day is before the start day.");
        if (to.DayNumber - from.DayNumber > MaxRangeDays) throw RequestValidationException.For("to", "Choose a period of at most three years.");
        return (from, to);
    }

    /// <summary>
    /// The period of the same length just before: whole months map to the same number of months before (November → October,
    /// a year → the year before); any other range moves back by its number of days.
    /// </summary>
    public static (DateOnly From, DateOnly To) PreviousPeriod(DateOnly from, DateOnly to)
    {
        if (from.Day == 1 && to.AddDays(1).Day == 1)
        {
            var months = (to.Year - from.Year) * 12 + to.Month - from.Month + 1;
            return (from.AddMonths(-months), from.AddDays(-1));
        }
        var days = to.DayNumber - from.DayNumber + 1;
        return (from.AddDays(-days), from.AddDays(-1));
    }

    // ---------------- Transactions ----------------

    /// <summary>Upper bound of lines read per kind for one query (a single centre stays far below it).</summary>
    public const int MaxRowsPerKind = 5000;

    /// <summary>Fees received, salaries paid and expenses in one list: filtered, searched, sorted and paged on the server.</summary>
    public async Task<FinanceEntriesDto> EntriesAsync(FinanceEntryQuery q, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var (all, kinds) = await MatchingAsync(q, ct);
        var page = all.Skip((q.SafePage - 1) * q.SafePageSize).Take(q.SafePageSize).ToList();
        var moneyIn = all.Where(e => e.Kind == FinanceEntryKind.Income).Sum(e => e.Amount);
        var moneyOut = all.Where(e => e.Kind != FinanceEntryKind.Income).Sum(e => e.Amount);
        return new FinanceEntriesDto(new PagedResult<FinanceEntryDto>(page, q.SafePage, q.SafePageSize, all.Count), moneyIn, moneyOut, moneyIn - moneyOut,
            kinds.GetValueOrDefault(FinanceEntryKind.Income), kinds.GetValueOrDefault(FinanceEntryKind.Salary), kinds.GetValueOrDefault(FinanceEntryKind.Expense));
    }

    /// <summary>The same filtered, sorted lines as a CSV file (amount signed: money in positive, money out negative).</summary>
    public async Task<string> EntriesCsvAsync(FinanceEntryQuery q, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var (all, _) = await MatchingAsync(q, ct);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(CsvText.Row("Date", "Type", "Who / what", "Detail", "Category", "Method", "Reference", "Amount"));
        foreach (var e in all)
            sb.AppendLine(CsvText.Row(e.Date.ToString("yyyy-MM-dd"), FinanceLabels.Kind(e.Kind), e.Title, e.Detail, e.Category is { } c ? FinanceLabels.Category(c) : null,
                e.Method is { } m ? FinanceLabels.Method(m) : null, e.Reference,
                (e.Kind == FinanceEntryKind.Income ? e.Amount : -e.Amount).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
        return sb.ToString();
    }

    /// <summary>
    /// Every line in the period matching search and category, sorted; the kind filter is applied after counting so the tabs
    /// can show how many lines of each kind match. A category narrows the list to expenses.
    /// </summary>
    private async Task<(List<FinanceEntryDto> Items, Dictionary<FinanceEntryKind, int> Kinds)> MatchingAsync(FinanceEntryQuery q, CancellationToken ct)
    {
        var (_, tz) = await CentreAsync(ct);
        var (from, to) = Range(q.From, q.To, DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz)));
        var term = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim().ToLowerInvariant();
        if (term is { Length: > 100 }) throw RequestValidationException.For("search", "Search for at most 100 characters.");
        var rows = new List<FinanceEntryDto>();

        if (q.Category is null)
        {
            var inc = db.FeePayments.Where(p => p.VoidedAtUtc == null && p.PaidOn >= from && p.PaidOn <= to);
            if (term is not null)
                inc = inc.Where(p => db.Students.Any(s => s.Id == p.StudentId && (s.User!.FullName.ToLower().Contains(term) || s.StudentCode.ToLower().Contains(term)))
                    || (p.Reference != null && p.Reference.ToLower().Contains(term))
                    || db.StudentPackages.Any(k => k.Id == p.PackageId && k.Name.ToLower().Contains(term)));
            rows.AddRange(await inc.OrderByDescending(p => p.PaidOn).Take(MaxRowsPerKind)
                .Select(p => new FinanceEntryDto(p.PaidOn, FinanceEntryKind.Income,
                    db.Students.Where(s => s.Id == p.StudentId).Select(s => s.User!.FullName).FirstOrDefault() ?? "Student",
                    db.StudentPackages.Where(k => k.Id == p.PackageId).Select(k => k.Name).FirstOrDefault(), p.Amount, p.StudentId, null,
                    p.Id, p.Method, p.Reference, null)).ToListAsync(ct));

            var sal = db.TeacherPayments.Where(p => p.VoidedAtUtc == null && p.PaidOn >= from && p.PaidOn <= to);
            if (term is not null)
                sal = sal.Where(p => db.Teachers.Any(t => t.Id == p.TeacherId && t.User!.FullName.ToLower().Contains(term))
                    || (p.Note != null && p.Note.ToLower().Contains(term)) || (p.Reference != null && p.Reference.ToLower().Contains(term)));
            rows.AddRange(await sal.OrderByDescending(p => p.PaidOn).Take(MaxRowsPerKind)
                .Select(p => new FinanceEntryDto(p.PaidOn, FinanceEntryKind.Salary,
                    db.Teachers.Where(t => t.Id == p.TeacherId).Select(t => t.User!.FullName).FirstOrDefault() ?? "Teacher",
                    p.Note, p.Amount, p.TeacherId, null, p.Id, p.Method, p.Reference, null)).ToListAsync(ct));
        }

        var exp = db.Expenses.Where(e => e.SpentOn >= from && e.SpentOn <= to);
        if (q.Category is { } cat) exp = exp.Where(e => e.Category == cat);
        if (term is not null)
            exp = exp.Where(e => e.Description.ToLower().Contains(term) || (e.PaidTo != null && e.PaidTo.ToLower().Contains(term))
                || (e.Reference != null && e.Reference.ToLower().Contains(term)));
        var expenses = await exp.OrderByDescending(e => e.SpentOn).Take(MaxRowsPerKind)
            .Select(e => new { e.Id, e.SpentOn, e.Category, e.Description, e.PaidTo, e.Amount, e.Method, e.Reference }).ToListAsync(ct);
        rows.AddRange(expenses.Select(e => new FinanceEntryDto(e.SpentOn, FinanceEntryKind.Expense, e.Description,
            FinanceLabels.Category(e.Category) + (e.PaidTo is { } paidTo ? $" · {paidTo}" : ""), e.Amount, null, e.Id, e.Id, e.Method, e.Reference, e.Category)));

        var kinds = rows.GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => g.Count());
        IEnumerable<FinanceEntryDto> shown = q.Kind is { } kind ? rows.Where(r => r.Kind == kind) : rows;
        shown = (q.Sort ?? FinanceSort.Newest) switch
        {
            FinanceSort.Oldest => shown.OrderBy(r => r.Date).ThenBy(r => r.Kind),
            FinanceSort.Largest => shown.OrderByDescending(r => r.Amount).ThenByDescending(r => r.Date),
            FinanceSort.Smallest => shown.OrderBy(r => r.Amount).ThenByDescending(r => r.Date),
            _ => shown.OrderByDescending(r => r.Date).ThenBy(r => r.Kind),
        };
        return (shown.ToList(), kinds);
    }

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
