using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Infrastructure.Persistence;
using StudentSync.Infrastructure.Seeding;
using StudentSync.Infrastructure.Services;

namespace StudentSync.IntegrationTests;

/// <summary>
/// Settings ▸ Integrations: an institute's own SMTP server, SMS provider and payment gateway. Admin only, isolated per
/// institute, secrets write-only and encrypted, tests report the provider's real answer (stubbed here, never the real
/// services), and notification email uses the institute's SMTP server when it is switched on.
/// </summary>
public sealed class IntegrationSettingsTests(ApiFixture api)
{
    private const string Path = "institution/connections";

    [Fact]
    public async Task Only_institution_admins_can_read_or_change_connections()
    {
        foreach (var who in new[] { Accounts.Amira, Accounts.Aisha })
        {
            var c = await api.LoginAsync(who);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetRaw(Path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PutRaw($"{Path}/email", new SaveEmailIntegrationRequest(false, "smtp.example.test", 587, true, null, null, null, null))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostRaw($"{Path}/payments/verify", null)).StatusCode);
        }
    }

    [Fact]
    public async Task Email_settings_keep_the_password_secret_and_stay_inside_the_institute()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        const string password = "smtp-Pass-never-shown-123";
        await admin.Put($"{Path}/email", new SaveEmailIntegrationRequest(false, "smtp.example.test", 587, true, "office@horizon.example.test", password, "office@horizon.example.test", "Horizon office"));

        var raw = await (await admin.GetRaw(Path)).Content.ReadAsStringAsync();
        Assert.DoesNotContain(password, raw);
        var dto = await admin.Get<InstitutionIntegrationsDto>(Path);
        Assert.Equal(("smtp.example.test", 587, true, true), (dto.Email.Host, dto.Email.Port, dto.Email.HasPassword, dto.Email.UseStartTls));

        // Saving again without a password keeps the stored one; the database holds it encrypted, the audit log not at all.
        await admin.Put($"{Path}/email", new SaveEmailIntegrationRequest(false, "smtp.example.test", 465, false, "office@horizon.example.test", null, "office@horizon.example.test", null));
        Assert.True((await admin.Get<InstitutionIntegrationsDto>(Path)).Email.HasPassword);
        using (var scope = api.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.InstitutionIntegrations.IgnoreQueryFilters().SingleAsync(x => x.TenantId == admin.Auth.User.TenantId && x.Kind == Domain.Platform.IntegrationKind.Email);
            Assert.DoesNotContain(password, row.EncryptedSecrets ?? "");
            Assert.DoesNotContain(password, row.SettingsJson);
            Assert.False(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Summary != null && a.Summary.Contains(password)));
        }

        // The other institute sees only its own (empty) settings.
        var other = await api.LoginAsync(Accounts.OtherAdmin);
        var theirs = await other.Get<InstitutionIntegrationsDto>(Path);
        Assert.Null(theirs.Email.Host);
        Assert.False(theirs.Email.HasPassword);
    }

    [Fact]
    public async Task Invalid_settings_are_refused_with_field_errors()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        async Task<string> Error(string path, object body)
        {
            var r = await admin.PutRaw(path, body);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            return await r.Content.ReadAsStringAsync();
        }
        Assert.Contains("host", await Error($"{Path}/email", new SaveEmailIntegrationRequest(true, null, 587, true, null, null, "a@b.test", null)));
        Assert.Contains("port", await Error($"{Path}/email", new SaveEmailIntegrationRequest(false, "smtp.example.test", 70000, true, null, null, null, null)));
        Assert.Contains("accountId", await Error($"{Path}/sms", new SaveSmsIntegrationRequest(false, SmsProvider.Twilio, "not-a-sid", null, null, null)));
        Assert.Contains("templateId", await Error($"{Path}/sms", new SaveSmsIntegrationRequest(true, SmsProvider.Msg91, null, null, null, "msg91-key")));
        Assert.Contains("keyId", await Error($"{Path}/payments", new SavePaymentIntegrationRequest(false, PaymentProvider.Razorpay, "pk_test_123", null, null)));
        // Test and live keys cannot be mixed.
        Assert.Contains("same mode", await Error($"{Path}/payments", new SavePaymentIntegrationRequest(false, PaymentProvider.Stripe, "pk_test_abc", "sk_live_abc", null)));
    }

    [Fact]
    public async Task A_failed_test_email_is_reported_as_failed_and_tests_are_throttled()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        // Port 9 on loopback refuses connections: the test must say it failed, never that it was sent.
        await admin.Put($"{Path}/email", new SaveEmailIntegrationRequest(false, "127.0.0.1", 9, false, null, null, "office@horizon.example.test", null));
        var check = await admin.Post<IntegrationCheckDto>($"{Path}/email/test", new TestEmailRequest("owner@horizon.example.test"));
        Assert.False(check.Ok);
        Assert.Contains("could not be sent", check.Message);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostRaw($"{Path}/email/test", new TestEmailRequest("owner@horizon.example.test"))).StatusCode);
    }

    [Fact]
    public async Task Sms_and_payment_checks_call_the_provider_and_report_its_answer()
    {
        var stub = new StubProvider();
        await using var factory = api.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddHttpClient(IntegrationClients.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => stub)));
        var http = factory.CreateClient();
        var login = await http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(Accounts.OtherAdmin, ApiFixture.Password, null, "tests"), ApiFixture.Json);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(ApiFixture.Json))!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var admin = new Client(http, auth);

        // Twilio: form post to the account's Messages resource with Basic SID:token.
        var sid = "AC" + new string('1', 32);
        await admin.Put($"{Path}/sms", new SaveSmsIntegrationRequest(true, SmsProvider.Twilio, sid, "+15005550006", null, "twilio-token"));
        var sms = await admin.Post<IntegrationCheckDto>($"{Path}/sms/test", new TestSmsRequest("+971 50 123 4567"));
        Assert.True(sms.Ok, sms.Message);
        var twilio = Assert.Single(stub.Requests, r => r.Uri.Host == "api.twilio.com");
        Assert.Equal($"/2010-04-01/Accounts/{sid}/Messages.json", twilio.Uri.AbsolutePath);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sid}:twilio-token")), twilio.Auth);
        Assert.Contains("To=%2B971501234567", twilio.Body);
        Assert.True((await admin.Get<InstitutionIntegrationsDto>(Path)).Sms.HasSecret);

        // Razorpay answers 401: reported as rejected keys, not as success.
        await admin.Put($"{Path}/payments", new SavePaymentIntegrationRequest(true, PaymentProvider.Razorpay, "rzp_test_abc", "bad-secret", null));
        var razor = await admin.Post<IntegrationCheckDto>($"{Path}/payments/verify", null);
        Assert.False(razor.Ok);
        Assert.Contains("rejected the credentials", razor.Message);

        // Switching to Stripe clears the Razorpay secret (and the last check); Stripe accepts its own keys (test mode).
        var cleared = await admin.PutRaw($"{Path}/payments", new SavePaymentIntegrationRequest(true, PaymentProvider.Stripe, "pk_test_abc", null, null));
        Assert.Equal(HttpStatusCode.BadRequest, cleared.StatusCode);
        await admin.Put($"{Path}/payments", new SavePaymentIntegrationRequest(true, PaymentProvider.Stripe, "pk_test_abc", "sk_test_good", "whsec_abc"));
        var stripe = await admin.Post<IntegrationCheckDto>($"{Path}/payments/verify", null);
        Assert.True(stripe.Ok, stripe.Message);
        Assert.Contains("test mode", stripe.Message);
        var payments = (await admin.Get<InstitutionIntegrationsDto>(Path)).Payments;
        Assert.Equal((PaymentProvider.Stripe, "test", true, true), (payments.Provider, payments.Mode, payments.HasSecret, payments.HasWebhookSecret));
        Assert.Equal("Bearer sk_test_good", Assert.Single(stub.Requests, r => r.Uri.Host == "api.stripe.com").AuthHeader);
    }

    [Fact]
    public async Task Notification_email_uses_the_institutes_own_smtp_server_once_switched_on()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tenantId = admin.Auth.User.TenantId!.Value;
        using var scope = api.Factory.Services.CreateScope();
        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        // The test host has no server-wide SMTP server.
        Assert.False(email.IsConfigured);
        await admin.Put($"{Path}/email", new SaveEmailIntegrationRequest(false, "127.0.0.1", 9, false, null, null, "office@horizon.example.test", null));
        Assert.False(await email.IsConfiguredForAsync(tenantId, CancellationToken.None));
        await admin.Put($"{Path}/email", new SaveEmailIntegrationRequest(true, "127.0.0.1", 9, false, null, null, "office@horizon.example.test", null));
        Assert.True(await email.IsConfiguredForAsync(tenantId, CancellationToken.None));
        var other = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tenants.IgnoreQueryFilters().Where(t => t.Slug == DemoDataSeeder.SecondarySlug).Select(t => t.Id).SingleAsync();
        Assert.False(await email.IsConfiguredForAsync(other, CancellationToken.None));
        // Sending goes to that server (which refuses here), so the result is an honest failure.
        var result = await email.SendAsync(new EmailMessage("someone@horizon.example.test", "Someone", "Hello", "Body", tenantId), CancellationToken.None);
        Assert.False(result.Accepted);
        await admin.Put($"{Path}/email", new SaveEmailIntegrationRequest(false, "127.0.0.1", 9, false, null, null, "office@horizon.example.test", null));
    }

    private sealed class StubProvider : HttpMessageHandler
    {
        public sealed record Seen(Uri Uri, string? Auth, string? AuthHeader, string Body);
        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new Seen(request.RequestUri!, request.Headers.Authorization?.Parameter, request.Headers.Authorization?.ToString(), body));
            return request.RequestUri!.Host switch
            {
                "api.twilio.com" => Json(HttpStatusCode.Created, """{"sid":"SM00000000000000000000000000000001","status":"queued"}"""),
                "api.razorpay.com" => Json(HttpStatusCode.Unauthorized, """{"error":{"code":"BAD_REQUEST_ERROR","description":"The api key provided is invalid"}}"""),
                "api.stripe.com" => Json(HttpStatusCode.OK, """{"object":"balance","available":[]}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
