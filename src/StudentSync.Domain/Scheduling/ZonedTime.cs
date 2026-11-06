namespace StudentSync.Domain.Scheduling;

public enum LocalTimeResolution
{
    /// <summary>The local time maps to exactly one instant.</summary>
    Exact,
    /// <summary>The local time occurred twice (clocks went back); the earlier instant was chosen.</summary>
    AmbiguousEarlierChosen,
    /// <summary>The local time did not exist (clocks went forward); it was moved forward by the length of the gap.</summary>
    SkippedShiftedForward
}

public readonly record struct ZonedInstant(DateTime Utc, LocalTimeResolution Resolution);

/// <summary>
/// Converts between local wall-clock time in an IANA time zone and UTC instants. All recurring schedules
/// are expanded in local time and converted per occurrence, so DST changes never shift a lesson's wall-clock time.
/// </summary>
public static class ZonedTime
{
    public static TimeZoneInfo Find(string timeZoneId)
    {
        if (!TryFind(timeZoneId, out var tz))
            throw new Common.DomainException($"Unknown time zone '{timeZoneId}'. Use an IANA identifier such as 'Asia/Dubai'.", "invalid_time_zone");
        return tz;
    }

    public static bool TryFind(string? timeZoneId, out TimeZoneInfo tz)
    {
        tz = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(timeZoneId)) return false;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            // Accept IANA ids only, so stored identifiers are portable across operating systems.
            return tz.HasIanaId || timeZoneId == "UTC";
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }

    public static ZonedInstant ToUtc(DateOnly date, TimeOnly time, string timeZoneId)
        => ToUtc(date.ToDateTime(time, DateTimeKind.Unspecified), timeZoneId);

    public static ZonedInstant ToUtc(DateTime localWallClock, string timeZoneId)
    {
        var tz = Find(timeZoneId);
        var local = DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified);

        if (tz.IsInvalidTime(local))
        {
            // Interpret using the offset in force before the transition; this moves the time forward by the gap.
            var offsetBefore = tz.GetUtcOffset(local.AddHours(-6));
            return new ZonedInstant(DateTime.SpecifyKind(local - offsetBefore, DateTimeKind.Utc), LocalTimeResolution.SkippedShiftedForward);
        }

        if (tz.IsAmbiguousTime(local))
        {
            var largest = tz.GetAmbiguousTimeOffsets(local).Max();
            return new ZonedInstant(DateTime.SpecifyKind(local - largest, DateTimeKind.Utc), LocalTimeResolution.AmbiguousEarlierChosen);
        }

        return new ZonedInstant(TimeZoneInfo.ConvertTimeToUtc(local, tz), LocalTimeResolution.Exact);
    }

    public static DateTime ToLocal(DateTime utc, string timeZoneId)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Find(timeZoneId));

    public static TimeSpan OffsetAt(DateTime utc, string timeZoneId)
        => Find(timeZoneId).GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
}
