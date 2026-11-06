using System.Globalization;
using System.Text.RegularExpressions;

namespace StudentSync.Application.AI;

public sealed record ParsedSegment(double Start, double End, string? Speaker, string Text);

/// <summary>Parses WebVTT, SRT or plain-text transcripts into timestamped segments.</summary>
public static partial class TranscriptParser
{
    [GeneratedRegex(@"(?<s>(\d+:)?\d{1,2}:\d{2}[\.,]\d{1,3})\s*-->\s*(?<e>(\d+:)?\d{1,2}:\d{2}[\.,]\d{1,3})")]
    private static partial Regex CueTiming();

    [GeneratedRegex(@"^<v\s+(?<sp>[^>]+)>(?<t>.*)$")]
    private static partial Regex VttVoice();

    [GeneratedRegex(@"^(?<sp>[A-Za-z][\w .'-]{0,40}):\s+(?<t>.+)$")]
    private static partial Regex SpeakerPrefix();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    public static List<ParsedSegment> Parse(string content)
    {
        var text = content.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('﻿');
        var lines = text.Split('\n');
        var segments = new List<ParsedSegment>();
        if (!lines.Any(l => CueTiming().IsMatch(l))) return ParsePlain(lines);

        for (var i = 0; i < lines.Length; i++)
        {
            var m = CueTiming().Match(lines[i]);
            if (!m.Success) continue;
            var start = ParseTime(m.Groups["s"].Value);
            var end = ParseTime(m.Groups["e"].Value);
            var body = new List<string>();
            for (i++; i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]); i++) body.Add(lines[i].Trim());
            var joined = string.Join(' ', body);
            string? speaker = null;
            var voice = VttVoice().Match(joined);
            if (voice.Success) { speaker = voice.Groups["sp"].Value.Trim(); joined = voice.Groups["t"].Value; }
            else if (SpeakerPrefix().Match(joined) is { Success: true } sp) { speaker = sp.Groups["sp"].Value.Trim(); joined = sp.Groups["t"].Value; }
            joined = Tags().Replace(joined, "").Trim();
            if (joined.Length > 0) segments.Add(new ParsedSegment(start, Math.Max(end, start), speaker, joined));
        }
        return segments;
    }

    private static List<ParsedSegment> ParsePlain(string[] lines)
    {
        // Plain text has no timing; keep paragraph order with sequential pseudo-positions of zero.
        var segments = new List<ParsedSegment>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var sp = SpeakerPrefix().Match(line);
            segments.Add(sp.Success ? new ParsedSegment(0, 0, sp.Groups["sp"].Value.Trim(), sp.Groups["t"].Value.Trim()) : new ParsedSegment(0, 0, null, line));
        }
        return segments;
    }

    private static double ParseTime(string value)
    {
        var parts = value.Replace(',', '.').Split(':');
        double seconds = double.Parse(parts[^1], CultureInfo.InvariantCulture);
        if (parts.Length >= 2) seconds += int.Parse(parts[^2], CultureInfo.InvariantCulture) * 60;
        if (parts.Length == 3) seconds += int.Parse(parts[0], CultureInfo.InvariantCulture) * 3600;
        return seconds;
    }
}
