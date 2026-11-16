using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StudentSync.Application.Automation;
using StudentSync.Application.Common;
using StudentSync.Application.Platform;
using StudentSync.Contracts;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Fees;
using StudentSync.Domain.Identity;

namespace StudentSync.Application.Fees;

/// <summary>
/// Online fee payment (owner request 2026-11-17) through the institute's Razorpay or Stripe account. A link is made for
/// what is owed on one package (reused while it is still valid and the amount is unchanged). A payment is recorded only
/// after the server itself asks the gateway — triggered by a signed webhook or by the 15-minute check — never from the
/// payer's browser. Each gateway payment id becomes at most one <see cref="FeePayment"/> (method Online), even when the
/// webhook and the check arrive together (unique index on the payment id).
/// </summary>
public sealed class PaymentLinkService(IAppDbContext db, ITenantContext tenant, AccessPolicy access, IClock clock, AuditService audit,
    IntegrationSettingsService integrations, IIntegrationClients clients, NotificationService notify, PublicLinks publicLinks,
    IServiceScopeFactory scopes, ILogger<PaymentLinkService> logger)
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(14);

    /// <summary>Admin: a link for what is owed on this package (Fees tab → "Payment link").</summary>
    public async Task<PaymentLinkDto> ForPackageAsync(Guid studentId, Guid packageId, CancellationToken ct)
    {
        access.Require(Permissions.UsersManage);
        var package = (await db.StudentPackages.Where(p => p.Id == packageId && p.StudentId == studentId && p.Status == PackageStatus.Active)
                .Select(p => new { p.Id, p.StudentId, p.Name, p.Price, p.CurrencyCode, Due = p.PaymentDueOn ?? p.StartsOn, Paid = p.Payments.Where(x => x.VoidedAtUtc == null).Sum(x => (decimal?)x.Amount) ?? 0 })
                .ToListAsync(ct))
            .Where(p => p.Price > p.Paid).Select(p => new FeeService.OpenPackage(p.Id, p.StudentId, p.Name, p.Price, p.Paid, p.CurrencyCode, p.Due)).FirstOrDefault()
            ?? throw new ConflictException("Nothing is owed on this package.", "nothing_due");
        if (await integrations.ActivePaymentsAsync(ct) is null)
            throw new ConflictException("Online payments are not switched on. Connect Razorpay or Stripe in Settings ▸ Integrations.", "payments_off");
        var link = await EnsureLinkEntityAsync(package, ct) ?? throw new ConflictException("The payment gateway did not create the link. Please try again in a minute.", "gateway_failed");
        return Dto(link);
    }

    /// <summary>
    /// Automations: the link's URL, or null when payments are off or anything failed (the reminder then goes without it).
    /// Runs in its own scope so a failure here can never leave half-saved rows in the caller's unit of work.
    /// </summary>
    public async Task<string?> EnsureLinkAsync(FeeService.OpenPackage package, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            using var _ = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(tenant.TenantId!.Value);
            return (await scope.ServiceProvider.GetRequiredService<PaymentLinkService>().EnsureLinkEntityAsync(package, ct))?.Url;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Payment link for package {Package} could not be made", package.PackageId);
            return null;
        }
    }

    /// <summary>
    /// After a payment is recorded by hand (or the package is cancelled), its open links must not be paid any more: they
    /// are closed at the gateway. A link that cannot be closed stays Open, so the 15-minute check still records (and
    /// flags) any payment made on it.
    /// </summary>
    public async Task CloseOpenLinksAsync(Guid packageId, CancellationToken ct)
    {
        var open = await db.PaymentLinks.Where(l => l.PackageId == packageId && l.Status == PaymentLinkStatus.Open).ToListAsync(ct);
        if (open.Count == 0) return;
        var gateway = await integrations.ActivePaymentsAsync(ct);
        if (gateway is null) return;
        foreach (var link in open.Where(l => l.Provider == gateway.Value.Settings.Provider.ToString()))
            if (await clients.ClosePaymentLinkAsync(gateway.Value.Settings, link.ProviderLinkId, ct)) link.Status = PaymentLinkStatus.Cancelled;
            else logger.LogWarning("Payment link {Link} could not be closed; it stays open and keeps being checked", link.Id);
        await db.SaveChangesAsync(ct);
    }

    private async Task<PaymentLink?> EnsureLinkEntityAsync(FeeService.OpenPackage p, CancellationToken ct)
    {
        var gateway = await integrations.ActivePaymentsAsync(ct);
        if (gateway is null || p.Balance <= 0) return null;
        var now = clock.UtcNow;
        var provider = gateway.Value.Settings.Provider.ToString();
        var open = await db.PaymentLinks.Where(l => l.PackageId == p.PackageId && l.Status == PaymentLinkStatus.Open).ToListAsync(ct);
        var reusable = open.FirstOrDefault(l => l.Provider == provider && l.Amount == p.Balance && l.ExpiresAtUtc > now.AddDays(1));
        if (reusable is not null) return reusable;

        // Any other open link (old amount, about to expire, other gateway) is closed first. If one cannot be closed, no new
        // link is made: two payable links for one debt would invite paying twice.
        foreach (var old in open)
        {
            if (old.Provider == provider && !await clients.ClosePaymentLinkAsync(gateway.Value.Settings, old.ProviderLinkId, ct))
            {
                logger.LogWarning("Older payment link {Link} could not be closed; not creating a new one", old.Id);
                return null;
            }
            old.Status = PaymentLinkStatus.Cancelled;
        }
        if (open.Count > 0) await db.SaveChangesAsync(ct);

        var student = await db.Students.Where(s => s.Id == p.StudentId).Select(s => new { s.User!.FullName, s.Phone, s.GuardianPhone }).FirstAsync(ct);
        var centre = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => new { t.Name, t.CountryCode }).FirstAsync(ct);
        var id = Guid.CreateVersion7();
        var result = await clients.CreatePaymentLinkAsync(gateway.Value.Settings, new GatewayLinkRequest(
            MinorUnits(p.Balance), p.CurrencyCode, $"{centre.Name}: {p.Name} for {student.FullName}", id.ToString("N"), student.FullName,
            PhoneNumbers.ToE164(student.GuardianPhone, centre.CountryCode) ?? PhoneNumbers.ToE164(student.Phone, centre.CountryCode),
            now.Add(LinkLifetime), publicLinks.WebBaseUrl is { } web ? $"{web}/payment-done" : null), ct);
        if (!result.Ok || result.Id is null || result.Url is null)
        {
            logger.LogWarning("Gateway refused a payment link: {Error}", result.Error);
            return null;
        }
        var link = new PaymentLink
        {
            Id = id,
            TenantId = tenant.TenantId!.Value,
            PackageId = p.PackageId,
            StudentId = p.StudentId,
            Provider = provider,
            ProviderLinkId = result.Id,
            Url = result.Url,
            Amount = p.Balance,
            CurrencyCode = p.CurrencyCode,
            Status = PaymentLinkStatus.Open,
            ExpiresAtUtc = now.Add(LinkLifetime),
            CreatedAtUtc = now,
        };
        db.PaymentLinks.Add(link);
        audit.Record("fees.payment_link_created", "Student", p.StudentId, $"{link.Provider} link for {p.Balance:0.##} {p.CurrencyCode} · {p.Name}");
        try { await db.SaveChangesAsync(ct); }
        catch
        {
            // Not recorded here (e.g. a second click made one at the same moment: one open link per package): the gateway
            // link must not stay payable without a record.
            await clients.ClosePaymentLinkAsync(gateway.Value.Settings, result.Id, CancellationToken.None);
            throw;
        }
        return link;
    }

    /// <summary>Job (every 15 minutes): asks the gateway about open links; records payments, closes expired links.</summary>
    public async Task<int> CheckOpenLinksAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        // Never-checked links first (PostgreSQL sorts NULLs last), then the longest unchecked.
        var open = await db.PaymentLinks.Where(l => l.Status == PaymentLinkStatus.Open && (l.LastCheckedAtUtc == null || l.LastCheckedAtUtc < now.AddMinutes(-10)))
            .OrderBy(l => l.LastCheckedAtUtc != null).ThenBy(l => l.LastCheckedAtUtc).Take(50).ToListAsync(ct);
        if (open.Count == 0) return 0;
        var gateway = await integrations.ActivePaymentsAsync(ct);
        if (gateway is null) return 0;
        foreach (var link in open.Where(l => l.Provider == gateway.Value.Settings.Provider.ToString()))
            await CheckAsync(link, gateway.Value.Settings, ct);
        return open.Count;
    }

    /// <summary>Webhook: verify the signature, then confirm with the gateway (the payload is only a hint of which link).</summary>
    public async Task<bool> HandleWebhookAsync(string provider, string body, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        var gateway = await integrations.ActivePaymentsAsync(ct);
        if (gateway is null || !string.Equals(gateway.Value.Settings.Provider.ToString(), provider, StringComparison.OrdinalIgnoreCase)) return false;
        if (gateway.Value.WebhookSecret is not { } secret) return false;
        var valid = gateway.Value.Settings.Provider == PaymentProvider.Razorpay
            ? PaymentWebhooks.VerifyRazorpay(body, headers.GetValueOrDefault("x-razorpay-signature"), secret)
            : PaymentWebhooks.VerifyStripe(body, headers.GetValueOrDefault("stripe-signature"), secret, clock.UtcNow);
        if (!valid) return false;
        var linkId = PaymentWebhooks.LinkIdOf(gateway.Value.Settings.Provider, body);
        if (linkId is null) return true; // a different event: acknowledged, nothing to do
        var link = await db.PaymentLinks.FirstOrDefaultAsync(l => l.ProviderLinkId == linkId && l.Provider == gateway.Value.Settings.Provider.ToString(), ct);
        if (link is null) logger.LogWarning("Webhook for unknown {Provider} payment link {Link}", provider, linkId);
        else if (link.Status != PaymentLinkStatus.Paid) await CheckAsync(link, gateway.Value.Settings, ct);
        return true;
    }

    private async Task CheckAsync(PaymentLink link, PaymentSettings settings, CancellationToken ct)
    {
        var state = await clients.GetPaymentLinkAsync(settings, link.ProviderLinkId, ct);
        link.LastCheckedAtUtc = clock.UtcNow;
        if (state.Ok && state.Paid && state.PaymentId is not null) await MarkPaidAsync(link, state.PaymentId, state.AmountPaidMinor, ct);
        else if (state.Ok && state.Closed) link.Status = PaymentLinkStatus.Expired;
        else if (state.Ok && link.ExpiresAtUtc <= clock.UtcNow && await clients.ClosePaymentLinkAsync(settings, link.ProviderLinkId, ct))
            link.Status = PaymentLinkStatus.Expired; // a link that cannot be closed stays Open and keeps being checked
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkPaidAsync(PaymentLink link, string paymentId, long? amountMinor, CancellationToken ct)
    {
        if (link.Status == PaymentLinkStatus.Paid || await db.FeePayments.AnyAsync(p => p.Method == PaymentMethod.Online && p.Reference == paymentId, ct)) return;
        var amount = amountMinor is { } m ? m / 100m : link.Amount;
        var recorder = link.CreatedByUserId ?? await db.Users.Where(u => u.TenantId == tenant.TenantId && u.Role == UserRole.InstitutionAdmin && u.IsActive)
            .OrderBy(u => u.Id).Select(u => u.Id).FirstAsync(ct);
        var tz = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.DefaultTimeZoneId).FirstAsync(ct);
        // The money arrived, so it is always recorded; if the package no longer owed it, the admins are asked to sort it out.
        var package = await db.StudentPackages.Where(x => x.Id == link.PackageId)
            .Select(x => new { x.Status, x.Price, Paid = x.Payments.Where(y => y.VoidedAtUtc == null).Sum(y => (decimal?)y.Amount) ?? 0 }).FirstAsync(ct);
        var overpaid = package.Status != PackageStatus.Active || package.Paid + amount > package.Price;
        var payment = new FeePayment
        {
            TenantId = link.TenantId,
            PackageId = link.PackageId,
            StudentId = link.StudentId,
            Amount = amount,
            PaidOn = DateOnly.FromDateTime(Domain.Scheduling.ZonedTime.ToLocal(clock.UtcNow, tz)),
            Method = PaymentMethod.Online,
            Reference = paymentId.Length > 100 ? paymentId[..100] : paymentId,
            RecordedByUserId = recorder,
        };
        db.FeePayments.Add(payment);
        (link.Status, link.ProviderPaymentId, link.PaidAtUtc, link.FeePaymentId) = (PaymentLinkStatus.Paid, paymentId, clock.UtcNow, payment.Id);
        audit.Record("fees.online_payment", "Student", link.StudentId, $"{amount:0.##} {link.CurrencyCode} via {link.Provider} ({paymentId}){(overpaid ? " — more than was owed" : "")}");
        var money = Fmt.Money(amount, link.CurrencyCode);
        await notify.NotifyAsync([link.StudentId], NotificationType.PaymentReceived, $"Payment received: {money}",
            "Thank you. Your receipt is ready.", $"/receipts/{payment.Id}", $"paid:{payment.Id}", ct);
        var name = await db.Users.Where(u => u.Id == link.StudentId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "A student";
        var admins = await db.Users.Where(u => u.TenantId == tenant.TenantId && u.Role == UserRole.InstitutionAdmin && u.IsActive).Select(u => u.Id).ToListAsync(ct);
        await notify.NotifyAsync(admins, NotificationType.PaymentReceived, overpaid ? $"Check: {name} paid {money} online, more than was owed" : $"{name} paid {money} online",
            overpaid ? $"The package was already paid or cancelled. Refund it, or keep it towards new hours ({link.Provider} {paymentId})." : $"Recorded automatically ({link.Provider} {paymentId}).",
            $"/students/{link.StudentId}?tab=fees", $"paid:{payment.Id}", ct);
    }

    private static long MinorUnits(decimal amount) => (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
    private static PaymentLinkDto Dto(PaymentLink l) => new(l.Id, l.Url, l.Amount, l.CurrencyCode, l.ExpiresAtUtc, l.Status.ToString(), l.Provider);
}

/// <summary>Gateway webhook signatures (pure, unit-tested). Only a valid signature lets a webhook trigger a check.</summary>
public static class PaymentWebhooks
{
    /// <summary>Razorpay: hex HMAC-SHA256 of the raw body with the webhook secret, in X-Razorpay-Signature.</summary>
    public static bool VerifyRazorpay(string body, string? signature, string secret)
    {
        if (string.IsNullOrEmpty(signature)) return false;
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant()));
    }

    /// <summary>Stripe: "t=…,v1=…"; v1 = hex HMAC-SHA256 of "{t}.{body}"; t within 5 minutes.</summary>
    public static bool VerifyStripe(string body, string? header, string secret, DateTime utcNow)
    {
        if (string.IsNullOrEmpty(header)) return false;
        var parts = header.Split(',', StringSplitOptions.TrimEntries).Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToList();
        var t = parts.FirstOrDefault(p => p[0] == "t")?[1];
        if (t is null || !long.TryParse(t, out var seconds) || Math.Abs((utcNow - DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime).TotalMinutes) > 5) return false;
        var expected = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{t}.{body}"))));
        return parts.Where(p => p[0] == "v1").Any(p => CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(p[1].ToLowerInvariant())));
    }

    /// <summary>The payment link an event is about: Razorpay payload.payment_link.entity.id; Stripe data.object.payment_link.</summary>
    public static string? LinkIdOf(PaymentProvider provider, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (provider == PaymentProvider.Razorpay)
                return root.TryGetProperty("payload", out var pl) && pl.TryGetProperty("payment_link", out var lk) && lk.TryGetProperty("entity", out var en)
                       && en.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            return root.TryGetProperty("data", out var data) && data.TryGetProperty("object", out var obj) && obj.TryGetProperty("payment_link", out var pls)
                   && pls.ValueKind == JsonValueKind.String ? pls.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
}
