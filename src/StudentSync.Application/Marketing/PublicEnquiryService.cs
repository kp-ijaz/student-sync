using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Common;
using StudentSync.Domain.Communication;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Marketing;

namespace StudentSync.Application.Marketing;

/// <summary>
/// The public website enquiry form (owner request 2026-11-24): anyone can send their name, phone, class and what they
/// need, and it becomes a lead (source Website) for the marketing team. No sign-in, so it is deliberately narrow: only
/// when the admin switched it on, only those few fields, a hidden field that drops spam bots, a daily cap per centre,
/// and it reveals nothing but the centre's name and class list. The same family sending twice makes one lead.
/// </summary>
public sealed class PublicEnquiryService(IAppDbContext db, ITenantContext tenant, IClock clock, NotificationService notify)
{
    /// <summary>More website enquiries than this in one day are refused (protects against flooding).</summary>
    public const int MaxPerDay = 100;

    private async Task<Domain.Tenancy.Tenant> CentreAsync(string code, CancellationToken ct)
    {
        code = (code ?? "").Trim().ToLowerInvariant();
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.Slug == code && x.Status == Domain.Tenancy.TenantStatus.Active, ct);
        // Unknown centre and "form switched off" look the same from outside.
        return t is { PublicEnquiryEnabled: true } ? t : throw new NotFoundException("Enquiry form");
    }

    public async Task<PublicEnquiryInfoDto> InfoAsync(string code, CancellationToken ct)
    {
        var t = await CentreAsync(code, ct);
        using var _ = tenant.BeginScope(t.Id);
        var grades = await db.GradeLevels.OrderBy(g => g.SortOrder).Select(g => new LookupItem(g.Id, g.Name, null)).ToListAsync(ct);
        return new PublicEnquiryInfoDto(t.Name, grades);
    }

    public async Task SubmitAsync(string code, PublicEnquiryRequest r, CancellationToken ct)
    {
        var t = await CentreAsync(code, ct);
        if (!string.IsNullOrEmpty(r.Website)) return; // the hidden field was filled in: a bot. Accept silently, store nothing.
        var name = Text.Required(r.StudentName, "studentName", 150);
        var phone = Text.Required(r.Phone, "phone", 30);
        if (MarketingRules.PhoneKey(phone).Length < 6 || phone.Any(c => !(char.IsDigit(c) || " +-()".Contains(c))))
            throw RequestValidationException.For("phone", "Enter a phone number we can call you on.");
        var guardian = Text.Optional(r.GuardianName, "guardianName", 150);
        var interest = Text.Optional(r.Interest, "interest", 300);

        using var _ = tenant.BeginScope(t.Id);
        if (r.GradeLevelId is { } g && !await db.GradeLevels.AnyAsync(x => x.Id == g, ct)) throw RequestValidationException.For("gradeLevelId", "Choose a class from the list.");
        var today = DateOnly.FromDateTime(Domain.Scheduling.ZonedTime.ToLocal(clock.UtcNow, t.DefaultTimeZoneId));
        if (await db.Leads.CountAsync(l => l.FromWebsiteForm && l.EnquiredOn == today, ct) >= MaxPerDay)
            throw new DomainException("We could not take your enquiry online just now. Please call the centre.", "enquiry_limit");

        // The same family sending the form again: keep the lead they already have.
        if (await MarketingService.OpenLeadExistsAsync(db, name, phone, ct)) return;

        var lead = new Lead { StudentName = name, Phone = phone, GuardianName = guardian, GradeLevelId = r.GradeLevelId, Interest = interest, Source = LeadSource.Website, EnquiredOn = today, FromWebsiteForm = true };
        db.Leads.Add(lead);
        var team = await db.Users.Where(u => u.TenantId == t.Id && u.IsActive && (u.Role == UserRole.Marketing || u.Role == UserRole.InstitutionAdmin)).Select(u => u.Id).ToListAsync(ct);
        // The notification names nobody: the details are on the lead, behind sign-in.
        await notify.NotifyAsync(team, NotificationType.LeadFromWebsite, "New enquiry from the website", "Someone sent the enquiry form. Call them back.", $"/marketing/leads/{lead.Id}", $"lead-web:{lead.Id}", ct);
        await db.SaveChangesAsync(ct);
    }
}
