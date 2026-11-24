using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StudentSync.Application.Automation;
using StudentSync.Application.Common;
using StudentSync.Application.People;
using StudentSync.Contracts;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Fees;
using StudentSync.Domain.Platform;
using StudentSync.Infrastructure.Persistence;
using StudentSync.Infrastructure.Services;

namespace StudentSync.IntegrationTests;

/// <summary>
/// Settings ▸ Automations (owner request 2026-11-17): settings are admin-only and per institute; daily tasks run once per
/// day; SMS go to the guardian once; online payments are recorded once, only from a signed webhook confirmed with the
/// gateway (stubbed here, never the real services); salaries, receipts and progress reports respect who may see what.
/// </summary>
public sealed class AutomationTests(ApiFixture api)
{
    private static SaveAutomationSettingsRequest Request(AutomationSettingsDto s, TimeOnly? morning = null, TimeOnly? evening = null, int? lowHoursAt = null) => new(
        s.AttendanceNudge, s.AttendanceNudgeAfterMinutes, s.AbsenceSms, s.AbsenceFlagAt, s.EveningTimetable, evening ?? s.EveningTimetableAt, s.ClassDaySms,
        s.ScheduleRunningOut, s.LowHoursAlert, lowHoursAt ?? s.LowHoursAt, s.LowHoursSms, s.FeeReminders, s.FeeReminderDaysBefore, s.FeeReminderDaysAfter,
        s.FeeReminderSms, s.PaymentLinks, s.MonthEndSalaries, s.MorningSummary, morning ?? s.MorningSummaryAt, s.MonthEndReport, s.ProgressReports,
        s.ProgressReportSms, s.AutoAiNotes, s.JobFailureAlerts);

    private async Task<Guid> TenantOf(string slug)
    {
        using var scope = api.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tenants.Where(t => t.Slug == slug).Select(t => t.Id).SingleAsync();
    }

    [Fact]
    public async Task Automation_settings_are_admin_only_validated_and_kept_per_institute()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var s = await admin.Get<AutomationSettingsDto>("automations");
        Assert.True(s.AttendanceNudge && s.FeeReminders && s.MorningSummary);
        Assert.False(s.AbsenceSms || s.LowHoursSms || s.FeeReminderSms || s.ClassDaySms || s.ProgressReportSms); // SMS costs money: off by default

        await admin.Put("automations", Request(s, lowHoursAt: 5));
        Assert.Equal(5, (await admin.Get<AutomationSettingsDto>("automations")).LowHoursAt);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw("automations", Request(s) with { FeeReminderDaysBefore = 90 })).StatusCode);

        foreach (var who in new[] { Accounts.Amira, Accounts.Aisha })
        {
            var c = await api.LoginAsync(who);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetRaw("automations")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PutRaw("automations", Request(s))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetRaw("automations/messages")).StatusCode);
        }
        // The other institute still has its own defaults.
        Assert.Equal(2, (await (await api.LoginAsync(Accounts.OtherAdmin)).Get<AutomationSettingsDto>("automations")).LowHoursAt);
        await admin.Put("automations", Request(s));
    }

    [Fact]
    public async Task Daily_tasks_run_once_per_day_and_send_the_admin_summary()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var s = await admin.Get<AutomationSettingsDto>("automations");
        // Midnight: every daily task is due now, whatever the time of the test run.
        await admin.Put("automations", Request(s, morning: TimeOnly.MinValue, evening: TimeOnly.MinValue));
        var tenantId = await TenantOf("horizon-demo");
        try
        {
            int first, second;
            using (var scope = api.Factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(tenantId))
                first = await scope.ServiceProvider.GetRequiredService<DailyAutomations>().RunDueAsync(CancellationToken.None);
            using (var scope = api.Factory.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(tenantId))
                second = await scope.ServiceProvider.GetRequiredService<DailyAutomations>().RunDueAsync(CancellationToken.None);
            Assert.True(first > 0);
            Assert.Equal(0, second);

            using var check = api.Factory.Services.CreateScope();
            var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
            var runs = await db.AutomationRuns.IgnoreQueryFilters().Where(r => r.TenantId == tenantId).Select(r => r.Task).ToListAsync();
            foreach (var task in new[] { "summary", "fees", "low-hours", "schedule", "timetable" }) Assert.Contains(task, runs);
            Assert.Equal(runs.Count, runs.Distinct().Count()); // each task once per day
            var adminId = admin.Auth.User.Id;
            Assert.Single(await db.Notifications.Where(n => n.UserId == adminId && n.Type == NotificationType.DailySummary).ToListAsync());
            // Teachers with classes tomorrow got their timetable; nobody in the other institute was touched.
            var other = await TenantOf("northbridge-demo");
            Assert.False(await db.AutomationRuns.IgnoreQueryFilters().AnyAsync(r => r.TenantId == other && r.Task == "summary"));
        }
        finally { await admin.Put("automations", Request(s)); }
    }

    [Fact]
    public async Task Sms_go_to_the_guardian_once_through_the_institutes_provider()
    {
        var stub = new Gateway();
        await using var factory = Stubbed(stub);
        var admin = await Login(factory, Accounts.OtherAdmin);
        var sid = "AC" + new string('2', 32);
        await admin.Put("institution/connections/sms", new SaveSmsIntegrationRequest(true, SmsProvider.Twilio, sid, "+15005550006", null, "twilio-token"));
        var student = await NewStudentAsync(admin, "+971 50 111 2222", "+971 50 765 4321");
        var tenantId = await TenantOf("northbridge-demo");

        using (var scope = factory.Services.CreateScope())
        using (scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(tenantId))
        {
            var sms = scope.ServiceProvider.GetRequiredService<SmsOutbox>();
            Assert.True(await sms.QueueFamilyAsync(student, "test", "Test reminder (demo).", $"test:{student}", CancellationToken.None));
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.SaveChangesAsync();
            Assert.False(await sms.QueueFamilyAsync(student, "test", "Test reminder (demo).", $"test:{student}", CancellationToken.None));
            // Quiet hours depend on the time of the test run: make it due now.
            await db.OutboundMessages.Where(m => m.StudentId == student).ExecuteUpdateAsync(u => u.SetProperty(m => m.NotBeforeUtc, DateTime.UtcNow.AddMinutes(-1)));
            db.ChangeTracker.Clear();
            Assert.Equal(1, await sms.SendDueAsync(CancellationToken.None));
            Assert.Equal(0, await sms.SendDueAsync(CancellationToken.None));
        }
        var twilio = Assert.Single(stub.Requests, r => r.Uri.Host == "api.twilio.com");
        Assert.Contains("To=%2B971507654321", twilio.Body); // the guardian, not the student
        var log = Assert.Single((await admin.Get<List<OutboundMessageDto>>("automations/messages")).Where(m => m.StudentId == student));
        Assert.Equal(("Sent", "Guardian", "+97•••••54321"), (log.Status, log.Recipient, log.PhoneMasked));
        Assert.StartsWith("Northbridge", log.Text);
        // Horizon's admin never sees Northbridge's messages.
        Assert.DoesNotContain(await (await api.LoginAsync(Accounts.Admin)).Get<List<OutboundMessageDto>>("automations/messages"), m => m.StudentId == student);
    }

    [Fact]
    public async Task Online_payment_is_recorded_once_and_only_from_a_signed_webhook()
    {
        var stub = new Gateway();
        await using var factory = Stubbed(stub);
        var admin = await Login(factory, Accounts.OtherAdmin);
        await admin.Put("institution/connections/payments", new SavePaymentIntegrationRequest(true, PaymentProvider.Razorpay, "rzp_test_demo", "rzp-secret", "hook-secret"));
        var student = await NewStudentAsync(admin, null, null);
        var package = (await admin.Post<IdResponse>($"students/{student}/packages", new SavePackageRequest(10, 1500))).Id;

        var link = await admin.Post<PaymentLinkDto>($"students/{student}/packages/{package}/payment-link", null);
        Assert.Equal(("https://rzp.io/i/1", 1500m, "Open"), (link.Url, link.Amount, link.Status));
        Assert.Contains("\"amount\":150000", Assert.Single(stub.Requests, r => r.Uri.AbsolutePath == "/v1/payment_links" && r.Method == "POST").Body);
        // Asking again reuses the same link.
        Assert.Equal(link.Id, (await admin.Post<PaymentLinkDto>($"students/{student}/packages/{package}/payment-link", null)).Id);

        var anon = factory.CreateClient();
        stub.Paid.Add(stub.LinkId(1));
        var body = """{"event":"payment_link.paid","payload":{"payment_link":{"entity":{"id":"LINK"}}}}""".Replace("LINK", stub.LinkId(1));
        var url = "/api/v1/webhooks/payments/razorpay/northbridge-demo";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(Webhook(url, body, "bad-signature"))).StatusCode);
        Assert.Equal(0m, (await admin.Get<StudentFeesDto>($"students/{student}/fees")).Paid);

        var signature = Convert.ToHexStringLower(HMACSHA256.HashData("hook-secret"u8.ToArray(), Encoding.UTF8.GetBytes(body)));
        Assert.Equal(HttpStatusCode.OK, (await anon.SendAsync(Webhook(url, body, signature))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.SendAsync(Webhook(url, body, signature))).StatusCode); // gateways retry
        var fees = await admin.Get<StudentFeesDto>($"students/{student}/fees");
        var paid = Assert.Single(fees.Packages.Single().Payments);
        Assert.Equal((1500m, PaymentMethod.Online, $"pay_{stub.LinkId(1)}", 0m), (paid.Amount, paid.Method, paid.Reference, fees.Due));

        // The receipt: admins of this institute only (the other institute gets "not found").
        var receipt = await admin.Get<ReceiptDto>($"receipts/{paid.Id}");
        Assert.Equal((1500m, "Online", 0m), (receipt.Amount, receipt.Method, receipt.BalanceOnPackage));
        Assert.StartsWith("R-", receipt.Number);
        Assert.Equal(HttpStatusCode.NotFound, (await (await api.LoginAsync(Accounts.Admin)).GetRaw($"receipts/{paid.Id}")).StatusCode);
        // "Online" can never be typed in by hand.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw($"students/{student}/packages", new SavePackageRequest(5, 500, PaidNow: 500, Method: PaymentMethod.Online))).StatusCode);
    }

    [Fact]
    public async Task Salaries_and_progress_reports_respect_who_may_see_what()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var month = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var salaries = await admin.Get<SalaryMonthDto>($"salaries?month={month:yyyy-MM-dd}");
        Assert.NotEmpty(salaries.Teachers);
        var amira = await api.LoginAsync(Accounts.Amira);
        Assert.Equal(HttpStatusCode.Forbidden, (await amira.GetRaw($"salaries?month={month:yyyy-MM-dd}")).StatusCode);
        if (salaries.Teachers.FirstOrDefault(t => t.DueNow > 0) is { } owed)
        {
            await admin.Post<IdResponse>($"salaries/{owed.TeacherId}/pay", new PaySalaryRequest(month, PaymentMethod.BankTransfer, "TEST-SAL"));
            Assert.Equal(0, (await admin.Get<SalaryMonthDto>($"salaries?month={month:yyyy-MM-dd}")).Teachers.Single(t => t.TeacherId == owed.TeacherId).DueNow);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostRaw($"salaries/{owed.TeacherId}/pay", new PaySalaryRequest(month, PaymentMethod.Cash, null))).StatusCode);
        }

        var tenantId = await TenantOf("horizon-demo");
        using (var scope = api.Factory.Services.CreateScope())
        using (scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(tenantId))
        {
            var progress = scope.ServiceProvider.GetRequiredService<ProgressReportService>();
            var made = await progress.PrepareAsync(month, CancellationToken.None);
            Assert.Equal(0, await progress.PrepareAsync(month, CancellationToken.None)); // once per student and month
            Assert.True(made >= 0);
        }
        var all = await admin.Get<List<ProgressReportDto>>("progress-reports?status=Draft");
        Assert.NotEmpty(all);
        var mine = await amira.Get<List<ProgressReportDto>>("progress-reports");
        Assert.All(mine, r => Assert.Equal(amira.Auth.User.FullName, r.TeacherName));
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.LoginAsync(Accounts.Aisha)).GetRaw("progress-reports")).StatusCode);

        var report = all.First();
        if (mine.All(r => r.Id != report.Id))
            Assert.Equal(HttpStatusCode.NotFound, (await amira.PostRaw($"progress-reports/{report.Id}/send", new SendProgressReportRequest("Not my student"))).StatusCode);
        await admin.Post($"progress-reports/{report.Id}/send", new SendProgressReportRequest("Steady progress this month (test)."));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostRaw($"progress-reports/{report.Id}/send", new SendProgressReportRequest(null))).StatusCode);
        using var check = api.Factory.Services.CreateScope();
        var note = await check.ServiceProvider.GetRequiredService<AppDbContext>().Notifications
            .SingleAsync(n => n.UserId == report.StudentId && n.Type == NotificationType.ProgressReport && n.DedupeKey == $"progress:{report.Id}");
        Assert.Contains("Steady progress this month (test).", note.Body);
    }

    [Fact]
    public async Task Holiday_clean_up_is_admin_only_and_needs_the_holiday_first()
    {
        var day = new DateOnly(2031, 1, 6);
        var admin = await api.LoginAsync(Accounts.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.LoginAsync(Accounts.Amira)).GetRaw($"institution/holiday-classes?date={day:yyyy-MM-dd}")).StatusCode);
        Assert.Empty((await admin.Get<HolidayClassesDto>($"institution/holiday-classes?date={day:yyyy-MM-dd}")).Classes);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw("institution/holiday-classes", new HolidayClassesRequest(day, "cancel"))).StatusCode);
        await admin.Post<IdResponse>("institution/holidays", new SaveHolidayRequest(day, "Test holiday"));
        Assert.Equal(0, (await admin.Post<HolidayClassesResult>("institution/holiday-classes", new HolidayClassesRequest(day, "move"))).Done);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw("institution/holiday-classes", new HolidayClassesRequest(day, "delete"))).StatusCode);
    }

    /// <summary>Owner decision 2026-11-24: nothing keeps acting for a deleted student, including a payment link already sent.</summary>
    [Fact]
    public async Task Deleting_a_student_closes_their_open_payment_link_and_no_new_link_can_be_made()
    {
        var stub = new Gateway();
        await using var factory = Stubbed(stub);
        var admin = await Login(factory, Accounts.OtherAdmin);
        await admin.Put("institution/connections/payments", new SavePaymentIntegrationRequest(true, PaymentProvider.Razorpay, "rzp_test_demo", "rzp-secret", "hook-secret"));
        var student = await NewStudentAsync(admin, null, null);
        var package = (await admin.Post<IdResponse>($"students/{student}/packages", new SavePackageRequest(10, 1000))).Id;
        await admin.Post<PaymentLinkDto>($"students/{student}/packages/{package}/payment-link", null);

        var removed = await (await admin.DeleteRaw($"students/{student}")).Content.ReadFromJsonAsync<RemovePersonResponse>(ApiFixture.Json);
        Assert.False(removed!.Deleted); // has a package: kept under Students ▸ Deleted
        Assert.Contains(stub.Requests, r => r.Uri.AbsolutePath == $"/v1/payment_links/{stub.LinkId(1)}/cancel");
        var again = await admin.PostRaw($"students/{student}/packages/{package}/payment-link", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("person_deleted", await again.Content.ReadAsStringAsync());

        // Restored: a link can be made again.
        await admin.Post($"students/{student}/restore", null);
        Assert.Equal(1000m, (await admin.Post<PaymentLinkDto>($"students/{student}/packages/{package}/payment-link", null)).Amount);
    }

    [Fact]
    public async Task A_payment_by_hand_closes_the_old_link_and_a_late_online_payment_is_kept_and_flagged()
    {
        var stub = new Gateway();
        await using var factory = Stubbed(stub);
        var admin = await Login(factory, Accounts.OtherAdmin);
        await admin.Put("institution/connections/payments", new SavePaymentIntegrationRequest(true, PaymentProvider.Razorpay, "rzp_test_demo", "rzp-secret", "hook-secret"));
        var student = await NewStudentAsync(admin, null, null);
        var package = (await admin.Post<IdResponse>($"students/{student}/packages", new SavePackageRequest(10, 1000))).Id;
        var first = await admin.Post<PaymentLinkDto>($"students/{student}/packages/{package}/payment-link", null);

        // Part paid in cash: the 1000 link is closed at the gateway; a new link asks for the 600 still owed.
        await admin.Post<IdResponse>($"students/{student}/packages/{package}/payments", new RecordPaymentRequest(400, Method: PaymentMethod.Cash));
        Assert.Contains(stub.Requests, r => r.Uri.AbsolutePath == $"/v1/payment_links/{stub.LinkId(1)}/cancel");
        var second = await admin.Post<PaymentLinkDto>($"students/{student}/packages/{package}/payment-link", null);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(600m, second.Amount);

        // The family pays the closed 1000 link anyway (the gateway let it through): the money is recorded and flagged.
        var tenantId = await TenantOf("northbridge-demo");
        using (var scope = factory.Services.CreateScope())
        using (scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Only one link per package can be open (database rule): retire the new one, re-open the old one.
            await db.PaymentLinks.Where(l => l.Id == second.Id).ExecuteUpdateAsync(u => u.SetProperty(l => l.Status, PaymentLinkStatus.Cancelled));
            await db.PaymentLinks.Where(l => l.Id == first.Id).ExecuteUpdateAsync(u => u.SetProperty(l => l.Status, PaymentLinkStatus.Open));
            stub.Paid.Add(stub.LinkId(1));
            await scope.ServiceProvider.GetRequiredService<StudentSync.Application.Fees.PaymentLinkService>().CheckOpenLinksAsync(CancellationToken.None);
            var adminId = admin.Auth.User.Id;
            Assert.Contains(await db.Notifications.Where(n => n.UserId == adminId && n.Type == NotificationType.PaymentReceived).Select(n => n.Title).ToListAsync(),
                t => t.Contains("more than was owed"));
        }
        var fees = await admin.Get<StudentFeesDto>($"students/{student}/fees");
        Assert.Equal(1400m, fees.Packages.Single().Payments.Where(p => !p.Voided).Sum(p => p.Amount));
    }

    [Fact]
    public async Task Overlapping_sms_runs_send_once_and_a_corrected_absence_is_not_sent()
    {
        var stub = new Gateway();
        await using var factory = Stubbed(stub);
        var admin = await Login(factory, Accounts.OtherAdmin);
        await admin.Put("institution/connections/sms", new SaveSmsIntegrationRequest(true, SmsProvider.Twilio, "AC" + new string('3', 32), "+15005550006", null, "twilio-token"));
        var student = await NewStudentAsync(admin, "+971 50 333 4444", null);
        var tenantId = await TenantOf("northbridge-demo");
        using (var scope = factory.Services.CreateScope())
        using (scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(tenantId))
        {
            var sms = scope.ServiceProvider.GetRequiredService<SmsOutbox>();
            await sms.QueueFamilyAsync(student, "test", "Overlap test (demo).", $"overlap:{student}", CancellationToken.None);
            // An absence that was corrected (no Absent record for this class any more).
            await sms.QueueFamilyAsync(student, "absence", "Absent (demo).", $"sms-absent:{Guid.NewGuid()}:{student}", CancellationToken.None);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.SaveChangesAsync();
            await db.OutboundMessages.Where(m => m.StudentId == student).ExecuteUpdateAsync(u => u.SetProperty(m => m.NotBeforeUtc, DateTime.UtcNow.AddMinutes(-1)));
        }
        async Task<int> Run()
        {
            using var scope = factory.Services.CreateScope();
            using var _ = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(tenantId);
            return await scope.ServiceProvider.GetRequiredService<SmsOutbox>().SendDueAsync(CancellationToken.None);
        }
        await Task.WhenAll(Run(), Run(), Run());
        Assert.Single(stub.Requests, r => r.Uri.Host == "api.twilio.com" && r.Body.Contains("To=%2B971503334444"));
        var log = (await admin.Get<List<OutboundMessageDto>>("automations/messages")).Where(m => m.StudentId == student).ToList();
        Assert.Equal("Sent", log.Single(m => m.Kind == "test").Status);
        var absence = log.Single(m => m.Kind == "absence");
        Assert.Equal(("Skipped", "Attendance was corrected before sending"), (absence.Status, absence.Error));
    }

    [Fact]
    public async Task A_failed_automation_is_reported_only_to_that_institutes_admins()
    {
        var horizon = await TenantOf("horizon-demo");
        var job = $"test-job-{Guid.NewGuid():N}"[..20];
        using (var scope = api.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<StudentSync.Application.Jobs.TenantJobRunner>().ForEachTenantAsync(job, (sp, _) =>
                sp.GetRequiredService<ITenantContext>().TenantId == horizon ? throw new InvalidOperationException("test failure") : Task.FromResult(0), CancellationToken.None);
        var horizonAdmin = (await api.LoginAsync(Accounts.Admin)).Auth.User.Id;
        var otherAdmin = (await api.LoginAsync(Accounts.OtherAdmin)).Auth.User.Id;
        using var check = api.Factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var alerted = await db.Notifications.IgnoreQueryFilters().Where(n => n.Type == NotificationType.AutomationFailed && n.DedupeKey.StartsWith($"job-failed:{job}"))
            .Select(n => n.UserId).ToListAsync();
        Assert.Contains(horizonAdmin, alerted);
        Assert.DoesNotContain(otherAdmin, alerted);
    }

    // ---------------- helpers ----------------

    private async Task<Guid> NewStudentAsync(Client admin, string? phone, string? guardianPhone)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        return (await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest($"Auto Test {tag} (Demo Student)", $"auto-{tag}@northbridge.example.test",
            $"AT-{tag}", null, null, null, Phone: phone, GuardianName: "Demo Guardian", GuardianPhone: guardianPhone))).Id;
    }

    private WebApplicationFactory<Program> Stubbed(Gateway stub) =>
        api.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddHttpClient(IntegrationClients.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => stub)));

    private static async Task<Client> Login(WebApplicationFactory<Program> factory, string email)
    {
        var http = factory.CreateClient();
        var login = await http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, ApiFixture.Password, null, "tests"), ApiFixture.Json);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(ApiFixture.Json))!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return new Client(http, auth);
    }

    private static HttpRequestMessage Webhook(string url, string body, string signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Razorpay-Signature", signature);
        return request;
    }

    /// <summary>Stands in for Twilio and Razorpay: accepts SMS, creates numbered links (plink_1, plink_2, …), reports a link
    /// paid only once it was marked paid here, and cancels links.</summary>
    private sealed class Gateway : HttpMessageHandler
    {
        public sealed record Seen(Uri Uri, string Method, string Body);
        public List<Seen> Requests { get; } = [];
        public HashSet<string> Paid { get; } = [];
        private readonly Dictionary<string, long> _amounts = [];
        private readonly string _tag = Guid.NewGuid().ToString("N")[..6];
        private int _links;
        /// <summary>Gateway ids are unique per stub, like real ones (tests share one database).</summary>
        public string LinkId(int n) => $"plink_{_tag}{n}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            lock (Requests) Requests.Add(new Seen(request.RequestUri!, request.Method.Method, body));
            var path = request.RequestUri!.AbsolutePath;
            if (request.RequestUri.Host == "api.twilio.com")
            {
                await Task.Delay(50, ct); // a little latency, so overlapping runs really overlap
                return Json(HttpStatusCode.Created, """{"sid":"SM00000000000000000000000000000002","status":"queued"}""");
            }
            if (request.RequestUri.Host != "api.razorpay.com") return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Post && path == "/v1/payment_links")
            {
                var n = Interlocked.Increment(ref _links);
                var amount = System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("amount").GetInt64();
                lock (_amounts) _amounts[LinkId(n)] = amount;
                return Json(HttpStatusCode.OK, $$"""{"id":"{{LinkId(n)}}","short_url":"https://rzp.io/i/{{n}}","status":"created"}""");
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/cancel")) return Json(HttpStatusCode.OK, """{"status":"cancelled"}""");
            if (request.Method == HttpMethod.Get && path.StartsWith("/v1/payment_links/plink_"))
            {
                var id = path["/v1/payment_links/".Length..];
                long amount;
                lock (_amounts) amount = _amounts.GetValueOrDefault(id);
                return Paid.Contains(id)
                    ? Json(HttpStatusCode.OK, $$"""{"id":"{{id}}","status":"paid","amount_paid":{{amount}},"payments":[{"payment_id":"pay_{{id}}","amount":{{amount}},"status":"captured"}]}""")
                    : Json(HttpStatusCode.OK, $$"""{"id":"{{id}}","status":"created","amount_paid":0}""");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
