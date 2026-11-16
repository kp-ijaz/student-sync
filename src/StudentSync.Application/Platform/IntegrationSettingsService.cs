using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Platform;

namespace StudentSync.Application.Platform;

/// <summary>
/// An institute's own connections (Settings ▸ Integrations, institution admins only): its SMTP server for email, an SMS
/// provider (Twilio or MSG91) and a payment gateway (Razorpay or Stripe). Secrets are encrypted at rest and never returned;
/// an empty secret on save keeps the stored one, and changing provider clears it. "Send test" / "Verify keys" make real
/// calls and only report success when the provider accepted the request. Audit entries never contain secrets.
/// </summary>
public sealed partial class IntegrationSettingsService(IAppDbContext db, ICurrentUser user, AccessPolicy access, AuditService audit,
    IIntegrationSecrets secrets, IIntegrationClients clients, IClock clock, IntegrationFlags flags)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    /// <summary>Tests call real providers (and may cost money per SMS); allow one every few seconds per connection.</summary>
    private static readonly TimeSpan TestCooldown = TimeSpan.FromSeconds(10);

    private sealed record EmailConfig(string? Host, int Port, bool UseStartTls, string? Username, string? FromAddress, string? FromName);
    private sealed record SmsConfig(string? AccountId, string? Sender, string? TemplateId);
    private sealed record PaymentConfig(string? KeyId);

    public async Task<InstitutionIntegrationsDto> GetAsync(CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        var rows = await db.InstitutionIntegrations.ToListAsync(ct);
        return new InstitutionIntegrationsDto(EmailDto(rows.Find(r => r.Kind == IntegrationKind.Email)), SmsDto(rows.Find(r => r.Kind == IntegrationKind.Sms)),
            PaymentDto(rows.Find(r => r.Kind == IntegrationKind.Payments)), flags.Email);
    }

    // ---------------- Email (SMTP) ----------------

    public async Task SaveEmailAsync(SaveEmailIntegrationRequest r, CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        var host = Text.Optional(r.Host, "host", 200);
        var from = Text.Optional(r.FromAddress, "fromAddress", 256);
        var username = Text.Optional(r.Username, "username", 256);
        var fromName = Text.Optional(r.FromName, "fromName", 100);
        if (r.Port is < 1 or > 65535) throw RequestValidationException.For("port", "Use a port between 1 and 65535 (usually 587 or 465).");
        if (host is not null && !HostPattern().IsMatch(host)) throw RequestValidationException.For("host", "Enter the server name only, for example smtp.gmail.com.");
        if (from is not null && !MailAddress.TryCreate(from, out _)) throw RequestValidationException.For("fromAddress", "Enter a valid email address.");
        if (r.Password is { Length: > 200 }) throw RequestValidationException.For("password", "The password is too long.");
        if (r.Enabled && host is null) throw RequestValidationException.For("host", "Enter the SMTP server to switch email on.");
        if (r.Enabled && from is null) throw RequestValidationException.For("fromAddress", "Enter the address emails are sent from.");

        var row = await RowAsync(IntegrationKind.Email, "Smtp", ct);
        var stored = secrets.Unprotect(row.EncryptedSecrets);
        if (!string.IsNullOrEmpty(r.Password)) stored["password"] = r.Password;
        if (string.IsNullOrEmpty(username)) stored.Remove("password"); // no sign-in, nothing to keep
        row.IsEnabled = r.Enabled;
        row.SettingsJson = JsonSerializer.Serialize(new EmailConfig(host, r.Port, r.UseStartTls, username, from, fromName), Json);
        row.EncryptedSecrets = stored.Count == 0 ? null : secrets.Protect(stored);
        audit.Record("integration.email.saved", nameof(InstitutionIntegration), row.Id, $"enabled={r.Enabled} host={host ?? "—"} port={r.Port} from={from ?? "—"}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<IntegrationCheckDto> TestEmailAsync(TestEmailRequest r, CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        var to = Text.Required(r.ToAddress, "toAddress", 256);
        if (!MailAddress.TryCreate(to, out _)) throw RequestValidationException.For("toAddress", "Enter a valid email address.");
        var row = await ExistingAsync(IntegrationKind.Email, "Save the email settings first.", ct);
        var cfg = Read<EmailConfig>(row);
        if (cfg.Host is null || cfg.FromAddress is null) throw RequestValidationException.For("host", "Save the SMTP server and the from address first.");
        var password = secrets.Unprotect(row.EncryptedSecrets).GetValueOrDefault("password");
        var settings = new SmtpSettings(cfg.Host, cfg.Port, cfg.UseStartTls, cfg.Username, password, cfg.FromAddress, cfg.FromName ?? await InstitutionNameAsync(ct));
        var result = await clients.SendEmailAsync(settings, new EmailMessage(to, to, "Student Sync test email",
            $"This is a test email from {settings.FromName}. If you can read it, the institute's email settings work.", user.TenantId), ct);
        return await RecordCheckAsync(row, result, $"Test email accepted by {cfg.Host} for {to}. Check the inbox (and spam).", "The email could not be sent", ct);
    }

    // ---------------- SMS ----------------

    public async Task SaveSmsAsync(SaveSmsIntegrationRequest r, CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        var accountId = Text.Optional(r.AccountId, "accountId", 64);
        var sender = Text.Optional(r.Sender, "sender", 64);
        var template = Text.Optional(r.TemplateId, "templateId", 64);
        if (r.Secret is { Length: > 200 }) throw RequestValidationException.For("secret", "The key is too long.");
        if (r.Provider == SmsProvider.Twilio)
        {
            if (accountId is not null && !TwilioSid().IsMatch(accountId)) throw RequestValidationException.For("accountId", "A Twilio Account SID starts with AC followed by 32 letters and digits.");
            if (sender is not null && !E164().IsMatch(sender) && !TwilioService().IsMatch(sender))
                throw RequestValidationException.For("sender", "Use your Twilio number in international format (+971…) or a Messaging Service SID (MG…).");
            if (r.Enabled && (accountId is null || sender is null)) throw RequestValidationException.For(accountId is null ? "accountId" : "sender", "Twilio needs the Account SID and a sender to switch SMS on.");
            template = null;
        }
        else
        {
            if (sender is not null && !Msg91Sender().IsMatch(sender)) throw RequestValidationException.For("sender", "An MSG91 sender ID is 6 letters, for example SSYNCS.");
            if (r.Enabled && template is null) throw RequestValidationException.For("templateId", "MSG91 needs the DLT-approved template (flow) ID to switch SMS on.");
            accountId = null;
        }

        var row = await RowAsync(IntegrationKind.Sms, r.Provider.ToString(), ct);
        var stored = secrets.Unprotect(row.EncryptedSecrets);
        if (!string.IsNullOrEmpty(r.Secret)) stored["secret"] = r.Secret.Trim();
        if (r.Enabled && !stored.ContainsKey("secret")) throw RequestValidationException.For("secret", r.Provider == SmsProvider.Twilio ? "Enter the Twilio Auth Token." : "Enter the MSG91 auth key.");
        row.IsEnabled = r.Enabled;
        row.SettingsJson = JsonSerializer.Serialize(new SmsConfig(accountId, sender, template), Json);
        row.EncryptedSecrets = stored.Count == 0 ? null : secrets.Protect(stored);
        audit.Record("integration.sms.saved", nameof(InstitutionIntegration), row.Id, $"enabled={r.Enabled} provider={r.Provider} sender={sender ?? "—"}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<IntegrationCheckDto> TestSmsAsync(TestSmsRequest r, CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        var to = Text.Required(r.ToPhone, "toPhone", 20).Replace(" ", "", StringComparison.Ordinal);
        if (!E164().IsMatch(to)) throw RequestValidationException.For("toPhone", "Use international format with the country code, for example +971501234567 or +919876543210.");
        var row = await ExistingAsync(IntegrationKind.Sms, "Save the SMS settings first.", ct);
        var cfg = Read<SmsConfig>(row);
        var secret = secrets.Unprotect(row.EncryptedSecrets).GetValueOrDefault("secret") ?? throw RequestValidationException.For("secret", "Save the provider key first.");
        var provider = Enum.Parse<SmsProvider>(row.Provider);
        var name = await InstitutionNameAsync(ct);
        var result = await clients.SendSmsAsync(new SmsSettings(provider, cfg.AccountId, cfg.Sender, cfg.TemplateId, secret), to, $"Test message from {name} (Student Sync). SMS settings work.", ct);
        return await RecordCheckAsync(row, result, $"{provider} accepted the test SMS to {to}.", $"{provider} did not accept the SMS", ct);
    }

    // ---------------- Payments ----------------

    public async Task SavePaymentsAsync(SavePaymentIntegrationRequest r, CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        var keyId = Text.Optional(r.KeyId, "keyId", 120);
        var secret = string.IsNullOrWhiteSpace(r.Secret) ? null : r.Secret.Trim();
        var webhook = string.IsNullOrWhiteSpace(r.WebhookSecret) ? null : r.WebhookSecret.Trim();
        if (secret is { Length: > 300 } || webhook is { Length: > 300 }) throw RequestValidationException.For("secret", "The key is too long.");
        if (r.Provider == PaymentProvider.Razorpay)
        {
            if (keyId is not null && !keyId.StartsWith("rzp_test_", StringComparison.Ordinal) && !keyId.StartsWith("rzp_live_", StringComparison.Ordinal))
                throw RequestValidationException.For("keyId", "A Razorpay Key ID starts with rzp_test_ or rzp_live_.");
        }
        else
        {
            if (keyId is not null && !keyId.StartsWith("pk_test_", StringComparison.Ordinal) && !keyId.StartsWith("pk_live_", StringComparison.Ordinal))
                throw RequestValidationException.For("keyId", "A Stripe publishable key starts with pk_test_ or pk_live_.");
            if (secret is not null && !Regex.IsMatch(secret, "^(sk|rk)_(test|live)_"))
                throw RequestValidationException.For("secret", "A Stripe secret key starts with sk_test_, sk_live_ (or rk_ for a restricted key).");
            if (webhook is not null && !webhook.StartsWith("whsec_", StringComparison.Ordinal))
                throw RequestValidationException.For("webhookSecret", "A Stripe webhook signing secret starts with whsec_.");
        }
        if (r.Enabled && keyId is null) throw RequestValidationException.For("keyId", "Enter the key ID to switch payments on.");

        var row = await RowAsync(IntegrationKind.Payments, r.Provider.ToString(), ct);
        var stored = secrets.Unprotect(row.EncryptedSecrets);
        if (secret is not null) stored["secret"] = secret;
        if (webhook is not null) stored["webhook"] = webhook;
        if (r.Enabled && !stored.ContainsKey("secret")) throw RequestValidationException.For("secret", r.Provider == PaymentProvider.Razorpay ? "Enter the Razorpay Key Secret." : "Enter the Stripe secret key.");
        if (keyId is not null && stored.TryGetValue("secret", out var s) && ModeOf(keyId) is { } keyMode && ModeOf(s) is { } secretMode && keyMode != secretMode)
            throw RequestValidationException.For("secret", $"The key ID is a {keyMode} key but the secret is a {secretMode} key. Use both from the same mode.");
        row.IsEnabled = r.Enabled;
        row.SettingsJson = JsonSerializer.Serialize(new PaymentConfig(keyId), Json);
        row.EncryptedSecrets = stored.Count == 0 ? null : secrets.Protect(stored);
        audit.Record("integration.payments.saved", nameof(InstitutionIntegration), row.Id, $"enabled={r.Enabled} provider={r.Provider} mode={ModeOf(keyId) ?? "—"}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<IntegrationCheckDto> VerifyPaymentsAsync(CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        var row = await ExistingAsync(IntegrationKind.Payments, "Save the payment settings first.", ct);
        var keyId = Read<PaymentConfig>(row).KeyId ?? throw RequestValidationException.For("keyId", "Save the key ID first.");
        var secret = secrets.Unprotect(row.EncryptedSecrets).GetValueOrDefault("secret") ?? throw RequestValidationException.For("secret", "Save the secret key first.");
        var provider = Enum.Parse<PaymentProvider>(row.Provider);
        var result = await clients.VerifyPaymentKeysAsync(new PaymentSettings(provider, keyId, secret), ct);
        return await RecordCheckAsync(row, result, $"{provider} accepted these keys ({ModeOf(keyId)} mode).", $"{provider} did not accept these keys", ct);
    }

    // ---------------- For automations (no permission check: called by background jobs inside a tenant scope) ----------------

    /// <summary>The institute's SMS connection when it is switched on and has its key, else null (SMS is then skipped).</summary>
    public async Task<SmsSettings?> ActiveSmsAsync(CancellationToken ct)
    {
        var row = await db.InstitutionIntegrations.FirstOrDefaultAsync(x => x.Kind == IntegrationKind.Sms && x.IsEnabled, ct);
        if (row is null || secrets.Unprotect(row.EncryptedSecrets).GetValueOrDefault("secret") is not { } secret) return null;
        var cfg = Read<SmsConfig>(row);
        return new SmsSettings(Enum.Parse<SmsProvider>(row.Provider), cfg.AccountId, cfg.Sender, cfg.TemplateId, secret);
    }

    /// <summary>The institute's payment gateway when switched on with its secret (and webhook secret, if stored), else null.</summary>
    public async Task<(PaymentSettings Settings, string? WebhookSecret)?> ActivePaymentsAsync(CancellationToken ct)
    {
        var row = await db.InstitutionIntegrations.FirstOrDefaultAsync(x => x.Kind == IntegrationKind.Payments && x.IsEnabled, ct);
        if (row is null) return null;
        var stored = secrets.Unprotect(row.EncryptedSecrets);
        if (Read<PaymentConfig>(row).KeyId is not { } keyId || stored.GetValueOrDefault("secret") is not { } secret) return null;
        return (new PaymentSettings(Enum.Parse<PaymentProvider>(row.Provider), keyId, secret), stored.GetValueOrDefault("webhook"));
    }

    /// <summary>Which connections are ready, for the Automations page.</summary>
    public async Task<(bool Email, bool Sms, bool Payments)> ReadinessAsync(CancellationToken ct)
    {
        var rows = await db.InstitutionIntegrations.Where(x => x.IsEnabled).Select(x => x.Kind).ToListAsync(ct);
        return (rows.Contains(IntegrationKind.Email) || flags.Email, rows.Contains(IntegrationKind.Sms), rows.Contains(IntegrationKind.Payments));
    }

    // ---------------- helpers ----------------

    private async Task<InstitutionIntegration> RowAsync(IntegrationKind kind, string provider, CancellationToken ct)
    {
        var row = await db.InstitutionIntegrations.FirstOrDefaultAsync(x => x.Kind == kind, ct);
        if (row is null)
        {
            row = new InstitutionIntegration { Kind = kind, Provider = provider };
            db.InstitutionIntegrations.Add(row);
        }
        else if (row.Provider != provider)
        {
            // A different provider's keys are useless (and confusing) here: start clean.
            row.Provider = provider;
            row.EncryptedSecrets = null;
            (row.LastCheckedAtUtc, row.LastCheckOk, row.LastCheckMessage) = (null, null, null);
        }
        return row;
    }

    private async Task<InstitutionIntegration> ExistingAsync(IntegrationKind kind, string missing, CancellationToken ct)
    {
        var row = await db.InstitutionIntegrations.FirstOrDefaultAsync(x => x.Kind == kind, ct) ?? throw RequestValidationException.For("settings", missing);
        if (row.LastCheckedAtUtc is { } last && clock.UtcNow - last < TestCooldown)
            throw new ConflictException("Please wait a few seconds before testing again.", "test_too_soon");
        return row;
    }

    private async Task<IntegrationCheckDto> RecordCheckAsync(InstitutionIntegration row, DeliveryResult result, string okMessage, string failPrefix, CancellationToken ct)
    {
        var message = result.Accepted ? okMessage : $"{failPrefix}: {Short(result.Error)}";
        (row.LastCheckedAtUtc, row.LastCheckOk, row.LastCheckMessage) = (clock.UtcNow, result.Accepted, Truncate(message, 500));
        audit.Record($"integration.{row.Kind.ToString().ToLowerInvariant()}.checked", nameof(InstitutionIntegration), row.Id, $"provider={row.Provider} ok={result.Accepted}");
        await db.SaveChangesAsync(ct);
        return new IntegrationCheckDto(row.LastCheckedAtUtc.Value, result.Accepted, row.LastCheckMessage);
    }

    private Task<string> InstitutionNameAsync(CancellationToken ct) => db.Tenants.Where(t => t.Id == user.TenantId).Select(t => t.Name).FirstAsync(ct);

    private static T Read<T>(InstitutionIntegration row) => JsonSerializer.Deserialize<T>(row.SettingsJson, Json)!;
    private static IntegrationCheckDto? Check(InstitutionIntegration? row) =>
        row?.LastCheckedAtUtc is { } at ? new IntegrationCheckDto(at, row.LastCheckOk == true, row.LastCheckMessage ?? "") : null;
    private static string? ModeOf(string? key) => key is null ? null : key.Contains("_live_", StringComparison.Ordinal) ? "live" : key.Contains("_test_", StringComparison.Ordinal) ? "test" : null;
    private static string Short(string? error) => string.IsNullOrWhiteSpace(error) ? "no details from the provider" : Truncate(error, 300);
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private EmailIntegrationDto EmailDto(InstitutionIntegration? row)
    {
        if (row is null) return new(false, null, 587, true, null, false, null, null, null);
        var c = Read<EmailConfig>(row);
        return new(row.IsEnabled, c.Host, c.Port, c.UseStartTls, c.Username, secrets.Unprotect(row.EncryptedSecrets).ContainsKey("password"), c.FromAddress, c.FromName, Check(row));
    }

    private SmsIntegrationDto SmsDto(InstitutionIntegration? row)
    {
        if (row is null) return new(false, SmsProvider.Twilio, null, null, null, false, null);
        var c = Read<SmsConfig>(row);
        return new(row.IsEnabled, Enum.Parse<SmsProvider>(row.Provider), c.AccountId, c.Sender, c.TemplateId, secrets.Unprotect(row.EncryptedSecrets).ContainsKey("secret"), Check(row));
    }

    private PaymentIntegrationDto PaymentDto(InstitutionIntegration? row)
    {
        if (row is null) return new(false, PaymentProvider.Razorpay, null, null, false, false, null);
        var c = Read<PaymentConfig>(row);
        var s = secrets.Unprotect(row.EncryptedSecrets);
        return new(row.IsEnabled, Enum.Parse<PaymentProvider>(row.Provider), c.KeyId, ModeOf(c.KeyId), s.ContainsKey("secret"), s.ContainsKey("webhook"), Check(row));
    }

    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9\-\.]{0,198}[A-Za-z0-9])?$")] private static partial Regex HostPattern();
    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$")] private static partial Regex E164();
    [GeneratedRegex(@"^AC[0-9a-fA-F]{32}$")] private static partial Regex TwilioSid();
    [GeneratedRegex(@"^MG[0-9a-fA-F]{32}$")] private static partial Regex TwilioService();
    [GeneratedRegex(@"^[A-Za-z]{6}$")] private static partial Regex Msg91Sender();
}
