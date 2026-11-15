using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Platform;

namespace StudentSync.Infrastructure.Services;

/// <summary>Encrypts institute integration secrets with ASP.NET Core Data Protection (keys in DataProtection:KeysPath).</summary>
public sealed class IntegrationSecrets(IDataProtectionProvider provider, ILogger<IntegrationSecrets> logger) : IIntegrationSecrets
{
    private readonly IDataProtector _protector = provider.CreateProtector("StudentSync.InstitutionIntegrations.v1");

    public string Protect(IReadOnlyDictionary<string, string> secrets) => _protector.Protect(JsonSerializer.Serialize(secrets));

    public Dictionary<string, string> Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(_protector.Unprotect(protectedValue)) ?? []; }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Keys rotated away or lost: treat as "nothing saved" so the admin is asked to enter the secret again.
            logger.LogWarning("Stored integration secrets could not be decrypted; they must be entered again.");
            return [];
        }
    }
}

/// <summary>SMTP sending shared by the server-wide sender and institute connections. Accepted only when the server took it.</summary>
public static class SmtpMail
{
    public static async Task<DeliveryResult> SendAsync(SmtpSettings s, EmailMessage m, ILogger logger, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(s.FromName, s.FromAddress));
        message.To.Add(new MailboxAddress(m.ToName, m.ToAddress));
        message.Subject = m.Subject;
        message.Body = new TextPart("plain") { Text = m.TextBody };
        try
        {
            using var client = new SmtpClient { Timeout = 20_000 };
            // StartTLS when asked; otherwise Auto (implicit TLS on 465, STARTTLS when the server offers it).
            await client.ConnectAsync(s.Host, s.Port, s.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, ct);
            if (!string.IsNullOrEmpty(s.Username)) await client.AuthenticateAsync(s.Username, s.Password ?? "", ct);
            var response = await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            return new DeliveryResult(true, message.MessageId ?? response, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("SMTP delivery via {Host}:{Port} failed: {Error}", s.Host, s.Port, ex.GetType().Name);
            return new DeliveryResult(false, null, ex switch
            {
                AuthenticationException => "the server rejected the username or password",
                SslHandshakeException => "a secure connection could not be made (check the port and the StartTLS setting)",
                System.Net.Sockets.SocketException or TimeoutException or OperationCanceledException => $"could not reach {s.Host}:{s.Port}",
                _ => ex.Message,
            });
        }
    }
}

/// <summary>
/// Sends email through the institute's own SMTP server when it has switched one on (Settings ▸ Integrations), otherwise
/// through the server-wide one (Email:Smtp). Scoped: it reads the institute's connection per message.
/// </summary>
public sealed class InstitutionEmailSender(IAppDbContext db, IIntegrationSecrets secrets, IOptions<SmtpOptions> server, ILogger<InstitutionEmailSender> logger) : IEmailSender
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(server.Value.Host);

    public async Task<bool> IsConfiguredForAsync(Guid? tenantId, CancellationToken ct) => IsConfigured || await InstituteSmtpAsync(tenantId, ct) is not null;

    public async Task<DeliveryResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (await InstituteSmtpAsync(message.TenantId, ct) is { } own) return await SmtpMail.SendAsync(own, message, logger, ct);
        if (!IsConfigured) return new DeliveryResult(false, null, "Email provider not configured.");
        var o = server.Value;
        return await SmtpMail.SendAsync(new SmtpSettings(o.Host!, o.Port, o.UseStartTls, o.Username, o.Password, o.FromAddress, o.FromName), message, logger, ct);
    }

    private async Task<SmtpSettings?> InstituteSmtpAsync(Guid? tenantId, CancellationToken ct)
    {
        if (tenantId is null) return null;
        var row = await db.InstitutionIntegrations.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Kind == IntegrationKind.Email && x.IsEnabled, ct);
        if (row is null) return null;
        var c = JsonSerializer.Deserialize<JsonElement>(row.SettingsJson);
        string? Str(string name) => c.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var host = Str("host");
        var from = Str("fromAddress");
        if (host is null || from is null) return null;
        var port = c.TryGetProperty("port", out var p) && p.TryGetInt32(out var n) ? n : 587;
        var startTls = c.TryGetProperty("useStartTls", out var t) && t.ValueKind == JsonValueKind.True;
        var name = Str("fromName") ?? await db.Tenants.IgnoreQueryFilters().Where(x => x.Id == tenantId).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "Student Sync";
        return new SmtpSettings(host, port, startTls, Str("username"), secrets.Unprotect(row.EncryptedSecrets).GetValueOrDefault("password"), from, name);
    }
}

/// <summary>
/// Calls to the providers an institute connects. Twilio: POST Accounts/{SID}/Messages.json (form, Basic SID:token).
/// MSG91: POST control.msg91.com/api/v5/flow/ with the authkey header and a DLT template (sent as template_id and the older
/// flow_id). Razorpay: GET /v1/payments?count=1 (Basic key:secret). Stripe: GET /v1/balance (Bearer secret key).
/// Errors returned here never contain the secrets.
/// </summary>
public sealed class IntegrationClients(IHttpClientFactory http, ILogger<IntegrationClients> logger) : IIntegrationClients
{
    public const string HttpClientName = "integrations";

    public Task<DeliveryResult> SendEmailAsync(SmtpSettings settings, EmailMessage message, CancellationToken ct) => SmtpMail.SendAsync(settings, message, logger, ct);

    public async Task<DeliveryResult> SendSmsAsync(SmsSettings s, string toE164, string text, CancellationToken ct)
    {
        using var client = http.CreateClient(HttpClientName);
        HttpRequestMessage request;
        if (s.Provider == SmsProvider.Twilio)
        {
            var fields = new Dictionary<string, string> { ["To"] = toE164, ["Body"] = text };
            if (s.Sender?.StartsWith("MG", StringComparison.Ordinal) == true) fields["MessagingServiceSid"] = s.Sender; else fields["From"] = s.Sender ?? "";
            request = new HttpRequestMessage(HttpMethod.Post, $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(s.AccountId ?? "")}/Messages.json")
            { Content = new FormUrlEncodedContent(fields) };
            request.Headers.Authorization = Basic(s.AccountId ?? "", s.Secret);
        }
        else
        {
            var recipient = new Dictionary<string, string> { ["mobiles"] = toE164.TrimStart('+'), ["VAR1"] = text };
            var body = new Dictionary<string, object?> { ["template_id"] = s.TemplateId, ["flow_id"] = s.TemplateId, ["recipients"] = new[] { recipient } };
            if (!string.IsNullOrEmpty(s.Sender)) body["sender"] = s.Sender;
            request = new HttpRequestMessage(HttpMethod.Post, "https://control.msg91.com/api/v5/flow/") { Content = JsonContent.Create(body) };
            request.Headers.Add("authkey", s.Secret);
        }
        return await SendAsync(client, request, s.Provider.ToString(), ok: json =>
            s.Provider == SmsProvider.Twilio ? Str(json, "sid") is not null : Str(json, "type") == "success", ct);
    }

    public async Task<DeliveryResult> VerifyPaymentKeysAsync(PaymentSettings s, CancellationToken ct)
    {
        using var client = http.CreateClient(HttpClientName);
        HttpRequestMessage request;
        if (s.Provider == PaymentProvider.Razorpay)
        {
            request = new HttpRequestMessage(HttpMethod.Get, "https://api.razorpay.com/v1/payments?count=1");
            request.Headers.Authorization = Basic(s.KeyId, s.Secret);
        }
        else
        {
            request = new HttpRequestMessage(HttpMethod.Get, "https://api.stripe.com/v1/balance");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Secret);
        }
        return await SendAsync(client, request, s.Provider.ToString(), ok: _ => true, ct);
    }

    private async Task<DeliveryResult> SendAsync(HttpClient client, HttpRequestMessage request, string provider, Func<JsonElement, bool> ok, CancellationToken ct)
    {
        try
        {
            using var response = await client.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonElement json = default;
            try { json = JsonSerializer.Deserialize<JsonElement>(text); } catch (JsonException) { /* not JSON */ }
            if (response.IsSuccessStatusCode && (json.ValueKind != JsonValueKind.Object || ok(json)))
                return new DeliveryResult(true, json.ValueKind == JsonValueKind.Object ? Str(json, "sid") ?? Str(json, "message") : null, null);
            var detail = json.ValueKind == JsonValueKind.Object
                ? Str(json, "message") ?? (json.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object ? Str(e, "description") ?? Str(e, "message") : null)
                : null;
            logger.LogWarning("{Provider} call failed with HTTP {Status}", provider, (int)response.StatusCode);
            return new DeliveryResult(false, null, response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"{provider} rejected the credentials" + (detail is null ? "" : $" ({detail})"),
                _ => detail ?? $"{provider} answered HTTP {(int)response.StatusCode}",
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("{Provider} could not be reached: {Error}", provider, ex.GetType().Name);
            return new DeliveryResult(false, null, $"{provider} could not be reached (check the server's internet access)");
        }
    }

    private static AuthenticationHeaderValue Basic(string user, string password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
