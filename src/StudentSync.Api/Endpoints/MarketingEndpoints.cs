using StudentSync.Api.Hosting;
using StudentSync.Application.Common;
using StudentSync.Application.Marketing;
using StudentSync.Contracts;

namespace StudentSync.Api.Endpoints;

/// <summary>
/// Marketing: leads, their calls and follow-ups, campaigns, the overview and the team's accounts. Every route checks
/// <c>marketing.manage</c> (admins and the marketing team) in the service; team accounts and linking a lead to a student
/// need <c>users.manage</c> (admins only).
/// </summary>
public static class MarketingEndpoints
{
    public static void MapMarketing(this RouteGroupBuilder api)
    {
        // Public website enquiry form: no sign-in. Narrow by design (see PublicEnquiryService) and rate limited.
        var pub = api.MapGroup("/public/enquiry").WithTags("Marketing").AllowAnonymous().RequireRateLimiting("enquiry");
        pub.MapGet("/{code}", (string code, PublicEnquiryService s, CancellationToken ct) => s.InfoAsync(code, ct));
        pub.MapPost("/{code}", async (string code, PublicEnquiryRequest r, PublicEnquiryService s, CancellationToken ct) => { await s.SubmitAsync(code, r, ct); return Results.NoContent(); });

        var m = api.MapGroup("/marketing").WithTags("Marketing");
        m.MapGet("/options", (MarketingService s, CancellationToken ct) => s.OptionsAsync(ct));
        m.MapGet("/overview", (DateOnly? from, DateOnly? to, MarketingService s, CancellationToken ct) => s.OverviewAsync(from, to, ct));

        m.MapGet("/leads", ([AsParameters] LeadQuery q, MarketingService s, CancellationToken ct) => s.LeadsAsync(q, ct));
        m.MapGet("/leads.csv", async ([AsParameters] LeadQuery q, MarketingService s, CancellationToken ct) =>
            Results.File(System.Text.Encoding.UTF8.GetBytes(await s.LeadsCsvAsync(q, ct)), "text/csv", "leads.csv"));
        m.MapGet("/leads/{id:guid}", (Guid id, MarketingService s, CancellationToken ct) => s.LeadAsync(id, ct));
        m.MapPost("/leads", async (SaveLeadRequest r, MarketingService s, CancellationToken ct) => new IdResponse(await s.SaveLeadAsync(null, r, ct))).AddEndpointFilter<IdempotencyFilter>();
        m.MapPut("/leads/{id:guid}", async (Guid id, SaveLeadRequest r, MarketingService s, CancellationToken ct) => new IdResponse(await s.SaveLeadAsync(id, r, ct)));
        m.MapDelete("/leads/{id:guid}", async (Guid id, MarketingService s, CancellationToken ct) => { await s.DeleteLeadAsync(id, ct); return Results.NoContent(); });
        m.MapPost("/leads/{id:guid}/student", async (Guid id, LinkLeadStudentRequest r, MarketingService s, CancellationToken ct) => { await s.LinkStudentAsync(id, r, ct); return Results.NoContent(); });

        m.MapPost("/leads/{id:guid}/activities", async (Guid id, SaveLeadActivityRequest r, MarketingService s, CancellationToken ct) => new IdResponse(await s.SaveActivityAsync(id, null, r, ct))).AddEndpointFilter<IdempotencyFilter>();
        m.MapPut("/leads/{id:guid}/activities/{activityId:guid}", async (Guid id, Guid activityId, SaveLeadActivityRequest r, MarketingService s, CancellationToken ct) => new IdResponse(await s.SaveActivityAsync(id, activityId, r, ct)));
        m.MapDelete("/leads/{id:guid}/activities/{activityId:guid}", async (Guid id, Guid activityId, MarketingService s, CancellationToken ct) => { await s.DeleteActivityAsync(id, activityId, ct); return Results.NoContent(); });

        m.MapPost("/leads/import", (ImportLeadsRequest r, MarketingService s, CancellationToken ct) => s.ImportAsync(r, ct));
        m.MapGet("/enquiry-form", (MarketingService s, CancellationToken ct) => s.EnquiryFormAsync(ct));
        m.MapPut("/enquiry-form", async (SetEnquiryFormRequest r, MarketingService s, CancellationToken ct) => { await s.SetEnquiryFormAsync(r, ct); return Results.NoContent(); });

        m.MapGet("/campaigns/{id:guid}/spend", (Guid id, MarketingService s, CancellationToken ct) => s.SpendAsync(id, ct));
        m.MapPost("/campaigns/{id:guid}/spend", async (Guid id, AddCampaignSpendRequest r, MarketingService s, CancellationToken ct) => new IdResponse(await s.AddSpendAsync(id, r, ct))).AddEndpointFilter<IdempotencyFilter>();
        m.MapDelete("/campaigns/{id:guid}/spend/{expenseId:guid}", async (Guid id, Guid expenseId, MarketingService s, CancellationToken ct) => { await s.DeleteSpendAsync(id, expenseId, ct); return Results.NoContent(); });
        m.MapGet("/campaigns", (MarketingService s, CancellationToken ct) => s.CampaignsAsync(ct));
        m.MapPost("/campaigns", async (SaveCampaignRequest r, MarketingService s, CancellationToken ct) => new IdResponse(await s.SaveCampaignAsync(null, r, ct)));
        m.MapPut("/campaigns/{id:guid}", async (Guid id, SaveCampaignRequest r, MarketingService s, CancellationToken ct) => new IdResponse(await s.SaveCampaignAsync(id, r, ct)));
        m.MapDelete("/campaigns/{id:guid}", async (Guid id, MarketingService s, CancellationToken ct) => { await s.DeleteCampaignAsync(id, ct); return Results.NoContent(); });

        // The marketing team's sign-in accounts: created and managed by the admin only (no self-registration).
        m.MapGet("/team", (MarketingService s, CancellationToken ct) => s.TeamAsync(ct));
        m.MapPost("/team", (CreateMarketingUserRequest r, MarketingService s, IUserAccountService accounts, CancellationToken ct) => s.CreateTeamMemberAsync(r, accounts, ct));
        m.MapPut("/team/{id:guid}", async (Guid id, UpdateMarketingUserRequest r, MarketingService s, IUserAccountService accounts, CancellationToken ct) => { await s.UpdateTeamMemberAsync(id, r, accounts, ct); return Results.NoContent(); });
        m.MapPost("/team/{id:guid}/password-reset", (Guid id, MarketingService s, IUserAccountService accounts, CancellationToken ct) => s.ResetTeamPasswordAsync(id, accounts, ct));
        m.MapDelete("/team/{id:guid}", (Guid id, MarketingService s, IUserAccountService accounts, CancellationToken ct) => s.DeleteTeamMemberAsync(id, accounts, ct));
    }
}
