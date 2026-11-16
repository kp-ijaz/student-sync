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

    // ---------------- Payment links ----------------

    public async Task<GatewayLink> CreatePaymentLinkAsync(PaymentSettings s, GatewayLinkRequest r, CancellationToken ct)
    {
        using var client = http.CreateClient(HttpClientName);
        if (s.Provider == PaymentProvider.Razorpay)
        {
            var body = new Dictionary<string, object?>
            {
                ["amount"] = r.AmountMinor,
                ["currency"] = r.Currency,
                ["accept_partial"] = false,
                ["expire_by"] = new DateTimeOffset(r.ExpiresAtUtc).ToUnixTimeSeconds(),
                ["reference_id"] = r.ReferenceId,
                ["description"] = r.Description,
                ["customer"] = r.CustomerPhoneE164 is null ? new Dictionary<string, string> { ["name"] = r.CustomerName } : new Dictionary<string, string> { ["name"] = r.CustomerName, ["contact"] = r.CustomerPhoneE164 },
                ["notify"] = new Dictionary<string, bool> { ["sms"] = false, ["email"] = false },
                ["reminder_enable"] = false,
            };
            if (r.ReturnUrl is not null) { body["callback_url"] = r.ReturnUrl; body["callback_method"] = "get"; }
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.razorpay.com/v1/payment_links") { Content = JsonContent.Create(body) };
            req.Headers.Authorization = Basic(s.KeyId, s.Secret);
            var (ok, json, error) = await CallAsync(client, req, "Razorpay", ct);
            return ok ? new GatewayLink(true, Str(json, "id"), Str(json, "short_url"), null) : new GatewayLink(false, null, null, error);
        }
        else
        {
            // Stripe: a one-off price, then a Payment Link limited to one completed payment.
            var priceFields = new Dictionary<string, string>
            {
                ["currency"] = r.Currency.ToLowerInvariant(),
                ["unit_amount"] = r.AmountMinor.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["product_data[name]"] = r.Description,
            };
            using var priceReq = Stripe(HttpMethod.Post, "https://api.stripe.com/v1/prices", s.Secret, priceFields);
            var (pok, price, perror) = await CallAsync(client, priceReq, "Stripe", ct);
            if (!pok || Str(price, "id") is not { } priceId) return new GatewayLink(false, null, null, perror ?? "Stripe did not create the price");
            var linkFields = new Dictionary<string, string>
            {
                ["line_items[0][price]"] = priceId,
                ["line_items[0][quantity]"] = "1",
                ["metadata[reference]"] = r.ReferenceId,
                ["restrictions[completed_sessions][limit]"] = "1",
            };
            if (r.ReturnUrl is not null) { linkFields["after_completion[type]"] = "redirect"; linkFields["after_completion[redirect][url]"] = r.ReturnUrl; }
            using var linkReq = Stripe(HttpMethod.Post, "https://api.stripe.com/v1/payment_links", s.Secret, linkFields);
            var (lok, link, lerror) = await CallAsync(client, linkReq, "Stripe", ct);
            return lok ? new GatewayLink(true, Str(link, "id"), Str(link, "url"), null) : new GatewayLink(false, null, null, lerror);
        }
    }

    public async Task<GatewayLinkState> GetPaymentLinkAsync(PaymentSettings s, string linkId, CancellationToken ct)
    {
        using var client = http.CreateClient(HttpClientName);
        if (s.Provider == PaymentProvider.Razorpay)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.razorpay.com/v1/payment_links/{Uri.EscapeDataString(linkId)}");
            req.Headers.Authorization = Basic(s.KeyId, s.Secret);
            var (ok, json, error) = await CallAsync(client, req, "Razorpay", ct);
            if (!ok) return new GatewayLinkState(false, false, false, null, null, error);
            var status = Str(json, "status");
            string? paymentId = null;
            if (json.TryGetProperty("payments", out var payments) && payments.ValueKind == JsonValueKind.Array)
                foreach (var p in payments.EnumerateArray())
                    if (Str(p, "status") == "captured") paymentId = Str(p, "payment_id");
            long? paid = json.TryGetProperty("amount_paid", out var ap) && ap.ValueKind == JsonValueKind.Number ? ap.GetInt64() : null;
            return new GatewayLinkState(true, status == "paid" && paymentId is not null, status is "expired" or "cancelled", paymentId, paid, null);
        }
        else
        {
            using var req = Stripe(HttpMethod.Get, $"https://api.stripe.com/v1/checkout/sessions?payment_link={Uri.EscapeDataString(linkId)}&limit=10", s.Secret, null);
            var (ok, json, error) = await CallAsync(client, req, "Stripe", ct);
            if (!ok) return new GatewayLinkState(false, false, false, null, null, error);
            if (json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                foreach (var session in data.EnumerateArray())
                    if (Str(session, "payment_status") == "paid")
                        return new GatewayLinkState(true, true, false, Str(session, "payment_intent") ?? Str(session, "id"),
                            session.TryGetProperty("amount_total", out var at) && at.ValueKind == JsonValueKind.Number ? at.GetInt64() : null, null);
            return new GatewayLinkState(true, false, false, null, null, null);
        }
    }

    public async Task<bool> ClosePaymentLinkAsync(PaymentSettings s, string linkId, CancellationToken ct)
    {
        using var client = http.CreateClient(HttpClientName);
        HttpRequestMessage req;
        if (s.Provider == PaymentProvider.Razorpay)
        {
            req = new HttpRequestMessage(HttpMethod.Post, $"https://api.razorpay.com/v1/payment_links/{Uri.EscapeDataString(linkId)}/cancel");
            req.Headers.Authorization = Basic(s.KeyId, s.Secret);
        }
        else req = Stripe(HttpMethod.Post, $"https://api.stripe.com/v1/payment_links/{Uri.EscapeDataString(linkId)}", s.Secret, new Dictionary<string, string> { ["active"] = "false" });
        using (req) return (await CallAsync(client, req, s.Provider.ToString(), ct)).Ok;
    }

    private static HttpRequestMessage Stripe(HttpMethod method, string url, string secret, Dictionary<string, string>? form)
    {
        var req = new HttpRequestMessage(method, url);
        if (form is not null) req.Content = new FormUrlEncodedContent(form);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return req;
    }

    private async Task<(bool Ok, JsonElement Json, string? Error)> CallAsync(HttpClient client, HttpRequestMessage request, string provider, CancellationToken ct)
    {
        try
        {
            using var response = await client.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonElement json = default;
            try { json = JsonSerializer.Deserialize<JsonElement>(text); } catch (JsonException) { /* not JSON */ }
            if (response.IsSuccessStatusCode && json.ValueKind == JsonValueKind.Object) return (true, json, null);
            var detail = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object
                ? Str(e, "description") ?? Str(e, "message") : null;
            logger.LogWarning("{Provider} call failed with HTTP {Status}", provider, (int)response.StatusCode);
            return (false, default, detail ?? $"{provider} answered HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("{Provider} could not be reached: {Error}", provider, ex.GetType().Name);
            return (false, default, $"{provider} could not be reached");
        }
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
