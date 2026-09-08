using System.Text.RegularExpressions;

namespace StitchHelper;

/// <summary>Only explicit '+' compositions become blends; ambiguous legends stay in review.</summary>
public static class ThreadUsageParser
{
    public static List<ThreadUsageComponent> Parse(string expression, string id, IReadOnlyDictionary<string, ThreadEntry> catalog, string? strands = null)
    {
        var parts = expression.Split('+', StringSplitOptions.TrimEntries);
        var counts = strands?.Split('+', StringSplitOptions.TrimEntries);
        List<ThreadUsageComponent> result = [];
        for (var i = 0; i < parts.Length; i++)
        {
            var match = Regex.Match(parts[i], @"^(?:DMC\s*)?(B5200|White|Blanc|Ecru|\d{1,4})(?:\s*\((\d+)\))?$", RegexOptions.IgnoreCase);
            if (!match.Success) throw new UserError("The thread blend could not be read. Use explicit DMC components and review the original legend.");
            var raw = ThreadCatalog.CanonicalCode(match.Groups[1].Value);
            var code = catalog.Keys.FirstOrDefault(k => k.Equals(raw, StringComparison.OrdinalIgnoreCase)) ?? raw;
            int? count = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : counts?.Length == parts.Length && int.TryParse(counts[i], out var n) ? n : null;
            if (count is <= 0) throw new UserError("A strand count in the source is zero or negative. Review the original legend.");
            result.Add(new(id + "-c" + i, code, count));
        }
        if (result.Select(x => x.ThreadCode).Distinct().Count() != result.Count) throw new UserError("A blend repeats the same thread. Review the component strand counts.");
        return result;
    }
}
