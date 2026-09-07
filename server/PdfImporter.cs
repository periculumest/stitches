using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace StitchHelper;

public interface IPatternImporter { PatternData Parse(string path, IReadOnlyDictionary<string, ThreadEntry> catalog); }

/// <summary>Conservative structured-PDF adapter. Every result requires human review.</summary>
public class PdfImporter : IPatternImporter
{
    private record Grid(Page Page, double[] X, double[] Y);
    public PatternData Parse(string path, IReadOnlyDictionary<string, ThreadEntry> catalog)
    {
        var result = new PatternData();
        using var document = PdfDocument.Open(path);
        if (document.NumberOfPages > 150) throw new UserError("Please split PDFs longer than 150 pages before importing.");
        var pages = document.GetPages().ToList();
        var chartTable = ChartTableImporter.TryParse(pages, catalog);
        if (chartTable is not null) return chartTable;
        var grids = new List<Grid>();
        foreach (var page in pages)
        {
            var lines = page.Paths.SelectMany(p => p).SelectMany(p => p.Commands).OfType<PdfSubpath.Line>().ToList();
            var vertical = lines.Where(l => Math.Abs(l.From.X - l.To.X) < .2 && Math.Abs(l.From.Y - l.To.Y) > 20).ToList();
            var horizontal = lines.Where(l => Math.Abs(l.From.Y - l.To.Y) < .2 && Math.Abs(l.From.X - l.To.X) > 20).ToList();
            var xs = RegularAxis(vertical.Select(l => l.From.X));
            var ys = RegularAxis(horizontal.Select(l => l.From.Y));
            if (xs.Length >= 6 && ys.Length >= 6)
            {
                // Require crossing lines; a table elsewhere on the page must not extend the chart.
                xs = RegularAxis(vertical.Where(l => Math.Min(l.From.Y, l.To.Y) <= ys[0] + 1 && Math.Max(l.From.Y, l.To.Y) >= ys[^1] - 1).Select(l => l.From.X));
                if (xs.Length >= 6) grids.Add(new(page, xs, ys));
            }
        }
        var mappings = new Dictionary<string, string>();
        foreach (var page in pages)
        {
            var grid = grids.FirstOrDefault(g => g.Page.Number == page.Number);
            var letters = page.Letters.Where(l => grid is null || !Inside(l, grid)).ToList();
            foreach (var row in letters.GroupBy(l => Math.Round(l.StartBaseLine.Y / 3)))
            {
                var ordered = row.OrderBy(l => l.StartBaseLine.X).ToList();
                // Reconstruct spaces from geometry so a symbol cannot swallow the thread code.
                var text = ""; double right = -100;
                foreach (var l in ordered) { if (l.StartBaseLine.X - right > Math.Max(1.5, l.FontSize * .25)) text += " "; text += l.Value; right = l.EndBaseLine.X; }
                var match = Regex.Match(text, @"^\s*(\S{1,2})\s+(?:DMC\s+)?(B5200|White|Blanc|Ecru|\d{1,4})\b", RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                var symbol = match.Groups[1].Value;
                var raw = ThreadCatalog.CanonicalCode(match.Groups[2].Value);
                var code = catalog.Keys.FirstOrDefault(k => k.Equals(raw, StringComparison.OrdinalIgnoreCase)) ?? raw;
                if (mappings.TryGetValue(symbol, out var existing) && existing != code) result.Warnings.Add(new($"Symbol {symbol} has more than one thread in the key. Check its mapping.", page.Number));
                else mappings[symbol] = code;
            }
        }
        var offsetY = 0;
        foreach (var grid in grids)
        {
            var width = grid.X.Length - 1; var height = grid.Y.Length - 1;
            if (width > 1000 || height > 1000 || offsetY + height > 5000) throw new UserError("Detected grid exceeds the import limits. Split this pattern into smaller PDFs.");
            var occupied = new HashSet<(int, int)>();
            foreach (var letter in grid.Page.Letters.Where(l => Inside(l, grid) && !string.IsNullOrWhiteSpace(l.Value)))
            {
                var cx = (letter.GlyphRectangle.Left + letter.GlyphRectangle.Right) / 2;
                var cy = (letter.GlyphRectangle.Top + letter.GlyphRectangle.Bottom) / 2;
                var x = Cell(grid.X, cx); var y = height - 1 - Cell(grid.Y, cy);
                if (x < 0 || x >= width || y < 0 || y >= height) continue;
                if (!occupied.Add((x, y))) { result.Warnings.Add(new("Multiple glyphs occupy one cell. Check this stitch against the PDF.", grid.Page.Number, x, y + offsetY)); continue; }
                var symbol = letter.Value.Length <= 4 && !letter.Value.Any(char.IsControl) ? letter.Value : "?";
                var definition = result.Definitions.FirstOrDefault(d => d.Symbol == symbol);
                if (definition is null)
                {
                    definition = new($"d{result.Definitions.Count}", symbol, mappings.GetValueOrDefault(symbol, "UNKNOWN"));
                    result.Definitions.Add(definition);
                    if (!catalog.ContainsKey(definition.ThreadCode)) result.Warnings.Add(new($"Assign a DMC thread to symbol {symbol} (detected: {definition.ThreadCode}).", grid.Page.Number, x, y + offsetY));
                }
                result.Stitches.Add(new($"p{grid.Page.Number}-{x}-{y}", x, y + offsetY, definition.Id));
            }
            result.Pages.Add(new(grid.Page.Number, 0, offsetY, width, height));
            offsetY += height;
            result.Width = Math.Max(result.Width, width);
        }
        result.Height = offsetY;
        if (grids.Count == 0) result.Warnings.Add(new("No regular vector grid was found. Image-only scans and charts made from outlines are not supported yet. Your original PDF is retained."));
        if (grids.Count > 1) result.Warnings.Add(new("Pages are provisionally stacked top to bottom. Set their X/Y offsets to match the source, and remove any repeated overlap cells before starting."));
        var omitted = pages.Where(p => grids.All(g => g.Page.Number != p.Number)).Select(p => p.Number).ToArray();
        if (omitted.Length > 0) result.Warnings.Add(new($"No chart extracted from page(s) {string.Join(", ", omitted)}. Check these pages for additional charts or specialty stitches."));
        result.Warnings.Add(new("Review the entire chart and key. Automatic extraction reads text symbols as full crosses; fractional stitches, drawn symbols, backstitch, knots, and beads need manual checking."));
        foreach (var entry in mappings.Where(m => result.Definitions.All(d => d.Symbol != m.Key))) result.Warnings.Add(new($"Key symbol {entry.Key} was not found in the chart."));
        return result;
    }
    private static bool Inside(Letter l, Grid g)
    {
        var x = (l.GlyphRectangle.Left + l.GlyphRectangle.Right) / 2; var y = (l.GlyphRectangle.Top + l.GlyphRectangle.Bottom) / 2;
        return x > g.X[0] + .2 && x < g.X[^1] - .2 && y > g.Y[0] + .2 && y < g.Y[^1] - .2;
    }
    private static int Cell(double[] axis, double coordinate)
    {
        var index = Array.BinarySearch(axis, coordinate); return index >= 0 ? Math.Min(index, axis.Length - 2) : ~index - 1;
    }
    private static double[] RegularAxis(IEnumerable<double> coordinates)
    {
        var values = coordinates.Select(v => Math.Round(v, 1)).Distinct().Order().ToArray();
        if (values.Length < 6) return [];
        double[] best = [];
        for (var start = 0; start < values.Length - 5; start++)
        {
            var step = values[start + 1] - values[start];
            if (step < 2 || step > 40) continue;
            var end = start + 1;
            while (end + 1 < values.Length && Math.Abs(values[end + 1] - values[end] - step) < .4) end++;
            if (end - start + 1 > best.Length) best = values[start..(end + 1)];
        }
        return best;
    }
}
