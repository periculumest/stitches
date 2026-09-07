using StitchHelper;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using Xunit;

namespace StitchHelper.Tests;

public class ChartTableImportTests
{
    public static string SamplePath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../docs/sample-patterns/Spider-ManBWChart.pdf"));
    private static Dictionary<string, ThreadEntry> Catalog() => ThreadCatalog.Load(Path.Combine(AppContext.BaseDirectory, "rgb-dmc.json")).ToDictionary(t => t.Code);

    [SamplePdfFact]
    public void SuppliedChartHasExactDimensionsCountsOverlapAndSourceSymbols()
    {
        var data = new PdfImporter().Parse(SamplePath, Catalog());
        Assert.Equal(200, data.Width); Assert.Equal(300, data.Height);
        Assert.Equal(60000, data.Stitches.Count); Assert.Equal(60000, data.Stitches.Select(s => (s.X, s.Y)).Distinct().Count());
        Assert.Equal(78, data.Definitions.Count); Assert.Equal(20, data.Pages.Count);
        Assert.Equal(new SourcePage(8, 0, 0, 53, 72), data.Pages[0]);
        Assert.Equal(new SourcePage(9, 50, 0, 53, 72), data.Pages[1]);
        Assert.Equal(new SourcePage(12, 0, 69, 53, 72), data.Pages[4]);
        Assert.Equal(new SourcePage(27, 150, 276, 50, 24), data.Pages[^1]);
        var definitions = data.Definitions.ToDictionary(d => d.Id);
        var counts = data.Stitches.GroupBy(s => definitions[s.DefinitionId].ThreadCode).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(5119, counts["310"]); Assert.Equal(1826, counts["White"]); Assert.Equal(2756, counts["947"]);
        Assert.All(data.Definitions, d => { Assert.True(Catalog().ContainsKey(d.ThreadCode)); Assert.NotNull(d.SymbolGlyph); Assert.True(d.SymbolGlyph.Width > 0); Assert.Contains("M ", d.SymbolGlyph.Path); });
        Assert.Contains("Q ", data.Definitions.Single(d => d.ThreadCode == "154").SymbolGlyph!.Path); // preserve quadratic font curves as Q, not malformed C commands
        Assert.Contains(data.Warnings, w => w.Message.Contains("78 threads"));
        Assert.Contains(data.Warnings, w => w.Message.Contains("5,208"));
        var p = new Project { Data = data };
        ProjectCommands.Execute(p, new(0, "confirm"), Catalog());
        var first = data.Definitions[0];
        ProjectCommands.Execute(p, new(p.Revision, "definition", Definition: first with { Symbol = "!" }), Catalog());
        Assert.Null(p.Data.Definitions[0].SymbolGlyph);
        ProjectCommands.Execute(p, new(p.Revision, "undo"), Catalog());
        Assert.Equal(first.SymbolGlyph, p.Data.Definitions[0].SymbolGlyph);
    }

    [Fact]
    public void TabularLegendAndPrintedOffsetsRemoveOnlyMatchingOverlap()
    {
        WithFixture(false, path => {
            var data = new PdfImporter().Parse(path, Catalog());
            Assert.Equal(37, data.Width); Assert.Equal(20, data.Height); Assert.Equal(740, data.Stitches.Count);
            Assert.Equal(17, data.Pages[1].X); Assert.Equal(0, data.Pages[1].Y);
            Assert.All(data.Stitches, s => Assert.Equal("310", data.Definitions.Single(d => d.Id == s.DefinitionId).ThreadCode));
        });
    }

    [Fact]
    public void ConflictingOverlapStopsImportInsteadOfDroppingStitches()
    {
        WithFixture(true, path => Assert.Contains("Repeated stitches disagree", Assert.Throws<UserError>(() => new PdfImporter().Parse(path, Catalog())).Message));
    }

    private static void WithFixture(bool conflict, Action<string> check)
    {
        var builder = new PdfDocumentBuilder(); var text = builder.AddStandard14Font(Standard14Font.Helvetica); var symbol = builder.AddStandard14Font(Standard14Font.Courier);
        var legend = builder.AddPage(350, 260);
        foreach (var (label, x) in new[] { ("Symbol", 35), ("Strands", 90), ("Type", 140), ("Number", 180), ("Color", 240) }) legend.AddText(label, 8, new PdfPoint(x, 220), text);
        for (var row = 0; row < 2; row++) {
            var y = 200 - row * 12;
            legend.AddText(row == 0 ? "X" : "Y", 9, new PdfPoint(45, y + 1.4), symbol);
            legend.AddText("2", 8, new PdfPoint(100, y), text); legend.AddText("DMC", 8, new PdfPoint(140, y), text);
            legend.AddText(row == 0 ? "310" : "321", 8, new PdfPoint(180, y), text); legend.AddText(row == 0 ? "Black" : "Red", 8, new PdfPoint(240, y), text);
        }
        for (var tile = 0; tile < 2; tile++) {
            var page = builder.AddPage(300, 260); var offset = tile * 17;
            for (var y = 0; y < 20; y++) for (var x = 0; x < 20; x++) page.AddText(conflict && tile == 1 && x == 0 && y == 0 ? "Y" : "X", 8, new PdfPoint(31.3 + x * 10, 223.3 - y * 10), symbol);
            foreach (var n in Enumerable.Range(1, 4).Select(n => n * 10).Where(n => n > offset && n <= offset + 20)) page.AddText(n.ToString(), 8, new PdfPoint(26 + (n - offset) * 10, 233), text);
            page.AddText("10", 8, new PdfPoint(12, 123.3), text); page.AddText("20", 8, new PdfPoint(12, 23.3), text);
        }
        var path = Path.Combine(Path.GetTempPath(), $"chart-table-{Guid.NewGuid():N}.pdf"); File.WriteAllBytes(path, builder.Build());
        try { check(path); } finally { File.Delete(path); }
    }
}

public sealed class SamplePdfFactAttribute : FactAttribute
{
    public SamplePdfFactAttribute() { if (!File.Exists(ChartTableImportTests.SamplePath)) Skip = "User-provided PDF is not present; synthetic format regressions still run."; }
}
