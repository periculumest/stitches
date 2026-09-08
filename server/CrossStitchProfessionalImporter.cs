using System.Text.RegularExpressions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace StitchHelper;

/// <summary>Cross Stitch Professional symbol charts with repeated Sym/No./Colour Name legend columns.</summary>
public static class CrossStitchProfessionalImporter
{
    private record LegendColumn(double Left, double CodeLeft, double CodeRight, double Top);
    private record Grid(double[] X, double[] Y)
    {
        public int Width => X.Length - 1;
        public int Height => Y.Length - 1;
        public double StepX => (X[^1] - X[0]) / Width;
        public double StepY => (Y[^1] - Y[0]) / Height;
    }

    public static PatternData? TryParse(IReadOnlyList<Page> pages, IReadOnlyDictionary<string, ThreadEntry> catalog)
    {
        var keyPages = pages.Where(p => p.Text.Contains("Cross Stitch Professional", StringComparison.OrdinalIgnoreCase)).ToList();
        if (keyPages.Count == 0) return null;
        var metadata = string.Join("\n", keyPages.SelectMany(p => ChartTableImporter.Rows(p.Letters)).Select(row => string.Concat(row.Select(l => l.Value))));
        var size = Regex.Match(metadata, @"Stitches:\s*(\d+)\s*[x×]\s*(\d+)", RegexOptions.IgnoreCase);
        if (!size.Success) throw new UserError("The Cross Stitch Professional key is missing its design dimensions. Import cannot safely place the chart pages.");
        if (!int.TryParse(size.Groups[1].Value, out var width) || !int.TryParse(size.Groups[2].Value, out var height) || width is < 1 or > 5000 || height is < 1 or > 5000)
            throw new UserError("Chart dimensions exceed the supported 5,000 × 5,000 grid.");
        if (!Regex.IsMatch(metadata, @"Colou?rs:\s*DMC\b", RegexOptions.IgnoreCase))
            throw new UserError("This Cross Stitch Professional chart does not identify a DMC thread palette.");
        var strandMatch = Regex.Match(metadata, @"Use\s+(\d+)\s+strands?\s+of\s+thread\s+for\s+cross\s+stitch", RegexOptions.IgnoreCase);
        int? strands = strandMatch.Success && int.TryParse(strandMatch.Groups[1].Value, out var count) && count > 0 ? count : null;
        var result = new PatternData { Width = width, Height = height, PageSourcesComplete = true };
        var definitions = new Dictionary<string, StitchDefinition>();
        var legendPages = new HashSet<int>();
        foreach (var page in pages)
        {
            var columns = LegendColumns(page);
            if (columns.Count == 0) continue;
            legendPages.Add(page.Number);
            foreach (var column in columns)
            foreach (var row in ChartTableImporter.Rows(page.Letters).Where(r => r.Max(l => l.StartBaseLine.Y) < column.Top - 3))
            {
                var codeText = string.Concat(row.Where(l => l.StartBaseLine.X >= column.CodeLeft && l.StartBaseLine.X < column.CodeRight).Select(l => l.Value)).Trim();
                if (!Regex.IsMatch(codeText, @"^(?:\d{1,4}|B5200|White|Blanc|Ecru)$", RegexOptions.IgnoreCase)) continue;
                var symbols = row.Where(l => l.StartBaseLine.X >= column.Left && l.StartBaseLine.X < column.CodeLeft && !string.IsNullOrWhiteSpace(l.Value)).ToList();
                if (symbols.Count != 1) throw new UserError($"The symbol for DMC {codeText} on PDF page {page.Number} is ambiguous. Import stopped for review.");
                var letter = symbols[0];
                var code = ThreadCatalog.CanonicalCode(codeText);
                var key = ChartTableImporter.SymbolKey(letter);
                if (definitions.TryGetValue(key, out var previous))
                {
                    if (previous.ThreadCode != code) throw new UserError("This chart assigns different DMC threads to the same font symbol. Check its legend.");
                    continue;
                }
                var id = $"d{definitions.Count}";
                var glyph = ChartTableImporter.ExtractGlyph(letter);
                definitions[key] = new(id, letter.Value, code, "FullCross", glyph, [new(id + "-c0", code, strands)]);
                if (!catalog.ContainsKey(code)) result.Warnings.Add(new($"DMC {code} is not in the thread catalog. Assign a thread before starting.", page.Number));
                if (glyph is null) result.Warnings.Add(new($"The source symbol shape for DMC {code} could not be preserved. Check the text symbol against the PDF.", page.Number));
            }
        }
        if (definitions.Count == 0) throw new UserError("No readable Sym/No./Colour Name legend was found in this Cross Stitch Professional chart.");
        NormalizeGlyphFrames(definitions);
        var fonts = definitions.Keys.Select(k => k.Split('\0')[0]).ToHashSet();
        // A malformed row must not silently drop one of the key's custom-font symbols.
        foreach (var page in pages.Where(p => legendPages.Contains(p.Number)))
            if (page.Letters.Any(l => fonts.Contains(ChartTableImporter.FontKey(l)) && !string.IsNullOrWhiteSpace(l.Value) && !definitions.ContainsKey(ChartTableImporter.SymbolKey(l))))
                throw new UserError($"A symbol on key page {page.Number} has no readable DMC code. Import stopped for review.");
        var occupied = new Dictionary<(int X, int Y), Stitch>();
        var overlaps = 0;
        foreach (var page in pages.Where(p => !legendPages.Contains(p.Number)))
        {
            var symbols = page.Letters.Where(l => fonts.Contains(ChartTableImporter.FontKey(l)) && !string.IsNullOrWhiteSpace(l.Value)).ToList();
            var grid = FindGrid(page);
            if (grid is null)
            {
                if (symbols.Count > 0) throw new UserError($"PDF page {page.Number} has chart symbols but no complete regular grid. Import stopped to avoid missing stitches.");
                continue;
            }
            var ordinary = page.Letters.Where(l => !fonts.Contains(ChartTableImporter.FontKey(l)) && l.Value.Length > 0 && l.Value.All(char.IsDigit)).ToList();
            var top = ordinary.Where(l => l.StartBaseLine.Y > grid.Y[^1] && l.StartBaseLine.Y < grid.Y[^1] + grid.StepY * 2 && l.StartBaseLine.X >= grid.X[0] && l.StartBaseLine.X <= grid.X[^1]).ToList();
            var left = ordinary.Where(l => l.GlyphRectangle.Right < grid.X[0] && l.StartBaseLine.X > grid.X[0] - grid.StepX * 3 && l.StartBaseLine.Y >= grid.Y[0] && l.StartBaseLine.Y < grid.Y[^1]).ToList();
            var offsetX = Offset(top, grid.X[0] + grid.StepX / 2, grid.StepX, false, page.Number);
            var offsetY = Offset(left, grid.Y[^1] - grid.StepY / 2, grid.StepY, true, page.Number);
            if (offsetX < 0 || offsetY < 0 || offsetX + grid.Width > width || offsetY + grid.Height > height)
                throw new UserError($"Grid coordinates on PDF page {page.Number} fall outside the printed {width} × {height} design.");
            var local = new HashSet<(int, int)>();
            foreach (var letter in symbols)
            {
                // Glyph baselines stay inside their cells, even when different symbols have different bearings.
                var x = (int)Math.Floor((letter.StartBaseLine.X - grid.X[0]) / grid.StepX);
                var y = (int)Math.Floor((grid.Y[^1] - letter.StartBaseLine.Y) / grid.StepY);
                if (x < 0 || y < 0 || x >= grid.Width || y >= grid.Height) throw new UserError($"A symbol falls outside the grid on PDF page {page.Number}.");
                if (!local.Add((x, y))) throw new UserError($"PDF page {page.Number} contains multiple symbols in one cell. Import cannot safely assign them.");
                if (!definitions.TryGetValue(ChartTableImporter.SymbolKey(letter), out var definition)) throw new UserError($"A chart symbol on PDF page {page.Number} is missing from the key. Import stopped for review.");
                var position = (x + offsetX, y + offsetY);
                if (occupied.TryGetValue(position, out var previous))
                {
                    if (previous.DefinitionId != definition.Id) throw new UserError($"Repeated stitches disagree on PDF page {page.Number}. Check the printed page coordinates.");
                    result.PageOverlapStitches.Add(new($"p{page.Number}-{x}-{y}", position.Item1, position.Item2, definition.Id));
                    overlaps++; continue;
                }
                occupied[position] = new($"p{page.Number}-{x}-{y}", position.Item1, position.Item2, definition.Id);
            }
            result.Pages.Add(new(page.Number, offsetX, offsetY, grid.Width, grid.Height));
        }
        // Check page footprints, not stitch counts: intentionally empty fabric is allowed.
        for (var y = 0; y < height; y++)
        {
            var end = 0;
            foreach (var tile in result.Pages.Where(p => p.Y <= y && p.Y + p.Height > y).OrderBy(p => p.X))
            {
                if (tile.X > end) break;
                end = Math.Max(end, tile.X + tile.Width);
            }
            if (end != width) throw new UserError("The chart pages do not cover the printed design dimensions. A page may be missing or have unreadable coordinates.");
        }
        result.Definitions = definitions.Values.ToList(); result.Stitches = occupied.Values.ToList();
        result.Warnings.Add(new($"Assembled {result.Pages.Count} chart pages using one-based printed coordinates and verified coverage of the {width} × {height} design; removed {overlaps:N0} matching overlap stitches."));
        result.Warnings.Add(new($"Read {definitions.Count} DMC symbols from the column-based key and preserved their source shapes. Review the chart before starting; this adapter imports full crosses."));
        if (strands is null) result.Warnings.Add(new("No explicit cross-stitch strand count was found; strand counts remain unspecified."));
        return result;
    }

    private static List<LegendColumn> LegendColumns(Page page)
    {
        var result = new List<LegendColumn>();
        foreach (var row in ChartTableImporter.Rows(page.Letters))
        {
            var chars = row.Where(l => !string.IsNullOrWhiteSpace(l.Value)).SelectMany(l => l.Value.Select(c => (Character: c, Letter: l))).ToList();
            var text = string.Concat(chars.Select(c => c.Character));
            foreach (Match match in Regex.Matches(text, @"SymNo\.Colou?rName", RegexOptions.IgnoreCase))
            {
                var symbolLeft = chars[match.Index].Letter.StartBaseLine.X;
                var symbolRight = chars[match.Index + 2].Letter.EndBaseLine.X;
                var numberLeft = chars[match.Index + 3].Letter.StartBaseLine.X;
                var numberRight = chars[match.Index + 5].Letter.EndBaseLine.X;
                var nameLeft = chars[match.Index + 6].Letter.StartBaseLine.X;
                result.Add(new(symbolLeft - 15, (symbolRight + numberLeft) / 2, (numberRight + nameLeft) / 2, row.Min(l => l.StartBaseLine.Y)));
            }
        }
        return result;
    }

    private static Grid? FindGrid(Page page)
    {
        var lines = page.Paths.SelectMany(p => p).SelectMany(p => p.Commands).OfType<PdfSubpath.Line>().ToList();
        var vertical = lines.Where(l => Math.Abs(l.From.X - l.To.X) < .2 && Math.Abs(l.From.Y - l.To.Y) > 20).ToList();
        var horizontal = lines.Where(l => Math.Abs(l.From.Y - l.To.Y) < .2 && Math.Abs(l.From.X - l.To.X) > 20).ToList();
        var x = PdfImporter.RegularAxis(vertical.Select(l => l.From.X));
        var y = PdfImporter.RegularAxis(horizontal.Select(l => l.From.Y));
        if (x.Length < 6 || y.Length < 6) return null;
        x = PdfImporter.RegularAxis(vertical.Where(l => Math.Min(l.From.Y, l.To.Y) <= y[0] + .5 && Math.Max(l.From.Y, l.To.Y) >= y[^1] - .5).Select(l => l.From.X));
        if (x.Length < 6) return null;
        y = PdfImporter.RegularAxis(horizontal.Where(l => Math.Min(l.From.X, l.To.X) <= x[0] + .5 && Math.Max(l.From.X, l.To.X) >= x[^1] - .5).Select(l => l.From.Y));
        return y.Length >= 6 ? new(x, y) : null;
    }

    private static int Offset(List<Letter> letters, double origin, double step, bool vertical, int page)
    {
        var votes = new List<int>();
        foreach (var row in ChartTableImporter.Rows(letters))
        {
            var groups = new List<List<Letter>>();
            foreach (var letter in row)
            {
                if (groups.Count == 0 || letter.StartBaseLine.X - groups[^1][^1].EndBaseLine.X > letter.FontSize) groups.Add([]);
                groups[^1].Add(letter);
            }
            foreach (var group in groups)
            {
                if (!int.TryParse(string.Concat(group.Select(l => l.Value)), out var number) || number < 1) continue;
                var center = vertical ? (group.Max(l => l.GlyphRectangle.Top) + group.Min(l => l.GlyphRectangle.Bottom)) / 2 : (group.Min(l => l.GlyphRectangle.Left) + group.Max(l => l.GlyphRectangle.Right)) / 2;
                var local = (vertical ? origin - center : center - origin) / step;
                if (Math.Abs(local - Math.Round(local)) > .4) continue;
                votes.Add(number - 1 - (int)Math.Round(local));
            }
        }
        if (votes.Count < 2 || votes.Distinct().Count() != 1) throw new UserError($"Could not confidently read {(vertical ? "row" : "column")} coordinates on PDF page {page}. Import stopped rather than guessing page placement.");
        return votes[0];
    }

    private static void NormalizeGlyphFrames(Dictionary<string, StitchDefinition> definitions)
    {
        foreach (var group in definitions.Where(d => d.Value.SymbolGlyph is not null).GroupBy(d => d.Key.Split('\0')[0]).ToList())
        {
            var glyphs = group.Select(d => d.Value.SymbolGlyph!).ToList();
            var minX = glyphs.Min(g => g.MinX); var minY = glyphs.Min(g => g.MinY);
            var width = glyphs.Max(g => g.MinX + g.Width) - minX; var height = glyphs.Max(g => g.MinY + g.Height) - minY;
            foreach (var (key, definition) in group) definitions[key] = definition with { SymbolGlyph = definition.SymbolGlyph! with { MinX = minX, MinY = minY, Width = width, Height = height } };
        }
    }
}
