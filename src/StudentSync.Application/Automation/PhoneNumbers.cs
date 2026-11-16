using System.Text.RegularExpressions;

namespace StudentSync.Application.Automation;

/// <summary>
/// Turns the phone numbers typed at admission ("98765 43210", "050 123 4567", "+91-98765-43210", "0091…") into the
/// international form SMS providers need (+919876543210). Local numbers get the institute's country code (India or UAE);
/// anything that cannot be read safely returns null and the message is skipped rather than sent to a wrong number.
/// </summary>
public static partial class PhoneNumbers
{
    public static string? ToE164(string? raw, string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = Separators().Replace(raw.Trim(), "");
        if (s.StartsWith("00", StringComparison.Ordinal)) s = "+" + s[2..];
        if (!s.StartsWith('+'))
        {
            var country = (countryCode ?? "").Trim().ToUpperInvariant();
            s = country switch
            {
                "IN" when s.Length == 11 && s[0] == '0' => "+91" + s[1..],
                "IN" when s.Length == 10 && s[0] is >= '6' and <= '9' => "+91" + s,
                "IN" when s.Length == 12 && s.StartsWith("91", StringComparison.Ordinal) => "+" + s,
                "AE" when s.Length == 10 && s.StartsWith("05", StringComparison.Ordinal) => "+971" + s[1..],
                "AE" when s.Length == 9 && s[0] == '5' => "+971" + s,
                "AE" when s.Length == 12 && s.StartsWith("971", StringComparison.Ordinal) => "+" + s,
                _ => "",
            };
        }
        return E164().IsMatch(s) ? s : null;
    }

    /// <summary>"+919876543210" → "+91•••••43210": enough to recognise, not enough to misuse.</summary>
    public static string Mask(string e164) => e164.Length <= 7 ? "•••" : e164[..3] + new string('•', e164.Length - 8) + e164[^5..];

    [GeneratedRegex(@"[\s\-\.\(\)]")] private static partial Regex Separators();
    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$")] private static partial Regex E164();
}
