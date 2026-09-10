using System.Text.Json;

namespace StitchHelper;

public static class ThreadCatalog
{
    public static string CanonicalCode(string code)
    {
        code = code.Trim();
        if (code.Equals("Blanc", StringComparison.OrdinalIgnoreCase)) return "White";
        if (code.Length > 0 && code.All(char.IsAsciiDigit))
        {
            var unpadded = code.TrimStart('0');
            return unpadded.Length == 0 ? "0" : unpadded;
        }
        return code;
    }

    public static List<ThreadEntry> Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        List<ThreadEntry> entries = [];
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in document.RootElement.EnumerateArray())
        {
            var code = CanonicalCode(row.GetProperty("floss").GetString() ?? throw new InvalidDataException("A catalog row has no floss code."));
            var name = row.GetProperty("description").GetString() ?? throw new InvalidDataException($"DMC {code} has no description.");
            var r = row.GetProperty("r").GetInt32();
            var g = row.GetProperty("g").GetInt32();
            var b = row.GetProperty("b").GetInt32();
            if (r is < 0 or > 255 || g is < 0 or > 255 || b is < 0 or > 255) throw new InvalidDataException($"DMC {code} has an RGB channel outside 0–255.");
            if (!codes.Add(code)) throw new InvalidDataException($"Duplicate DMC code: {code}.");
            // RGB channels are authoritative. The supplied hex column contains inconsistent and spreadsheet-converted values.
            var entry = new ThreadEntry(code, name, $"#{r:X2}{g:X2}{b:X2}");
            Repository.ValidateThread(entry);
            entries.Add(entry);
        }
        if (entries.Count == 0) throw new InvalidDataException("The DMC catalog is empty.");
        return entries;
    }

    public static PatternData Normalize(PatternData data)
    {
        data.Definitions = data.Definitions.Select(Normalize).ToList();
        return data;
    }
    private static StitchDefinition Normalize(StitchDefinition d)
    {
        var components = d.GetComponents().Select(c => c with { ThreadCode = CanonicalCode(c.ThreadCode) }).ToList();
        return d with { ThreadCode = components.FirstOrDefault()?.ThreadCode ?? CanonicalCode(d.ThreadCode), Components = components };
    }

    public static Project Normalize(Project project)
    {
        Normalize(project.Data);
        var substitutions = new Dictionary<string, string>();
        // Keys identify thread components; only replacement values are DMC codes.
        foreach (var (componentId, replacement) in project.Substitutions)
            substitutions[componentId] = CanonicalCode(replacement);
        project.Substitutions = substitutions;
        foreach (var change in project.Undo.Concat(project.Redo))
        {
            if (change.Replacement is not null) change.Replacement = CanonicalCode(change.Replacement);
            if (change.Definition is not null) change.Definition = Normalize(change.Definition);
            if (change.Data is not null) Normalize(change.Data);
        }
        return project;
    }
}
