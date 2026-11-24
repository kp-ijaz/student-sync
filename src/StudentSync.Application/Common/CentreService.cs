using Microsoft.EntityFrameworkCore;
using StudentSync.Domain.Scheduling;

namespace StudentSync.Application.Common;

/// <summary>The centre's money and clock settings: what amounts are in, and the time zone its days are counted in.</summary>
public sealed record CentreSettings(string Currency, string TimeZoneId);

/// <summary>
/// One place that reads the current institution's currency and time zone (it used to be copied into every fees, pay,
/// finance and marketing service). The answer is kept for the rest of the request, so a page that touches several
/// services asks the database once. A missing institution falls back to USD / UTC rather than failing a money screen.
/// </summary>
public sealed class CentreService(IAppDbContext db, ITenantContext tenant, IClock clock)
{
    private (Guid? TenantId, CentreSettings Settings)? _cached;

    public async Task<CentreSettings> GetAsync(CancellationToken ct)
    {
        var id = tenant.TenantId;
        if (_cached is { } c && c.TenantId == id) return c.Settings;
        var t = await db.Tenants.Where(x => x.Id == id).Select(x => new { x.CurrencyCode, x.DefaultTimeZoneId }).FirstOrDefaultAsync(ct);
        var settings = new CentreSettings(t?.CurrencyCode ?? "USD", t?.DefaultTimeZoneId ?? "UTC");
        _cached = (id, settings);
        return settings;
    }

    /// <summary>Today's date where the centre is (not the server's or the user's day).</summary>
    public async Task<DateOnly> TodayAsync(CancellationToken ct) =>
        DateOnly.FromDateTime(ZonedTime.ToLocal(clock.UtcNow, (await GetAsync(ct)).TimeZoneId));
}
