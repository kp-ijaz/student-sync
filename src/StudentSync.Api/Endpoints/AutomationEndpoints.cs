using Microsoft.EntityFrameworkCore;
using StudentSync.Api.Hosting;
using StudentSync.Application.Automation;
using StudentSync.Application.Common;
using StudentSync.Application.Fees;
using StudentSync.Application.People;
using StudentSync.Application.Scheduling;
using StudentSync.Contracts;
using StudentSync.Domain.Identity;

namespace StudentSync.Api.Endpoints;

/// <summary>Settings ▸ Automations and what they produce: SMS log, salaries, payment links, receipts, progress reports, holiday clean-up.</summary>
public static class AutomationEndpoints
{
    public static void MapAutomations(this RouteGroupBuilder api)
    {
        var a = api.MapGroup("/automations").WithTags("Automations").RequirePermission(Permissions.InstitutionSettingsManage);
        a.MapGet("/", (AutomationSettingsService s, CancellationToken ct) => s.GetAsync(ct));
        a.MapPut("/", async (SaveAutomationSettingsRequest r, AutomationSettingsService s, CancellationToken ct) => { await s.SaveAsync(r, ct); return Results.NoContent(); });
        a.MapGet("/messages", (int? take, AutomationSettingsService s, CancellationToken ct) => s.MessagesAsync(take ?? 50, ct));

        // Holiday clean-up (classes on a holiday: cancel all, or move each to the next free day).
        var h = api.MapGroup("/institution/holiday-classes").WithTags("Institution").RequirePermission(Permissions.InstitutionSettingsManage);
        h.MapGet("/", (DateOnly date, HolidayClassesService s, CancellationToken ct) => s.ClassesOnAsync(date, ct));
        h.MapPost("/", (HolidayClassesRequest r, HolidayClassesService s, CancellationToken ct) => s.ApplyAsync(r, ct));

        // Salaries (Fees ▸ Salaries).
        var sal = api.MapGroup("/salaries").WithTags("Fees").RequirePermission(Permissions.UsersManage);
        sal.MapGet("/", (DateOnly? month, SalaryService s, IClock clock, CancellationToken ct) =>
            s.ForMonthAsync(month ?? DateOnly.FromDateTime(clock.UtcNow).AddMonths(-1), ct));
        sal.MapPost("/{teacherId:guid}/pay", async (Guid teacherId, PaySalaryRequest r, SalaryService s, CancellationToken ct) => new IdResponse(await s.PayAsync(teacherId, r, ct)))
            .AddEndpointFilter<IdempotencyFilter>();

        // Online payment link for what is owed on a package (admins).
        api.MapPost("/students/{id:guid}/packages/{packageId:guid}/payment-link", (Guid id, Guid packageId, PaymentLinkService s, CancellationToken ct) => s.ForPackageAsync(id, packageId, ct))
            .WithTags("Fees").RequirePermission(Permissions.UsersManage);

        // Gateway webhooks: anonymous, but only a valid signature (institute's own webhook secret) leads to a check, and the
        // payment is then confirmed by asking the gateway. Always 200 for a known institute so gateways do not retry forever.
        api.MapPost("/webhooks/payments/{provider}/{institution}", async (string provider, string institution, HttpRequest request, IAppDbContext db,
                ITenantContext tenant, IServiceProvider services, CancellationToken ct) =>
            {
                var tenantId = await db.Tenants.Where(t => t.Slug == institution.ToLower()).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
                if (tenantId is null) return Results.NotFound();
                using var reader = new StreamReader(request.Body);
                var body = await reader.ReadToEndAsync(ct);
                if (body.Length > 512_000) return Results.BadRequest();
                var headers = request.Headers.ToDictionary(x => x.Key.ToLowerInvariant(), x => x.Value.ToString());
                using var _ = tenant.BeginScope(tenantId.Value);
                var ok = await services.GetRequiredService<PaymentLinkService>().HandleWebhookAsync(provider, body, headers, ct);
                return ok ? Results.Ok() : Results.Unauthorized();
            })
            .AllowAnonymous().WithTags("Fees").RequireRateLimiting("webhooks");

        // Receipts (admins; the student it belongs to) and salary slips (admins; the teacher it belongs to).
        api.MapGet("/receipts/{paymentId:guid}", (Guid paymentId, ReceiptService s, CancellationToken ct) => s.ReceiptAsync(paymentId, ct)).WithTags("Fees");
        api.MapGet("/teachers/{id:guid}/pay/payments/{paymentId:guid}/slip", (Guid id, Guid paymentId, ReceiptService s, CancellationToken ct) => s.SlipAsync(id, paymentId, ct))
            .WithTags("Fees");

        // Monthly progress reports (teachers see their own; admins all).
        var pr = api.MapGroup("/progress-reports").WithTags("People");
        pr.MapGet("/", (string? status, ProgressReportService s, CancellationToken ct) => s.ListAsync(status, ct));
        pr.MapPost("/{id:guid}/send", async (Guid id, SendProgressReportRequest r, ProgressReportService s, CancellationToken ct) => { await s.SendAsync(id, r, ct); return Results.NoContent(); });
        pr.MapPost("/{id:guid}/skip", async (Guid id, ProgressReportService s, CancellationToken ct) => { await s.SkipAsync(id, ct); return Results.NoContent(); });
    }
}
