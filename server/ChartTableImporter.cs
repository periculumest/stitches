using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace StitchHelper;

/// <summary>Charts with Symbol/Strands/Type/Number/Color legends, symbol fonts, and printed global coordinates.</summary>
public static class ChartTableImporter
{
    private record Legend(Letter Letter, string Code, string Strands);
    private record Tile(Page Page, List<Letter> Letters, double[] X, double[] Y, int OffsetX, int OffsetY);
    private record Header(List<Letter> Row, Dictionary<string, double> Boundaries);
    internal static string FontKey(Letter l) => Regex.Replace(l.FontName ?? "", @"^[A-Z]{6}\+", "");
    internal static string SymbolKey(Letter l) => FontKey(l) + "\0" + l.Value;

    public static PatternData? TryParse(IReadOnlyList<Page> pages, IReadOnlyDictionary<string, ThreadEntry> catalog)
    {
        var legend = new Dictionary<string, Legend>();
        var notes = new List<ImportWarning>();
        var legendPages = new HashSet<int>();
        var expected = new Dictionary<string, int>();
        foreach (var page in pages)
        {
            var rows = Rows(page.Letters);
            var header = FindHeader(rows, ["Symbol", "Strands", "Type", "Number", "Color"]);
            if (header is not null)
            {
                legendPages.Add(page.Number);
                foreach (var row in rows.Where(r => r.Max(l => l.StartBaseLine.Y) < header.Row.Min(l => l.StartBaseLine.Y) - 2))
                {
                    if (Column(row, header, "Type") != "DMC") continue;
                    var symbols = ColumnLetters(row, header, "Symbol").Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
                    if (symbols.Count != 1) { notes.Add(new("A legend row has an ambiguous symbol. Check its mapping.", page.Number)); continue; }
                    var rawCode = ThreadCatalog.CanonicalCode(Column(row, header, "Number"));
                    var code = catalog.Keys.FirstOrDefault(k => k.Equals(rawCode, StringComparison.OrdinalIgnoreCase)) ?? rawCode;
                    var key = SymbolKey(symbols[0]);
                    if (legend.TryGetValue(key, out var previous) && previous.Code != code)
                        throw new UserError("This chart assigns multiple threads to the same font symbol. Automatic import cannot safely distinguish them yet.");
                    legend[key] = new(symbols[0], code, Column(row, header, "Strands"));
                }
            }
            var usage = FindHeader(rows, ["Type", "Number", "Full", "Half"]);
            if (usage is not null)
                foreach (var row in rows.Where(r => r.Max(l => l.StartBaseLine.Y) < usage.Row.Min(l => l.StartBaseLine.Y) - 2))
                    if (Column(row, usage, "Type") == "DMC" && int.TryParse(Column(row, usage, "Full"), out var count))
                        expected[ThreadCatalog.CanonicalCode(Column(row, usage, "Number"))] = count;
        }
        if (legend.Count == 0) return null; // keep the simpler grid/inline-key importer for other PDFs
        var fonts = legend.Values.Select(l => FontKey(l.Letter)).ToHashSet();
        var tiles = new List<Tile>();
        foreach (var page in pages.Where(p => !legendPages.Contains(p.Number)))
        {
            var symbols = page.Letters.Where(l => fonts.Contains(FontKey(l)) && !string.IsNullOrWhiteSpace(l.Value)).ToList();
            if (symbols.Count < 25) continue;
            var xs = Cluster(symbols.Select(l => l.StartBaseLine.X));
            var ys = Cluster(symbols.Select(l => l.StartBaseLine.Y)).Reverse().ToArray();
            if (!IsLattice(xs) || !IsLattice(ys.Reverse().ToArray()))
                throw new UserError($"The symbol grid on PDF page {page.Number} is irregular. It needs manual grid calibration, which is not supported yet.");
            var stepX = MedianStep(xs); var stepY = MedianStep(ys.Reverse().ToArray());
            var ordinary = page.Letters.Where(l => !fonts.Contains(FontKey(l)) && l.Value.All(char.IsDigit) && l.Value.Length > 0).ToList();
            var top = ordinary.Where(l => l.StartBaseLine.Y > ys[0] && l.StartBaseLine.Y < ys[0] + stepY * 2.5 && l.StartBaseLine.X >= xs[0] - stepX && l.StartBaseLine.X <= xs[^1] + stepX).ToList();
            var left = ordinary.Where(l => l.StartBaseLine.X < xs[0] && l.StartBaseLine.X > xs[0] - stepX * 2.5 && l.StartBaseLine.Y >= ys[^1] - stepY && l.StartBaseLine.Y <= ys[0]).ToList();
            var offsetX = Offset(Numbers(top, false), xs[0], stepX, false);
            var offsetY = Offset(Numbers(left, true), ys[0], stepY, true);
            if (offsetX is null || offsetY is null)
                throw new UserError($"Could not confidently read global grid coordinates on PDF page {page.Number}. Import stopped rather than guessing page placement.");
            tiles.Add(new(page, symbols, xs, ys, offsetX.Value, offsetY.Value));
        }
        if (tiles.Count == 0) return null;
        var result = new PatternData { Warnings = notes, PageSourcesComplete = true };
        var definitions = new Dictionary<string, StitchDefinition>();
        foreach (var (key, entry) in legend)
        {
            var glyph = ExtractGlyph(entry.Letter);
            var definitionId = $"d{definitions.Count}";
            var composition = ThreadUsageParser.Parse(entry.Code, definitionId, catalog, entry.Strands);
            var definition = new StitchDefinition(definitionId, entry.Letter.Value, composition[0].ThreadCode, "FullCross", glyph, composition);
            definitions[key] = definition;
            if (composition.Any(c => !catalog.ContainsKey(c.ThreadCode))) notes.Add(new($"DMC {entry.Code} is not in the thread catalog. Assign a thread before starting."));
        }
        var occupied = new Dictionary<(int, int), Stitch>();
        var overlapCount = 0;
        foreach (var tile in tiles)
        {
            if (tile.OffsetX < 0 || tile.OffsetY < 0 || tile.OffsetX + tile.X.Length > 5000 || tile.OffsetY + tile.Y.Length > 5000) throw new UserError("Chart coordinates exceed the supported 5,000 × 5,000 grid.");
            var localCells = new HashSet<(int, int)>();
            foreach (var letter in tile.Letters)
            {
                var x = Nearest(tile.X, letter.StartBaseLine.X) + tile.OffsetX;
                var y = Nearest(tile.Y, letter.StartBaseLine.Y) + tile.OffsetY;
                if (!localCells.Add((x, y))) throw new UserError($"PDF page {tile.Page.Number} contains multiple symbols in one cell. Import cannot safely assign them.");
                if (!definitions.TryGetValue(SymbolKey(letter), out var definition))
                {
                    definition = new($"d{definitions.Count}", letter.Value, "UNKNOWN", "FullCross", ExtractGlyph(letter));
                    definitions[SymbolKey(letter)] = definition;
                    notes.Add(new("A chart symbol is missing from the legend. Assign its thread before starting.", tile.Page.Number, x, y));
                }
                if (occupied.TryGetValue((x, y), out var previous))
                {
                    if (previous.DefinitionId != definition.Id) throw new UserError($"Repeated stitches disagree at column {x + 1}, row {y + 1} on PDF page {tile.Page.Number}. The page placement needs review.");
                    result.PageOverlapStitches.Add(new($"p{tile.Page.Number}-{x - tile.OffsetX}-{y - tile.OffsetY}", x, y, definition.Id));
                    overlapCount++; continue;
                }
                occupied[(x, y)] = new($"p{tile.Page.Number}-{x - tile.OffsetX}-{y - tile.OffsetY}", x, y, definition.Id);
            }
            result.Pages.Add(new(tile.Page.Number, tile.OffsetX, tile.OffsetY, tile.X.Length, tile.Y.Length));
        }
        result.Width = result.Pages.Max(p => p.X + p.Width); result.Height = result.Pages.Max(p => p.Y + p.Height);
        result.Stitches = occupied.Values.ToList();
        // A shared font frame preserves relative sizes (a dot must not grow to fill its cell).
        foreach (var group in definitions.Where(d => d.Value.SymbolGlyph is not null).GroupBy(d => d.Key.Split('\0')[0]).ToList())
        {
            var glyphs = group.Select(d => d.Value.SymbolGlyph!).ToList();
            var minX = glyphs.Min(g => g.MinX); var minY = glyphs.Min(g => g.MinY);
            var width = glyphs.Max(g => g.MinX + g.Width) - minX; var height = glyphs.Max(g => g.MinY + g.Height) - minY;
            foreach (var (key, d) in group) definitions[key] = d with { SymbolGlyph = d.SymbolGlyph! with { MinX = minX, MinY = minY, Width = width, Height = height } };
        }
        result.Definitions = definitions.Values.ToList();
        var dimensions = pages.Select(p => Regex.Match(p.Text, @"\b(\d+)\s*w\s*[xX×]\s*(\d+)\s*h\s*Stitches", RegexOptions.IgnoreCase)).FirstOrDefault(m => m.Success);
        if (dimensions is not null && (int.Parse(dimensions.Groups[1].Value) != result.Width || int.Parse(dimensions.Groups[2].Value) != result.Height))
            throw new UserError($"Assembled chart dimensions {result.Width} × {result.Height} disagree with the design size printed in the PDF. Import stopped for review.");
        var codesByDefinition = definitions.Values.ToDictionary(d => d.Id, d => d.GetComponents().Select(c => c.ThreadCode).ToArray());
        var counts = result.Stitches.SelectMany(s => codesByDefinition[s.DefinitionId]).GroupBy(code => code).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (code, count) in expected)
            if (counts.GetValueOrDefault(code) != count) throw new UserError($"DMC {code}: extracted {counts.GetValueOrDefault(code)} stitches, but the PDF usage summary lists {count}. Import stopped to avoid an incomplete chart.");
        notes.Add(new($"Assembled {tiles.Count} chart pages using printed grid coordinates; removed {overlapCount:N0} matching stitches repeated in overlap strips."));
        if (expected.Count > 0) notes.Add(new($"Verified full-stitch counts against the PDF usage summary for {expected.Count} threads."));
        ImportSymbols.AssignMissing(result, requireSourceGlyph: true);
        notes.Add(new("Source symbol shapes are preserved where available. Review the assembled chart and key before starting; this adapter imports full crosses."));
        return result;
    }

    internal static List<List<Letter>> Rows(IEnumerable<Letter> letters)
    {
        List<List<Letter>> rows = [];
        foreach (var l in letters.OrderByDescending(l => l.StartBaseLine.Y))
        {
            if (rows.Count == 0 || Math.Abs(rows[^1][0].StartBaseLine.Y - l.StartBaseLine.Y) > 2) rows.Add([]);
            rows[^1].Add(l);
        }
        foreach (var row in rows) row.Sort((a, b) => a.StartBaseLine.X.CompareTo(b.StartBaseLine.X));
        return rows;
    }
    private static Header? FindHeader(List<List<Letter>> rows, string[] labels)
    {
        foreach (var row in rows)
        {
            var letters = row.Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
            var text = string.Concat(letters.Select(l => l.Value));
            var ranges = new List<(string Label, double Left, double Right)>();
            var start = 0;
            foreach (var label in labels)
            {
                var index = text.IndexOf(label, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) break;
                var position = 0;
                var matches = letters.Where(l => { var from = position; position += l.Value.Length; return from < index + label.Length && position > index; }).ToList();
                ranges.Add((label, matches.Min(l => l.StartBaseLine.X), matches.Max(l => l.EndBaseLine.X)));
                start = index + label.Length;
            }
            if (ranges.Count != labels.Length) continue;
            var bounds = new Dictionary<string, double> { [labels[0]] = ranges[0].Left - 15 };
            for (var i = 1; i < ranges.Count; i++) bounds[labels[i]] = (ranges[i - 1].Right + ranges[i].Left) / 2;
            return new(row, bounds);
        }
        return null;
    }
    private static IEnumerable<Letter> ColumnLetters(List<Letter> row, Header header, string column)
    {
        var left = header.Boundaries[column]; var right = header.Boundaries.Values.Where(x => x > left).DefaultIfEmpty(double.MaxValue).Min();
        return row.Where(l => l.StartBaseLine.X >= left && l.StartBaseLine.X < right);
    }
    private static string Column(List<Letter> row, Header header, string column) => string.Concat(ColumnLetters(row, header, column).Select(l => l.Value)).Trim();
    private static double[] Cluster(IEnumerable<double> values)
    {
        List<List<double>> groups = [];
        foreach (var v in values.Order()) { if (groups.Count == 0 || v - groups[^1][0] > .5) groups.Add([]); groups[^1].Add(v); }
        return groups.Select(g => g.Average()).ToArray();
    }
    private static double MedianStep(double[] axis) => axis.Zip(axis.Skip(1), (a, b) => b - a).Order().ElementAt((axis.Length - 1) / 2);
    private static bool IsLattice(double[] axis) => axis.Length >= 5 && MedianStep(axis) >= 2 && axis.Zip(axis.Skip(1), (a, b) => b - a).All(step => Math.Abs(step - MedianStep(axis)) < .5);
    private static int Nearest(double[] axis, double value) => Enumerable.Range(0, axis.Length).MinBy(i => Math.Abs(axis[i] - value));
    private static List<(int Number, double Position)> Numbers(List<Letter> letters, bool vertical)
    {
        var sorted = letters.OrderBy(l => vertical ? l.StartBaseLine.Y : l.StartBaseLine.X).ToList();
        List<List<Letter>> groups = [];
        foreach (var l in sorted)
        {
            if (groups.Count == 0 || (vertical ? l.StartBaseLine.Y - groups[^1][^1].StartBaseLine.Y : l.StartBaseLine.X - groups[^1][^1].StartBaseLine.X) > l.FontSize * 1.4) groups.Add([]);
            groups[^1].Add(l);
        }
        return groups.Select(g => (Text: string.Concat(g.Select(l => l.Value)), Position: g.Average(l => vertical ? l.StartBaseLine.Y : l.StartBaseLine.X)))
            .Where(g => int.TryParse(g.Text, out _)).Select(g => (int.Parse(g.Text), g.Position)).ToList();
    }
    private static int? Offset(List<(int Number, double Position)> labels, double origin, double step, bool vertical)
    {
        var votes = labels.Select(l => l.Number - (int)Math.Round((vertical ? origin - l.Position : l.Position - origin) / step)).GroupBy(v => v).OrderByDescending(g => g.Count()).ToList();
        return votes.Count > 0 && votes[0].Count() >= 2 && votes[0].Count() >= labels.Count * .6 ? votes[0].Key : null;
    }
    internal static SymbolGlyph? ExtractGlyph(Letter letter)
    {
        var font = letter.GetFont(); if (font is null) return null;
        for (var code = 0; code < 256; code++)
        {
            if (!font.TryGetUnicode(code, out var value) || value != letter.Value || !font.TryGetNormalisedPath(code, out var paths) || paths.Count == 0) continue;
            var bounds = PdfSubpath.GetBoundingRectangle(paths);
            if (bounds is null || bounds.Value.Width <= 0 || bounds.Value.Height <= 0) continue;
            if (paths.SelectMany(p => p.Commands).Any(c => c is not (PdfSubpath.Move or PdfSubpath.Line or PdfSubpath.QuadraticBezierCurve or PdfSubpath.CubicBezierCurve or PdfSubpath.Close))) continue;
            var svg = new StringBuilder();
            static string Point(PdfPoint p) => FormattableString.Invariant($"{p.X:0.######} {-p.Y:0.######}");
            foreach (var command in paths.SelectMany(p => p.Commands))
                svg.Append(command switch
                {
                    PdfSubpath.Move m => $"M {Point(m.Location)} ",
                    PdfSubpath.Line l => $"L {Point(l.To)} ",
                    PdfSubpath.QuadraticBezierCurve q => $"Q {Point(q.ControlPoint)} {Point(q.EndPoint)} ",
                    PdfSubpath.CubicBezierCurve c => $"C {Point(c.FirstControlPoint)} {Point(c.SecondControlPoint)} {Point(c.EndPoint)} ",
                    PdfSubpath.Close => "Z ",
                    _ => throw new UserError("This symbol font contains an unsupported outline command.")
                });
            return new(svg.ToString(), bounds.Value.Left, -bounds.Value.Top, bounds.Value.Width, bounds.Value.Height);
        }
        return null;
    }
}
