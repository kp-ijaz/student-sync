namespace StudentSync.Application.Common;

/// <summary>CSV cells for exports (attendance report, finance transactions): quoted when needed, formula injection neutralised.</summary>
public static class CsvText
{
    public static string Cell(string? v)
    {
        v ??= "";
        if (v.Length > 0 && "=+-@".Contains(v[0])) v = "'" + v;
        return v.Contains(',') || v.Contains('"') || v.Contains('\n') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }

    public static string Row(params string?[] cells) => string.Join(",", cells.Select(Cell));
}
