using System.Globalization;

namespace StudentSync.Application.Automation;

/// <summary>Plain text for notifications and SMS. SMS text avoids symbols like ₹ that force costlier Unicode messages.</summary>
public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Money(decimal amount, string currency, bool sms = false)
    {
        var n = amount == decimal.Truncate(amount) ? amount.ToString("#,0", Inv) : amount.ToString("#,0.00", Inv);
        if (sms) return $"{(currency == "INR" ? "Rs." : currency)} {n}";
        return currency switch { "INR" => $"₹{n}", "USD" => $"${n}", "EUR" => $"€{n}", "GBP" => $"£{n}", _ => $"{currency} {n}" };
    }

    public static string Hours(decimal h) => $"{h.ToString("0.##", Inv)} h";
    public static string Day(DateOnly d) => d.ToString("d MMM", Inv);
    public static string Month(DateOnly d) => d.ToString("MMMM yyyy", Inv);
    public static string Key(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);
    public static string MonthKey(DateOnly d) => d.ToString("yyyy-MM", Inv);
    public static string Time(DateTime local) => local.ToString("HH:mm", Inv);
    public static string FirstName(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;
}
