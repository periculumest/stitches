using System.Globalization;

namespace StitchHelper;

public static class ImportSymbols
{
    // Allocate after reading the whole key so replacements cannot take a later symbol.
    public static void AssignMissing(PatternData data, bool requireSourceGlyph = false)
    {
        var used = data.Definitions.Select(d => d.Symbol).ToHashSet(StringComparer.Ordinal);
        using var candidates = Candidates().GetEnumerator();
        for (var i = 0; i < data.Definitions.Count; i++)
        {
            var definition = data.Definitions[i];
            if (definition.SymbolGlyph is not null || (!requireSourceGlyph && IsReadable(definition.Symbol))) continue;
            string replacement;
            do
            {
                if (!candidates.MoveNext()) throw new UserError("This chart exceeds the available distinct symbols.");
                replacement = candidates.Current;
            } while (!used.Add(replacement));
            data.Definitions[i] = definition with { Symbol = replacement };
            data.Warnings.Add(new($"Assigned unused symbol {replacement} to DMC {string.Join(" + ", definition.GetComponents().Select(c => c.ThreadCode))} because its source symbol could not be displayed."));
        }
    }

    private static bool IsReadable(string symbol) => !string.IsNullOrWhiteSpace(symbol) && symbol.Length <= 4
        && !symbol.Contains('?') && !symbol.Contains('\uFFFD')
        && !symbol.Any(c => char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned or UnicodeCategory.Surrogate);

    private static IEnumerable<string> Candidates()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        foreach (var c in alphabet + "+/=<>@#%&*") yield return c.ToString();
        // Short ASCII labels remain renderable even when a PDF's custom font is unavailable.
        for (var length = 2; length <= 4; length++)
        for (var number = 0; number < (int)Math.Pow(alphabet.Length, length); number++)
        {
            var value = number;
            var label = new char[length];
            for (var index = length - 1; index >= 0; index--) { label[index] = alphabet[value % alphabet.Length]; value /= alphabet.Length; }
            yield return new string(label);
        }
    }
}
