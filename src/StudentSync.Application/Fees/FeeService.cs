using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Common;
using StudentSync.Domain.Fees;
using StudentSync.Domain.Identity;
using StudentSync.Domain.People;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Fees;

/// <summary>
/// Prepaid hour packages and the money received for them (addendum A). Money and hours stay separate: payments are
/// stored records, hours used are counted from attendance (present or late, class not cancelled), so a class can never
/// be charged twice. Admin only; nothing here blocks a student — the centre is warned and decides (owner decision).
/// </summary>
public sealed class FeeService(IAppDbContext db, ICurrentUser user, AccessPolicy access, IClock clock, AuditService audit, PhotoUrls photos, NotificationService notify,
    IServiceScopeFactory scopes, ILogger<FeeService> logger)
{
    /// <summary>"Running out of hours" at or below this many hours left.</summary>
    public const decimal LowHoursAt = 2m;
    private const int MaxDueItems = 200;

    public async Task<StudentFeesDto> ForStudentAsync(Guid studentId, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        if (!await db.Students.AnyAsync(s => s.Id == studentId, ct)) throw new NotFoundException("Student");
        var (currency, _) = await CentreAsync(ct);

        var packages = await db.StudentPackages.Where(p => p.StudentId == studentId)
            .OrderByDescending(p => p.StartsOn).ThenByDescending(p => p.CreatedAtUtc)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Hours,
                p.Price,
                p.CurrencyCode,
                p.StartsOn,
                p.PaymentDueOn,
                p.Status,
                p.Notes,
                p.CountsFromUtc,
                Payments = p.Payments.OrderByDescending(x => x.PaidOn).ThenByDescending(x => x.CreatedAtUtc)
                    .Select(x => new FeePaymentDto(x.Id, x.Amount, x.PaidOn, x.Method, x.Reference,
                        db.Users.Where(u => u.Id == x.RecordedByUserId).Select(u => u.FullName).FirstOrDefault(), x.VoidedAtUtc != null, x.VoidReason))
                    .ToList(),
            })
            .Take(100).ToListAsync(ct);

        var dtos = packages.Select(p =>
        {
            var paid = p.Payments.Where(x => !x.Voided).Sum(x => x.Amount);
            return new StudentPackageDto(p.Id, p.Name, p.Hours, p.Price, p.CurrencyCode, p.StartsOn, p.PaymentDueOn ?? p.StartsOn, p.Status,
                paid, p.Status == PackageStatus.Active ? Math.Max(0, p.Price - paid) : 0, p.Notes, p.Payments);
        }).ToList();
        var active = packages.Where(p => p.Status == PackageStatus.Active).ToList();
        var activeDtos = dtos.Where(d => d.Status == PackageStatus.Active).ToList();

        var usage = new List<HoursUsedDto>();
        if (active.Count > 0)
        {
            var from = active.Min(p => p.CountsFromUtc);
            usage = (await AttendedClasses().Where(x => x.StudentId == studentId && x.StartUtc >= from)
                    .OrderByDescending(x => x.StartUtc)
                    .Select(x => new { x.SessionId, x.Title, x.StartUtc, x.EndUtc, x.Status }).ToListAsync(ct))
                .Select(x => new HoursUsedDto(x.SessionId, x.Title, x.StartUtc, HoursOf(x.StartUtc, x.EndUtc), x.Status)).ToList();
        }

        var bought = active.Sum(p => p.Hours);
        var used = usage.Sum(u => u.Hours);
        var billed = activeDtos.Sum(p => p.Price);
        var paidTotal = activeDtos.Sum(p => p.Paid);
        var oldestDue = activeDtos.Where(p => p.Balance > 0).Select(p => (DateOnly?)p.DueOn).Min();
        var rate = await db.Students.Where(s => s.Id == studentId).Select(s => s.HourlyRate).FirstOrDefaultAsync(ct);
        return new StudentFeesDto(studentId, currency, bought, used, bought - used, billed, paidTotal, activeDtos.Sum(p => p.Balance), oldestDue, dtos, usage.Take(20).ToList(), rate);
    }

    /// <summary>The student's fee per hour (empty clears it). New packages are priced hours × rate; existing sales keep their price.</summary>
    public async Task SetRateAsync(Guid studentId, SetStudentRateRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var s = await db.Students.FirstOrDefaultAsync(x => x.Id == studentId, ct) ?? throw new NotFoundException("Student");
        if (r.HourlyRate is < 0 or > 1_000_000) throw RequestValidationException.For("hourlyRate", "Enter a valid rate per hour.");
        var before = s.HourlyRate;
        s.HourlyRate = r.HourlyRate is { } v ? decimal.Round(v, 2) : null;
        var (currency, _) = await CentreAsync(ct);
        audit.Record("student.fee_rate_set", "Student", studentId, $"{currency} {before?.ToString() ?? "—"} -> {s.HourlyRate?.ToString() ?? "—"} per hour");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Students who owe money or are running out of hours, most urgent first. <paramref name="take"/> bounds the list (max 200).</summary>
    public async Task<FeesDueDto> DueAsync(int? take, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var (currency, tz) = await CentreAsync(ct);
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz));
        var (balances, _) = await BalancesAsync(today, ct);
        if (balances.Count == 0) return new FeesDueDto(currency, 0, 0, 0, 0, LowHoursAt, await CollectedThisMonthAsync(today, ct), 0, []);

        var rows = balances.Where(r => r.Due > 0 || r.Left <= LowHoursAt)
            .OrderByDescending(r => r.OverdueDays).ThenByDescending(r => r.Due).ThenBy(r => r.Left)
            .ToList();
        var limit = Math.Clamp(take ?? 50, 1, MaxDueItems);
        var ids = rows.Take(limit).Select(r => r.StudentId).ToList();
        var people = await db.Students.Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.User!.FullName, s.StudentCode, s.User.PhotoFileId }).ToDictionaryAsync(s => s.Id, ct);
        var items = rows.Take(limit).Where(r => people.ContainsKey(r.StudentId)).Select(r =>
        {
            var p = people[r.StudentId];
            return new FeeDueItemDto(r.StudentId, p.FullName, p.StudentCode, photos.For(p.PhotoFileId), r.Due, r.DueOn, r.OverdueDays, r.Left);
        }).ToList();

        return new FeesDueDto(currency, rows.Sum(r => r.Due), rows.Count(r => r.Due > 0), rows.Count(r => r.OverdueDays > 0), rows.Count(r => r.Left <= LowHoursAt),
            LowHoursAt, await CollectedThisMonthAsync(today, ct), rows.Count, items);
    }

    /// <summary>A student with at least one active package: what is owed, since when, and the hours left.</summary>
    public sealed record StudentBalance(Guid StudentId, decimal Due, DateOnly? DueOn, int OverdueDays, decimal Bought, decimal Left, Guid LatestPackageId);
    /// <summary>An active package that is not fully paid.</summary>
    public sealed record OpenPackage(Guid PackageId, Guid StudentId, string Name, decimal Price, decimal Paid, string CurrencyCode, DateOnly DueOn)
    {
        public decimal Balance => Price - Paid;
    }

    /// <summary>
    /// Balances of every active student with an active package (shared by the Fees page and the automations; the caller
    /// checks permissions). Hours used = attended classes since each student's first active package.
    /// </summary>
    public async Task<(List<StudentBalance> Students, List<OpenPackage> Packages)> BalancesAsync(DateOnly today, CancellationToken ct)
    {
        var active = db.StudentPackages.Where(p => p.Status == PackageStatus.Active && p.Student!.Status == PersonStatus.Active);
        var packages = await active.Select(p => new
        {
            p.Id,
            p.StudentId,
            p.Name,
            p.Hours,
            p.Price,
            p.CurrencyCode,
            p.StartsOn,
            p.PaymentDueOn,
            p.CountsFromUtc,
            p.CreatedAtUtc,
            Paid = p.Payments.Where(x => x.VoidedAtUtc == null).Sum(x => (decimal?)x.Amount) ?? 0,
        }).ToListAsync(ct);
        if (packages.Count == 0) return ([], []);

        // Duration is summed here because the provider-neutral query cannot subtract dates; rows are bounded by classes
        // taken since those packages began.
        var starts = active.GroupBy(p => p.StudentId).Select(g => new { StudentId = g.Key, From = g.Min(p => p.CountsFromUtc) });
        var attended = await (from x in AttendedClasses()
                              join s in starts on x.StudentId equals s.StudentId
                              where x.StartUtc >= s.From
                              select new { x.StudentId, x.StartUtc, x.EndUtc }).ToListAsync(ct);
        var usedBy = attended.GroupBy(x => x.StudentId).ToDictionary(g => g.Key, g => g.Sum(x => HoursOf(x.StartUtc, x.EndUtc)));

        var open = packages.Where(p => p.Price > p.Paid)
            .Select(p => new OpenPackage(p.Id, p.StudentId, p.Name, p.Price, p.Paid, p.CurrencyCode, p.PaymentDueOn ?? p.StartsOn)).ToList();
        var students = packages.GroupBy(p => p.StudentId).Select(g =>
        {
            var owing = open.Where(o => o.StudentId == g.Key).ToList();
            DateOnly? dueOn = owing.Count == 0 ? null : owing.Min(o => o.DueOn);
            var bought = g.Sum(p => p.Hours);
            return new StudentBalance(g.Key, owing.Sum(o => o.Balance), dueOn, dueOn is { } d && d < today ? today.DayNumber - d.DayNumber : 0,
                bought, bought - usedBy.GetValueOrDefault(g.Key), g.OrderByDescending(p => p.CreatedAtUtc).First().Id);
        }).ToList();
        return (students, open);
    }

    public async Task<Guid> AddPackageAsync(Guid studentId, SavePackageRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var student = await db.Students.Where(s => s.Id == studentId).Select(s => new { s.Id, s.User!.FullName }).FirstOrDefaultAsync(ct)
                      ?? throw new NotFoundException("Student");
        var (currency, tz) = await CentreAsync(ct);
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz));
        Guard(() => StudentPackage.Validate(r.Hours, r.Price), "hours");
        var starts = r.StartsOn ?? today;
        if (Math.Abs(starts.DayNumber - today.DayNumber) > 366) throw RequestValidationException.For("startsOn", "The start day must be within a year of today.");
        if (r.PaymentDueOn is { } due && (due < starts.AddDays(-366) || due > starts.AddDays(366)))
            throw RequestValidationException.For("paymentDueOn", "The payment day must be within a year of the start day.");
        if (r.PaidNow is { } now && (now <= 0 || now > r.Price)) throw RequestValidationException.For("paidNow", "The amount paid now must be more than zero and not more than the price.");

        var package = new StudentPackage
        {
            StudentId = studentId,
            Name = Text.Optional(r.Name, "name", 100) ?? $"{r.Hours:0.#} hours",
            Hours = r.Hours,
            Price = decimal.Round(r.Price, 2),
            CurrencyCode = currency,
            StartsOn = starts,
            CountsFromUtc = ZonedTime.ToUtc(starts, TimeOnly.MinValue, tz).Utc,
            PaymentDueOn = r.PaymentDueOn,
            Notes = Text.Optional(r.Notes, "notes", 500),
        };
        if (r.PaidNow is { } amount)
            package.Payments.Add(NewPayment(studentId, amount, today, r.Method, r.Reference));
        db.StudentPackages.Add(package);
        foreach (var paid in package.Payments) await ReceiptNoticeAsync(paid, currency, ct);
        audit.Record("fees.package_added", "Student", studentId, $"{package.Name} · {package.Price:0.##} {currency}" + (r.PaidNow is { } p ? $" · paid {p:0.##}" : ""));
        await db.SaveChangesAsync(ct);
        return package.Id;
    }

    public async Task<Guid> RecordPaymentAsync(Guid studentId, Guid packageId, RecordPaymentRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var package = await db.StudentPackages.Include(p => p.Payments).FirstOrDefaultAsync(p => p.Id == packageId && p.StudentId == studentId, ct)
                      ?? throw new NotFoundException("Package");
        if (package.Status != PackageStatus.Active) throw new DomainException("This package was cancelled.", "package_cancelled");
        var (_, tz) = await CentreAsync(ct);
        var today = DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, tz));
        var paidOn = r.PaidOn ?? today;
        if (paidOn > today) throw RequestValidationException.For("paidOn", "The payment day cannot be in the future.");
        if (paidOn < today.AddDays(-366)) throw RequestValidationException.For("paidOn", "The payment day must be within the last year.");
        var balance = package.Price - package.Payments.Where(x => x.VoidedAtUtc == null).Sum(x => x.Amount);
        if (r.Amount <= 0) throw RequestValidationException.For("amount", "Enter the amount received.");
        if (r.Amount > balance) throw RequestValidationException.For("amount", balance <= 0 ? "This package is already fully paid." : $"Only {balance:0.##} is due on this package.");

        var payment = NewPayment(studentId, r.Amount, paidOn, r.Method, r.Reference);
        package.Payments.Add(payment);
        // The id is set up front (receipt link), so EF must be told this is a new row, not an existing one.
        db.FeePayments.Add(payment);
        await ReceiptNoticeAsync(payment, package.CurrencyCode, ct);
        audit.Record("fees.payment_recorded", "Student", studentId, $"{payment.Amount:0.##} {package.CurrencyCode} · {r.Method} · {package.Name}");
        await db.SaveChangesAsync(ct);
        await CloseLinksAsync(packageId, ct);
        return payment.Id;
    }

    /// <summary>Marks a payment as a mistake. It stays on record (who, when, why) and no longer counts as paid.</summary>
    public async Task VoidPaymentAsync(Guid studentId, Guid paymentId, VoidPaymentRequest r, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var payment = await db.FeePayments.FirstOrDefaultAsync(x => x.Id == paymentId && x.StudentId == studentId, ct) ?? throw new NotFoundException("Payment");
        if (payment.VoidedAtUtc is not null) throw new DomainException("This payment was already removed.", "payment_voided");
        payment.VoidReason = Text.Required(r.Reason, "reason", 300);
        payment.VoidedAtUtc = clock.UtcNow;
        payment.VoidedByUserId = user.UserId;
        audit.Record("fees.payment_voided", "Student", studentId, $"{payment.Amount:0.##} · {payment.VoidReason}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Cancels a package recorded by mistake. Only allowed while no valid payment is on it (remove those first).</summary>
    public async Task CancelPackageAsync(Guid studentId, Guid packageId, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var package = await db.StudentPackages.Include(p => p.Payments).FirstOrDefaultAsync(p => p.Id == packageId && p.StudentId == studentId, ct)
                      ?? throw new NotFoundException("Package");
        if (package.Status == PackageStatus.Cancelled) return;
        if (package.Payments.Any(x => x.VoidedAtUtc == null))
            throw new DomainException("Remove the payments on this package before cancelling it.", "package_has_payments");
        package.Status = PackageStatus.Cancelled;
        package.CancelledAtUtc = clock.UtcNow;
        audit.Record("fees.package_cancelled", "Student", studentId, package.Name);
        await db.SaveChangesAsync(ct);
        await CloseLinksAsync(packageId, ct);
    }

    /// <summary>
    /// After a payment by hand or a cancellation, older online payment links for the package must not stay payable (the
    /// amount changed or nothing is owed). Best effort after the change is saved: a gateway problem never undoes it.
    /// Resolved lazily to avoid a constructor cycle with <see cref="PaymentLinkService"/>.
    /// </summary>
    private async Task CloseLinksAsync(Guid packageId, CancellationToken ct)
    {
        if (!await db.PaymentLinks.AnyAsync(l => l.PackageId == packageId && l.Status == PaymentLinkStatus.Open, ct)) return;
        try
        {
            using var scope = scopes.CreateScope();
            using var _ = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(user.TenantId!.Value);
            await scope.ServiceProvider.GetRequiredService<PaymentLinkService>().CloseOpenLinksAsync(packageId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The 15-minute check keeps watching any link still open, and flags a payment that was not owed.
            logger.LogWarning(ex, "Open payment links for package {Package} could not be closed", packageId);
        }
    }

    /// <summary>The student is told and can open the printable receipt.</summary>
    private Task ReceiptNoticeAsync(FeePayment payment, string currency, CancellationToken ct) =>
        notify.NotifyAsync([payment.StudentId], Domain.Communication.NotificationType.PaymentReceived, $"Payment received: {Automation.Fmt.Money(payment.Amount, currency)}",
            "Thank you. Your receipt is ready.", $"/receipts/{payment.Id}", $"paid:{payment.Id}", ct);

    /// <summary>Exact minutes, in hours (two decimals). A 45-minute class uses 0.75 h.</summary>
    public static decimal HoursOf(DateTime startUtc, DateTime endUtc) => decimal.Round((decimal)(endUtc - startUtc).TotalMinutes / 60m, 2);

    private IQueryable<AttendedClass> AttendedClasses() =>
        from a in db.Attendance
        where a.Status == AttendanceStatus.Present || a.Status == AttendanceStatus.Late
        join s in db.Sessions on a.SessionId equals s.Id
        where s.Status != SessionStatus.Cancelled
        select new AttendedClass { StudentId = a.StudentId, SessionId = s.Id, Title = s.Title, StartUtc = s.StartUtc, EndUtc = s.EndUtc, Status = a.Status };

    private sealed class AttendedClass
    {
        public Guid StudentId { get; init; }
        public Guid SessionId { get; init; }
        public string Title { get; init; } = "";
        public DateTime StartUtc { get; init; }
        public DateTime EndUtc { get; init; }
        public AttendanceStatus Status { get; init; }
    }

    private FeePayment NewPayment(Guid studentId, decimal amount, DateOnly paidOn, PaymentMethod method, string? reference)
    {
        if (decimal.Round(amount, 2) != amount) throw RequestValidationException.For("amount", "Use at most two decimal places.");
        if (!Enum.IsDefined(method)) throw RequestValidationException.For("method", "Choose how it was paid.");
        // "Online" means "confirmed by the payment gateway" and is only ever recorded by PaymentLinkService.
        if (method == PaymentMethod.Online) throw RequestValidationException.For("method", "Online payments are recorded automatically when the gateway confirms them.");
        return new FeePayment
        {
            Id = Guid.CreateVersion7(), // known before saving: the receipt link uses it
            StudentId = studentId,
            Amount = amount,
            PaidOn = paidOn,
            Method = method,
            Reference = Text.Optional(reference, "reference", 100),
            RecordedByUserId = user.UserId,
        };
    }

    private async Task<decimal> CollectedThisMonthAsync(DateOnly today, CancellationToken ct)
    {
        var first = new DateOnly(today.Year, today.Month, 1);
        return await db.FeePayments.Where(x => x.VoidedAtUtc == null && x.PaidOn >= first && x.PaidOn <= today).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
    }

    private async Task<(string Currency, string TimeZoneId)> CentreAsync(CancellationToken ct)
    {
        var t = await db.Tenants.Where(x => x.Id == user.TenantId).Select(x => new { x.CurrencyCode, x.DefaultTimeZoneId }).FirstOrDefaultAsync(ct);
        return (t?.CurrencyCode ?? "USD", t?.DefaultTimeZoneId ?? "UTC");
    }

    private static void Guard(Action rule, string field)
    {
        try { rule(); }
        catch (DomainException e) { throw RequestValidationException.For(e.Code == "invalid_price" ? "price" : field, e.Message); }
    }
}
