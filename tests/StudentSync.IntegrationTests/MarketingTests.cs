using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StudentSync.Application.Automation;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Marketing;

namespace StudentSync.IntegrationTests;

/// <summary>
/// Marketing (owner request 2026-11-24): the admin creates marketing team accounts; the team manages leads, calls and
/// campaigns and sees nothing else; figures add up; other roles and other institutions are shut out.
/// </summary>
public sealed class MarketingTests(ApiFixture api)
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<(Client Admin, Client Marketer, Guid MarketerId)> TeamAsync(string tag)
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var email = $"mk-{tag}@horizon.example.test";
        var created = await admin.Post<CreatedPersonResponse>("marketing/team", new CreateMarketingUserRequest($"Marketer {tag} (Demo)", email));
        var first = await api.LoginAsync(email, created.TemporaryPassword);
        await first.Post("me/password", new ChangePasswordRequest(created.TemporaryPassword, "MarketingPass123"));
        return (admin, await api.LoginAsync(email, "MarketingPass123"), created.Id);
    }

    [Fact]
    public async Task The_marketing_team_sees_marketing_only()
    {
        var (admin, marketer, _) = await TeamAsync(Tag());
        Assert.Equal(Domain.Identity.UserRole.Marketing, marketer.Auth.User.Role);

        // Their own module works.
        Assert.Equal(HttpStatusCode.OK, (await marketer.GetRaw("marketing/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await marketer.GetRaw("marketing/options")).StatusCode);

        // Nothing about students, teachers, classes, fees, salaries, finance or settings.
        var aisha = (await api.LoginAsync(Accounts.Aisha)).UserId;
        var leaks = new List<string>();
        foreach (var path in new[] { "students", $"students/{aisha}", "students/lookup", $"students/{aisha}/fees", $"students/{aisha}/hours", $"students/{aisha}/progress", "teachers",
                     "fees/due", "fee-plans", "finance/summary", "finance/entries", "finance/former", $"salaries?month={Today:yyyy-MM-01}", "institution/settings", "institution/audit",
                     "reports/institution", "reports/students", "dashboard/admin", "dashboard/teacher", "dashboard/student", "marketing/team", "academics/grades", "academics/subjects",
                     "academics/batches", "contacts", "conversations", "questions", "exams", "assignments", "doubts", "materials", "videos", "library/notes", "library/recordings",
                     "my/subjects", "me/learning-plan", "class-requests", "progress-reports", "ai/usage", "automations" })
        {
            var r = await marketer.GetRaw(path);
            if (r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) continue;
            // Anything else must at least be empty: an empty list or an empty page.
            var body = (await r.Content.ReadAsStringAsync()).Trim();
            if (r.IsSuccessStatusCode && (body == "[]" || body.Contains("\"items\":[]"))) continue;
            leaks.Add($"GET {path} → {(int)r.StatusCode} {body[..Math.Min(body.Length, 80)]}");
        }
        Assert.True(leaks.Count == 0, string.Join("\n", leaks));
        var sessions = await marketer.GetRaw("sessions?limit=50");
        Assert.True(sessions.StatusCode == HttpStatusCode.Forbidden || (await sessions.Content.ReadFromJsonAsync<List<SessionListItemDto>>(ApiFixture.Json))!.Count == 0);
        Assert.Equal(HttpStatusCode.Forbidden, (await marketer.PostRaw("marketing/team", new CreateMarketingUserRequest("X", $"x-{Tag()}@horizon.example.test"))).StatusCode);

        // Teachers and students cannot open marketing; another institution sees none of it.
        foreach (var account in new[] { Accounts.Amira, Accounts.Aisha })
            Assert.Equal(HttpStatusCode.Forbidden, (await (await api.LoginAsync(account)).GetRaw("marketing/leads")).StatusCode);
        var lead = (await admin.Post<IdResponse>("marketing/leads", new SaveLeadRequest($"Isolation {Tag()}", "+91 90000 11111", LeadSource.WalkIn))).Id;
        var outsider = await api.LoginAsync(Accounts.OtherAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetRaw($"marketing/leads/{lead}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.DeleteRaw($"marketing/leads/{lead}")).StatusCode);
        Assert.DoesNotContain((await outsider.Get<PagedResult<LeadDto>>("marketing/leads?pageSize=100")).Items, l => l.Id == lead);
    }

    [Fact]
    public async Task A_lead_is_added_called_followed_up_and_admitted()
    {
        var tag = Tag();
        var (admin, marketer, marketerId) = await TeamAsync(tag);
        var campaign = (await marketer.Post<IdResponse>("marketing/campaigns", new SaveCampaignRequest($"Insta {tag}", LeadSource.Instagram, Today.AddDays(-10), null, 10000, CampaignStatus.Running, "Kochi"))).Id;
        // What was paid is recorded as spend: two payments, 4,000 in all. It is the same money Finance shows under Marketing.
        await marketer.Post<IdResponse>($"marketing/campaigns/{campaign}/spend", new AddCampaignSpendRequest(2500, Today, "Ad boost"));
        var spendId = (await marketer.Post<IdResponse>($"marketing/campaigns/{campaign}/spend", new AddCampaignSpendRequest(1500, Today))).Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await marketer.PostRaw($"marketing/campaigns/{campaign}/spend", new AddCampaignSpendRequest(0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await marketer.PostRaw($"marketing/campaigns/{campaign}/spend", new AddCampaignSpendRequest(10, Today.AddDays(2)))).StatusCode);
        Assert.Equal(2, (await marketer.Get<List<CampaignSpendDto>>($"marketing/campaigns/{campaign}/spend")).Count);
        var inFinance = (await admin.Get<FinanceEntriesDto>($"finance/entries?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&category=Marketing&search=Ad boost&pageSize=100")).Page.Items;
        Assert.Contains(inFinance, e => e.Amount == 2500 && e.Category == Domain.Fees.ExpenseCategory.Marketing);
        var phone = $"+91 9{Random.Shared.Next(100000000, 999999999)}";
        var request = new SaveLeadRequest($"Lead {tag}", phone, LeadSource.Instagram, "Parent (fictional)", CampaignId: campaign, AssignedToUserId: marketerId, Interest: "Maths, Class 10");
        var id = (await marketer.Post<IdResponse>("marketing/leads", request)).Id;

        // The same family calling again is not a second lead.
        Assert.Equal(HttpStatusCode.BadRequest, (await marketer.PostRaw("marketing/leads", request with { Phone = phone.Replace(" ", "") })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await marketer.PostRaw("marketing/leads", new SaveLeadRequest("X", "12", LeadSource.WalkIn))).StatusCode);

        // A missed call, then a call that reached them with a follow-up day: New → Contacted → Interested.
        await marketer.Post<IdResponse>($"marketing/leads/{id}/activities", new SaveLeadActivityRequest(LeadActivityKind.Call, LeadActivityOutcome.NoAnswer));
        Assert.Equal(LeadStatus.Contacted, (await marketer.Get<LeadDetailDto>($"marketing/leads/{id}")).Lead.Status);
        var call = (await marketer.Post<IdResponse>($"marketing/leads/{id}/activities",
            new SaveLeadActivityRequest(LeadActivityKind.Call, LeadActivityOutcome.Interested, null, 6, "Wants a trial class", LeadStatus.Interested, Today.AddDays(2)))).Id;
        var detail = await marketer.Get<LeadDetailDto>($"marketing/leads/{id}");
        Assert.Equal((LeadStatus.Interested, Today.AddDays(2), 2, false), (detail.Lead.Status, detail.Lead.NextFollowUpOn, detail.Lead.Calls, detail.Lead.FollowUpOverdue));
        Assert.Equal(2, detail.Activities.Count);

        // Found by search, by the digits of the phone number, by campaign and in the coming week's follow-ups.
        Assert.Contains((await marketer.Get<PagedResult<LeadDto>>($"marketing/leads?search={tag}")).Items, l => l.Id == id);
        Assert.Contains((await marketer.Get<PagedResult<LeadDto>>($"marketing/leads?search={phone[^6..]}")).Items, l => l.Id == id);
        Assert.Contains((await marketer.Get<PagedResult<LeadDto>>($"marketing/leads?campaignId={campaign}&open=true&followUp=week&sort=FollowUp")).Items, l => l.Id == id);
        Assert.DoesNotContain((await marketer.Get<PagedResult<LeadDto>>($"marketing/leads?search={tag}&followUp=overdue")).Items, l => l.Id == id);
        Assert.Contains($"Lead {tag}", await marketer.Http.GetStringAsync($"/api/v1/marketing/leads.csv?search={tag}"));

        // An entry is corrected by whoever logged it (or the admin); someone else on the team cannot change it.
        await marketer.Put($"marketing/leads/{id}/activities/{call}", new SaveLeadActivityRequest(LeadActivityKind.Call, LeadActivityOutcome.Interested, null, 8, "Wants a trial class on Saturday"));
        var (_, colleague, _) = await TeamAsync(Tag());
        Assert.Equal(HttpStatusCode.Forbidden, (await colleague.PutRaw($"marketing/leads/{id}/activities/{call}", new SaveLeadActivityRequest(LeadActivityKind.Call))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await colleague.DeleteRaw($"marketing/leads/{id}/activities/{call}")).StatusCode);

        // Only the admin links the admitted student; the lead closes and its follow-up is cleared.
        var student = (await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest($"Lead {tag} (Demo Student)", $"lead-{tag}@horizon.example.test", $"LD-{tag}", null, null, "Asia/Dubai"))).Id;
        Assert.Equal(HttpStatusCode.Forbidden, (await marketer.PostRaw($"marketing/leads/{id}/student", new LinkLeadStudentRequest(student))).StatusCode);
        await admin.Post($"marketing/leads/{id}/student", new LinkLeadStudentRequest(student));
        detail = await marketer.Get<LeadDetailDto>($"marketing/leads/{id}");
        Assert.Equal((LeadStatus.Admitted, student, (DateOnly?)null), (detail.Lead.Status, detail.Lead.ConvertedStudentId, detail.Lead.NextFollowUpOn));

        // The overview counts it: one lead, admitted, two calls of which one reached; the campaign shows its cost per lead.
        var o = await marketer.Get<MarketingOverviewDto>($"marketing/overview?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}");
        Assert.True(o.Leads >= 1 && o.Admitted >= 1 && o.Calls >= 2 && o.CallsReached >= 1 && o.CallsReached < o.Calls);
        var row = Assert.Single(o.Campaigns, c => c.Id == campaign);
        Assert.Equal((1, 1, 4000m, 4000m, 100m), (row.Leads, row.Admitted, row.CostPerLead, row.CostPerAdmission, row.ConversionPercent));
        var mine = Assert.Single(o.Team, t => t.UserId == marketerId);
        Assert.Equal((2, 1, 1), (mine.Calls, mine.CallsReached, mine.Admitted));
        Assert.Contains(o.Sources, s => s.Source == LeadSource.Instagram && s.Leads >= 1);

        // A colleague cannot remove spend they did not record; deleting the campaign leaves the money in Finance.
        Assert.Equal(HttpStatusCode.Forbidden, (await colleague.DeleteRaw($"marketing/campaigns/{campaign}/spend/{spendId}")).StatusCode);

        // Deleting the campaign keeps the lead; deleting the lead removes its log; a team member with calls is switched off, not deleted.
        Assert.Equal(HttpStatusCode.NoContent, (await marketer.DeleteRaw($"marketing/campaigns/{campaign}")).StatusCode);
        Assert.Null((await marketer.Get<LeadDetailDto>($"marketing/leads/{id}")).Lead.CampaignId);
        Assert.Contains((await admin.Get<FinanceEntriesDto>($"finance/entries?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&category=Marketing&search=Ad boost&pageSize=100")).Page.Items, e => e.Amount == 2500);
        var removed = await (await admin.DeleteRaw($"marketing/team/{marketerId}")).Content.ReadFromJsonAsync<RemovePersonResponse>(ApiFixture.Json);
        Assert.False(removed!.Deleted);
        Assert.Contains(await admin.Get<List<MarketingUserDto>>("marketing/team"), u => u.Id == marketerId && !u.IsActive);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteRaw($"marketing/leads/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetRaw($"marketing/leads/{id}")).StatusCode);
    }

    [Fact]
    public async Task Overdue_follow_ups_campaign_rules_and_team_accounts()
    {
        var tag = Tag();
        var (admin, marketer, marketerId) = await TeamAsync(tag);

        // A lead entered with a follow-up day in the past is overdue until it is closed.
        var id = (await marketer.Post<IdResponse>("marketing/leads", new SaveLeadRequest($"Overdue {tag}", $"+91 8{Random.Shared.Next(100000000, 999999999)}", LeadSource.Referral,
            Status: LeadStatus.Interested, NextFollowUpOn: Today.AddDays(-3), EnquiredOn: Today.AddDays(-10)))).Id;
        Assert.Contains((await marketer.Get<PagedResult<LeadDto>>($"marketing/leads?followUp=overdue&search={tag}")).Items, l => l.Id == id && l.FollowUpOverdue);
        Assert.True((await marketer.Get<MarketingOverviewDto>("marketing/overview")).FollowUpsOverdue >= 1);
        await marketer.Post<IdResponse>($"marketing/leads/{id}/activities", new SaveLeadActivityRequest(LeadActivityKind.WhatsApp, LeadActivityOutcome.NotInterested, SetStatus: LeadStatus.NotInterested));
        var closed = (await marketer.Get<LeadDetailDto>($"marketing/leads/{id}")).Lead;
        Assert.Equal((LeadStatus.NotInterested, (DateOnly?)null, false), (closed.Status, closed.NextFollowUpOn, closed.FollowUpOverdue));

        // Campaign validation and edit.
        Assert.Equal(HttpStatusCode.BadRequest, (await marketer.PostRaw("marketing/campaigns", new SaveCampaignRequest($"Bad {tag}", LeadSource.Flyer, Today, Today.AddDays(-1)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await marketer.PostRaw("marketing/campaigns", new SaveCampaignRequest($"Bad {tag}", LeadSource.Flyer, Today, null, -5))).StatusCode);
        var campaign = (await marketer.Post<IdResponse>("marketing/campaigns", new SaveCampaignRequest($"Flyers {tag}", LeadSource.Flyer, Today, null, 2000))).Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await marketer.PostRaw("marketing/campaigns", new SaveCampaignRequest($"flyers {tag}", LeadSource.Flyer, Today))).StatusCode); // same name
        await marketer.Put($"marketing/campaigns/{campaign}", new SaveCampaignRequest($"Flyers {tag}", LeadSource.Flyer, Today, Today.AddDays(30), 2000, CampaignStatus.Paused, "Near the school"));
        await marketer.Post<IdResponse>($"marketing/campaigns/{campaign}/spend", new AddCampaignSpendRequest(750, Today, "Printing"));
        var saved = (await marketer.Get<List<CampaignDto>>("marketing/campaigns")).Single(c => c.Id == campaign);
        Assert.Equal((750m, CampaignStatus.Paused, (decimal?)null), (saved.Spent, saved.Status, saved.CostPerLead)); // no leads yet: no made-up cost
        await marketer.DeleteRaw($"marketing/campaigns/{campaign}");

        // Team accounts: rename, switch off (cannot sign in), reset password, delete for good when nothing was logged.
        var fresh = await admin.Post<CreatedPersonResponse>("marketing/team", new CreateMarketingUserRequest($"Fresh {tag} (Demo)", $"fresh-{tag}@horizon.example.test"));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostRaw("marketing/team", new CreateMarketingUserRequest("Dup", $"fresh-{tag}@horizon.example.test"))).StatusCode);
        await admin.Put($"marketing/team/{fresh.Id}", new UpdateMarketingUserRequest($"Fresh {tag} Renamed", false));
        var login = await api.Anonymous().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest($"fresh-{tag}@horizon.example.test", fresh.TemporaryPassword, null, "tests"), ApiFixture.Json);
        Assert.False(login.IsSuccessStatusCode);
        Assert.NotEqual(fresh.TemporaryPassword, (await admin.Post<TemporaryPasswordResponse>($"marketing/team/{fresh.Id}/password-reset", null)).TemporaryPassword);
        Assert.True((await (await admin.DeleteRaw($"marketing/team/{fresh.Id}")).Content.ReadFromJsonAsync<RemovePersonResponse>(ApiFixture.Json))!.Deleted);
        Assert.DoesNotContain(await admin.Get<List<MarketingUserDto>>("marketing/team"), u => u.Id == fresh.Id);

        // The team routes never touch teachers or students: a teacher's id is "not found" there.
        var teacherId = (await api.LoginAsync(Accounts.Amira)).UserId;
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteRaw($"marketing/team/{teacherId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutRaw($"marketing/team/{teacherId}", new UpdateMarketingUserRequest("X", false))).StatusCode);
        Assert.Contains(await admin.Get<List<MarketingUserDto>>("marketing/team"), u => u.Id == marketerId);
    }

    [Fact]
    public async Task Website_form_import_trials_referrals_targets_and_reminders()
    {
        var tag = Tag();
        var (admin, marketer, marketerId) = await TeamAsync(tag);
        var http = api.Anonymous();
        var code = "horizon";
        var form = await admin.Get<EnquiryFormDto>("marketing/enquiry-form");
        code = form.Code;

        // The public form is closed until the admin switches it on (only the admin can).
        await admin.Put("marketing/enquiry-form", new SetEnquiryFormRequest(false));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/v1/public/enquiry/{code}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsJsonAsync($"/api/v1/public/enquiry/{code}", new PublicEnquiryRequest("X", "9876543210"), ApiFixture.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await marketer.PutRaw("marketing/enquiry-form", new SetEnquiryFormRequest(true))).StatusCode);
        await admin.Put("marketing/enquiry-form", new SetEnquiryFormRequest(true));

        // Open: it shows only the centre's name and classes; an enquiry becomes a Website lead and the team is told (no names in the notice).
        var info = await http.GetFromJsonAsync<PublicEnquiryInfoDto>($"/api/v1/public/enquiry/{code}", ApiFixture.Json);
        Assert.NotEmpty(info!.Grades);
        var phone = $"98{Random.Shared.Next(10000000, 99999999)}";
        var enquiry = new PublicEnquiryRequest($"Web {tag}", phone, "Parent (fictional)", info.Grades[0].Id, "Maths");
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync($"/api/v1/public/enquiry/{code}", enquiry, ApiFixture.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync($"/api/v1/public/enquiry/{code}", enquiry, ApiFixture.Json)).StatusCode); // sent twice: one lead
        var web = Assert.Single((await marketer.Get<PagedResult<LeadDto>>($"marketing/leads?websiteNew=true&search={tag}")).Items);
        Assert.Equal((LeadSource.Website, LeadStatus.New, true), (web.Source, web.Status, web.FromWebsiteForm));
        var notice = (await marketer.Get<PagedResult<NotificationDto>>("notifications?pageSize=50")).Items.First(n => n.Link == $"/marketing/leads/{web.Id}");
        Assert.DoesNotContain(tag, notice.Title + notice.Body);
        // A bot that fills the hidden field is accepted but nothing is stored; bad input is refused.
        await http.PostAsJsonAsync($"/api/v1/public/enquiry/{code}", new PublicEnquiryRequest($"Bot {tag}", "9811111111", Website: "http://spam.example"), ApiFixture.Json);
        Assert.Empty((await marketer.Get<PagedResult<LeadDto>>($"marketing/leads?search=Bot {tag}")).Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync($"/api/v1/public/enquiry/{code}", new PublicEnquiryRequest($"Bad {tag}", "call me"), ApiFixture.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/v1/public/enquiry/no-such-centre")).StatusCode);

        // Import from a spreadsheet: header skipped, a line without a phone and a repeated person reported.
        var rows = $"Name,Phone,Parent,Class,Asked about\nImp A {tag},97{Random.Shared.Next(10000000, 99999999)},Parent A,{info.Grades[0].Name},Physics\nImp B {tag}\t96{Random.Shared.Next(10000000, 99999999)}\nNo Phone {tag},\nWeb {tag},{phone}";
        var imported = await marketer.Post<ImportLeadsResult>("marketing/leads/import", new ImportLeadsRequest(rows, LeadSource.Facebook));
        Assert.Equal((2, 2), (imported.Added, imported.Skipped));
        var a = Assert.Single((await marketer.Get<PagedResult<LeadDto>>($"marketing/leads?search=Imp A {tag}")).Items);
        Assert.Equal((LeadSource.Facebook, info.Grades[0].Id, "Physics"), (a.Source, a.GradeLevelId, a.Interest));

        // A trial class and who referred them are kept on the lead and shown on the overview.
        var trialAt = DateTime.UtcNow.AddDays(2);
        await marketer.Put($"marketing/leads/{a.Id}", new SaveLeadRequest(a.StudentName, a.Phone, LeadSource.Referral, Status: LeadStatus.TrialBooked, AssignedToUserId: marketerId,
            ReferredBy: $"Referrer {tag}", TrialAtUtc: trialAt, TrialNotes: "Maths, at the centre"));
        Assert.Contains((await marketer.Get<PagedResult<LeadDto>>($"marketing/leads?trial=upcoming&search={tag}")).Items, l => l.Id == a.Id && l.TrialNotes == "Maths, at the centre");
        var o = await marketer.Get<MarketingOverviewDto>($"marketing/overview?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}");
        Assert.Contains(o.UpcomingTrials, l => l.Id == a.Id);
        Assert.Contains(o.Referrers, r => r.Key == $"Referrer {tag}" && r.Count == 1);
        Assert.True(o.WebsiteFormNew >= 1);

        // Targets set by the admin show against what was done (2 leads typed in by this member today via the import).
        await admin.Put($"marketing/team/{marketerId}", new UpdateMarketingUserRequest($"Marketer {tag} (Demo)", true, 20, 40, 5));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"marketing/team/{marketerId}", new UpdateMarketingUserRequest("X", true, -1))).StatusCode);
        Assert.Contains(await admin.Get<List<MarketingUserDto>>("marketing/team"), u => u.Id == marketerId && (u.CallsPerDay, u.LeadsPerMonth, u.AdmissionsPerMonth) == (20, 40, 5));
        var month = new DateOnly(Today.Year, Today.Month, 1);
        var row = (await marketer.Get<MarketingOverviewDto>($"marketing/overview?from={month:yyyy-MM-dd}&to={month.AddMonths(1).AddDays(-1):yyyy-MM-dd}")).Team.Single(t => t.UserId == marketerId);
        Assert.Equal((2, 40, 5), (row.LeadsAdded, row.LeadsTarget, row.AdmissionsTarget));
        Assert.True(row.CallsTarget is > 0 and <= 20 * 27);

        // The morning reminder tells the member how many follow-ups are due; once a day, with counts only.
        await marketer.Put($"marketing/leads/{web.Id}", new SaveLeadRequest(web.StudentName, web.Phone, LeadSource.Website, Status: LeadStatus.Contacted, AssignedToUserId: marketerId, NextFollowUpOn: Today.AddDays(-1)));
        await marketer.Post<IdResponse>("marketing/leads", new SaveLeadRequest($"Due {tag}", $"95{Random.Shared.Next(10000000, 99999999)}", LeadSource.WalkIn, AssignedToUserId: marketerId, NextFollowUpOn: Today));
        Assert.Equal(2, (await marketer.Get<PagedResult<LeadDto>>($"marketing/leads?followUp=due&assignedToUserId={marketerId}")).TotalCount);
        for (var run = 0; run < 2; run++)
        {
            using var scope = api.Factory.Services.CreateScope();
            using (scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(marketer.Auth.User.TenantId!.Value))
                await scope.ServiceProvider.GetRequiredService<DailyAutomations>().LeadFollowUpsAsync(Today, CancellationToken.None);
        }
        var reminders = (await marketer.Get<PagedResult<NotificationDto>>("notifications?pageSize=50")).Items.Where(n => n.Link == "/marketing/follow-ups").ToList();
        Assert.Equal("2 leads to follow up today (1 overdue)", Assert.Single(reminders).Title);

        await admin.Put("marketing/enquiry-form", new SetEnquiryFormRequest(false));
    }

    [Fact]
    public async Task A_student_on_hold_keeps_their_sign_in_and_fees_but_cannot_be_booked()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Tag();
        var email = $"hold-{tag}@horizon.example.test";
        var created = await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest($"Hold {tag} (Demo Student)", email, $"HD-{tag}", null, null, "Asia/Dubai"));
        await admin.Post<IdResponse>($"students/{created.Id}/packages", new SavePackageRequest(10, 5000, PaidNow: 1000));
        var subject = (await admin.Post<IdResponse>("academics/centre-subjects", new CentreSubjectRequest($"Hold {tag}"))).Id;
        var teacher = (await admin.Post<CreatedPersonResponse>("teachers", new CreateTeacherRequest($"Hold {tag} (Demo Teacher)", $"hold-t-{tag}@horizon.example.test", null, null, null, "Asia/Dubai", [subject]))).Id;
        await admin.Put($"students/{created.Id}", new UpdateStudentRequest($"Hold {tag} (Demo Student)", $"HD-{tag}", null, null, Domain.People.PersonStatus.OnHold, "Asia/Dubai"));

        // Still signs in; still on the current fee list (not "people who left"); found by the On hold filter.
        var student = await api.LoginAsync(email, created.TemporaryPassword);
        Assert.Equal(created.Id, student.UserId);
        var due = Assert.Single((await admin.Get<FeesDueDto>("fees/due?take=200")).Items, i => i.StudentId == created.Id);
        Assert.Equal((4000m, Domain.People.PersonStatus.OnHold), (due.Due, due.Status));
        Assert.DoesNotContain((await admin.Get<FormerBalancesDto>("finance/former")).Students, s => s.StudentId == created.Id);
        Assert.Contains((await admin.Get<PagedResult<StudentDto>>($"students?status=OnHold&search=HD-{tag}")).Items, s => s.Id == created.Id);

        // No new classes while on hold; booking works again once Active.
        var booking = new CreateSessionRequest("Hold class", Domain.Scheduling.SessionType.OneToOne, teacher, null, null, null, subject, null, [created.Id], "Asia/Dubai",
            Today.AddDays(Random.Shared.Next(5, 40)), new TimeOnly(Random.Shared.Next(6, 20), 0), 60, Domain.Scheduling.DeliveryMode.InPerson, null, null, null, null);
        var refused = await admin.PostRaw("sessions", booking);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("on hold", await refused.Content.ReadAsStringAsync());
        await admin.Put($"students/{created.Id}", new UpdateStudentRequest($"Hold {tag} (Demo Student)", $"HD-{tag}", null, null, Domain.People.PersonStatus.Active, "Asia/Dubai"));
        Assert.Single((await admin.Post<CreateSessionResponse>("sessions", booking)).SessionIds);

        // "On hold" is for students: a teacher cannot be put on hold.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutRaw($"teachers/{teacher}", new UpdateTeacherRequest($"Hold {tag} (Demo Teacher)", null, null, null, Domain.People.PersonStatus.OnHold, "Asia/Dubai"))).StatusCode);
    }

    [Fact]
    public async Task Wrong_passwords_lock_the_account_without_touching_other_account_details()
    {
        var admin = await api.LoginAsync(Accounts.Admin);
        var tag = Tag();
        var email = $"lock-{tag}@horizon.example.test";
        var created = await admin.Post<CreatedPersonResponse>("students", new CreateStudentRequest($"Lock {tag} (Demo Student)", email, $"LK-{tag}", null, null, "Asia/Dubai"));
        var http = api.Anonymous();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 12 && !statuses.Contains(HttpStatusCode.Locked); i++)
            statuses.Add((await http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "wrong-password-1A", null, "tests"), ApiFixture.Json)).StatusCode);
        Assert.Contains(HttpStatusCode.Locked, statuses);
        Assert.All(statuses.SkipLast(1), s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        // Locked: even the right password is refused until the lock ends; the student's name was not rewritten by the attempts.
        Assert.Equal(HttpStatusCode.Locked, (await http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, created.TemporaryPassword, null, "tests"), ApiFixture.Json)).StatusCode);
        Assert.Equal($"Lock {tag} (Demo Student)", (await admin.Get<StudentProfileDto>($"students/{created.Id}")).Student.FullName);
    }
}
