using Microsoft.EntityFrameworkCore;
using StudentSync.Application.Common;
using StudentSync.Contracts;
using StudentSync.Domain.Identity;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Platform;

public sealed class InstitutionService(IAppDbContext db, ICurrentUser user, AccessPolicy access, AuditService audit, PlatformService platform)
{
    public async Task<InstitutionSettingsDto> SettingsAsync(CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        var t = await db.Tenants.FirstAsync(x => x.Id == user.TenantId, ct);
        return new InstitutionSettingsDto(t.Id, t.Name, t.Slug, t.CountryCode, t.DefaultTimeZoneId, t.CurrencyCode, t.DefaultLanguage, t.ContactEmail, t.BrandColorHex,
            t.RecordingEnabled, t.TranscriptionEnabled, t.AiAssistantEnabled, t.DoubtFollowUpEnabled, t.RecordingRetentionDays, t.StudentsCanMessageTeachers,
            t.ParentsCanMessageTeachers, RowVersions.Encode(t.RowVersion));
    }

    public async Task UpdateSettingsAsync(UpdateInstitutionSettingsRequest r, CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        var t = await db.Tenants.FirstAsync(x => x.Id == user.TenantId, ct);
        RowVersions.Apply(db, t, r.RowVersion);
        if (!ZonedTime.TryFind(r.DefaultTimeZoneId, out _)) throw RequestValidationException.For("defaultTimeZoneId", "Choose a valid time zone.");
        if (r.RecordingRetentionDays is < 7 or > 3650) throw RequestValidationException.For("recordingRetentionDays", "Retention must be between 7 and 3650 days.");
        if (r.BrandColorHex is { Length: > 0 } c && !System.Text.RegularExpressions.Regex.IsMatch(c, "^#[0-9A-Fa-f]{6}$"))
            throw RequestValidationException.For("brandColorHex", "Use a hex colour such as #1264D8.");
        if (r.AiAssistantEnabled && !r.TranscriptionEnabled) throw RequestValidationException.For("aiAssistantEnabled", "The AI Class Assistant needs transcription to be enabled.");
        var before = $"rec={t.RecordingEnabled},tx={t.TranscriptionEnabled},ai={t.AiAssistantEnabled}";
        t.Name = Text.Required(r.Name, "name", 150);
        t.CountryCode = Text.Required(r.CountryCode, "countryCode", 2).ToUpperInvariant();
        t.DefaultTimeZoneId = r.DefaultTimeZoneId;
        t.CurrencyCode = Text.Required(r.CurrencyCode, "currencyCode", 3).ToUpperInvariant();
        t.DefaultLanguage = r.DefaultLanguage is "ar" or "ml" ? r.DefaultLanguage : "en";
        t.ContactEmail = Text.Optional(r.ContactEmail, "contactEmail", 256);
        t.BrandColorHex = Text.Optional(r.BrandColorHex, "brandColorHex", 7);
        t.RecordingEnabled = r.RecordingEnabled;
        t.TranscriptionEnabled = r.TranscriptionEnabled;
        t.AiAssistantEnabled = r.AiAssistantEnabled;
        t.DoubtFollowUpEnabled = r.DoubtFollowUpEnabled;
        t.RecordingRetentionDays = r.RecordingRetentionDays;
        t.StudentsCanMessageTeachers = r.StudentsCanMessageTeachers;
        t.ParentsCanMessageTeachers = r.ParentsCanMessageTeachers;
        audit.Record("institution.settings", "Tenant", t.Id, $"{before} -> rec={t.RecordingEnabled},tx={t.TranscriptionEnabled},ai={t.AiAssistantEnabled}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<HolidayDto>> HolidaysAsync(CancellationToken ct) =>
        await db.Holidays.OrderBy(h => h.Date).Select(h => new HolidayDto(h.Id, h.Date, h.Name)).ToListAsync(ct);

    public async Task<Guid> AddHolidayAsync(SaveHolidayRequest r, CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        if (await db.Holidays.AnyAsync(h => h.Date == r.Date, ct)) throw RequestValidationException.For("date", "A holiday already exists on this date.");
        var h = new Domain.Tenancy.InstitutionHoliday { Date = r.Date, Name = Text.Required(r.Name, "name", 100) };
        db.Holidays.Add(h);
        audit.Record("holiday.added", "InstitutionHoliday", h.Id, $"{r.Date:yyyy-MM-dd} {h.Name}");
        await db.SaveChangesAsync(ct);
        return h.Id;
    }

    public async Task DeleteHolidayAsync(Guid id, CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        var h = await db.Holidays.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Holiday");
        db.Holidays.Remove(h);
        audit.Record("holiday.removed", "InstitutionHoliday", h.Id, h.Name);
        await db.SaveChangesAsync(ct);
    }

    public async Task<SubscriptionDto> SubscriptionAsync(CancellationToken ct)
    {
        access.Require(Permissions.InstitutionSettingsManage);
        var s = await db.Subscriptions.Include(x => x.Plan).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Subscription");
        var name = await db.Tenants.Where(t => t.Id == s.TenantId).Select(t => t.Name).FirstAsync(ct);
        return await platform.ToDtoAsync(s, name, ct);
    }

    public IReadOnlyList<RolePermissionDto> PermissionMatrix() =>
        Enum.GetValues<UserRole>().Select(r => new RolePermissionDto(r.ToString(), RolePermissions.For(r).Order().ToList())).ToList();

    public Task<PagedResult<AuditEntryDto>> AuditAsync(AuditQuery q, CancellationToken ct)
    {
        access.Require(Permissions.InstitutionAuditView);
        return platform.AuditAsync(q, user.TenantId, ct);
    }
}
