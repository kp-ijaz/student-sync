using StudentSync.Domain.Common;

namespace StudentSync.Domain.Scheduling;

public enum RecurrenceFrequency { Daily, Weekly }

[Flags]
public enum WeekDays
{
    None = 0,
    Monday = 1, Tuesday = 2, Wednesday = 4, Thursday = 8, Friday = 16, Saturday = 32, Sunday = 64
}

/// <summary>A local-calendar recurrence pattern. Occurrence dates are wall-clock dates in the series' time zone.</summary>
public sealed record RecurrencePattern(
    RecurrenceFrequency Frequency,
    int Interval,
    WeekDays Days,
    DateOnly StartDate,
    DateOnly? EndDate,
    int? Count)
{
    public const int MaxOccurrences = 400;

    public void Validate()
    {
        if (Interval is < 1 or > 12) throw new DomainException("Recurrence interval must be between 1 and 12.", "invalid_recurrence");
        if (Frequency == RecurrenceFrequency.Weekly && Days == WeekDays.None)
            throw new DomainException("Choose at least one weekday for a weekly schedule.", "invalid_recurrence");
        if (EndDate is null && Count is null)
            throw new DomainException("A recurring schedule needs an end date or a number of occurrences.", "invalid_recurrence");
        if (EndDate < StartDate) throw new DomainException("The end date must be on or after the start date.", "invalid_recurrence");
        if (Count is < 1 or > MaxOccurrences) throw new DomainException($"Occurrences must be between 1 and {MaxOccurrences}.", "invalid_recurrence");
        if (EndDate is { } end && end.DayNumber - StartDate.DayNumber > 3 * 366)
            throw new DomainException("A recurring schedule may span at most three years.", "invalid_recurrence");
    }

    public static WeekDays ToFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => WeekDays.Monday,
        DayOfWeek.Tuesday => WeekDays.Tuesday,
        DayOfWeek.Wednesday => WeekDays.Wednesday,
        DayOfWeek.Thursday => WeekDays.Thursday,
        DayOfWeek.Friday => WeekDays.Friday,
        DayOfWeek.Saturday => WeekDays.Saturday,
        _ => WeekDays.Sunday,
    };

    /// <summary>Enumerates occurrence dates from the start, honouring Count/EndDate, never exceeding <see cref="MaxOccurrences"/>.</summary>
    public IEnumerable<DateOnly> Dates()
    {
        Validate();
        var produced = 0;
        var limit = Math.Min(Count ?? MaxOccurrences, MaxOccurrences);
        var mondayOfStartWeek = StartDate.AddDays(-(((int)StartDate.DayOfWeek + 6) % 7));

        for (var date = StartDate; produced < limit; date = date.AddDays(1))
        {
            if (EndDate is { } end && date > end) yield break;
            if (date.DayNumber - StartDate.DayNumber > 3 * 366 + 7) yield break;

            bool matches = Frequency switch
            {
                RecurrenceFrequency.Daily => (date.DayNumber - StartDate.DayNumber) % Interval == 0,
                _ => (Days & ToFlag(date.DayOfWeek)) != 0
                     && ((date.DayNumber - mondayOfStartWeek.DayNumber) / 7) % Interval == 0,
            };
            if (!matches) continue;
            produced++;
            yield return date;
        }
    }
}
