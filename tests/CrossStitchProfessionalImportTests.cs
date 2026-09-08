using StitchHelper;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace StitchHelper.Tests;

public class CrossStitchProfessionalImportTests
{
    public static string SamplePath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../docs/sample-patterns/Iron Man (1).pdf"));

    [IronManPdfFact]
    public void IronManHasAllPagesThreadsAndOriginalSymbols()
    {
        var catalog = DomainTests.Catalog();
        var data = new PdfImporter().Parse(SamplePath, catalog);
        Assert.Equal(450, data.Width); Assert.Equal(450, data.Height);
        Assert.Equal(202500, data.Stitches.Count);
        Assert.Equal(202500, data.Stitches.Select(s => (s.X, s.Y)).Distinct().Count());
        Assert.Equal(63, data.Definitions.Count); Assert.Equal(48, data.Pages.Count);
        Assert.Equal(new SourcePage(1, 0, 0, 61, 85), data.Pages[0]);
        Assert.Equal(new SourcePage(2, 61, 0, 61, 85), data.Pages[1]);
        Assert.Equal(new SourcePage(8, 427, 0, 23, 85), data.Pages[7]);
        Assert.Equal(new SourcePage(9, 0, 85, 61, 85), data.Pages[8]);
        Assert.Equal(new SourcePage(48, 427, 425, 23, 25), data.Pages[^1]);
        Assert.All(data.Definitions, d => {
            Assert.True(catalog.ContainsKey(d.ThreadCode)); Assert.Equal("FullCross", d.StitchType);
            Assert.Equal(2, Assert.Single(d.GetComponents()).StrandCount);
            Assert.NotNull(d.SymbolGlyph); Assert.Contains("M ", d.SymbolGlyph.Path);
            Assert.True(d.SymbolGlyph.Width > 0 && d.SymbolGlyph.Height > 0);
        });
        // Counts independently tallied from source font codes; mappings checked against the printed two-column key.
        var definitions = data.Definitions.ToDictionary(d => d.Id);
        var counts = data.Stitches.GroupBy(s => definitions[s.DefinitionId].ThreadCode).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(40346, counts["310"]); Assert.Equal(23133, counts["939"]);
        Assert.Equal(15187, counts["3371"]); Assert.Equal(5860, counts["3756"]);
        Assert.Equal(4, counts["826"]); Assert.Equal(469, counts["3820"]);
        Assert.Equal("823", definitions[data.Stitches.Single(s => s.X == 0 && s.Y == 0).DefinitionId].ThreadCode);
        Assert.Equal("310", definitions[data.Stitches.Single(s => s.X == 449 && s.Y == 449).DefinitionId].ThreadCode);
        Assert.Equal("9", data.Definitions.Single(d => d.ThreadCode == "3820").Symbol); // A numeric font symbol must not merge with its DMC code.
        var dot = data.Definitions.Single(d => d.ThreadCode == "3756").SymbolGlyph!;
        var square = data.Definitions.Single(d => d.ThreadCode == "310").SymbolGlyph!;
        Assert.Equal((square.MinX, square.MinY, square.Width, square.Height), (dot.MinX, dot.MinY, dot.Width, dot.Height));
        Assert.NotEqual(square.Path, dot.Path);
        Assert.Equal(2, data.Warnings.Count);
        var project = new Project { Data = data };
        ProjectCommands.Execute(project, new(0, "confirm"), catalog);
        Assert.Equal("active", project.Status);
    }

    [Theory]
    [InlineData("normal", 740)]
    [InlineData("blank", 739)]
    public void ColumnLegendAndOneBasedCoordinatesAssembleMatchingOverlap(string variant, int stitches)
    {
        WithFixture(variant, path => {
            var data = new PdfImporter().Parse(path, DomainTests.Catalog());
            Assert.Equal(37, data.Width); Assert.Equal(20, data.Height); Assert.Equal(stitches, data.Stitches.Count);
            Assert.Equal(17, data.Pages[1].X); Assert.Equal(0, data.Pages[1].Y);
            Assert.Equal(new[] { "310", "321" }, data.Definitions.Select(d => d.ThreadCode));
            Assert.All(data.Definitions, d => Assert.Equal(2, Assert.Single(d.GetComponents()).StrandCount));
            Assert.Contains(data.Warnings, w => w.Message.Contains("60 matching overlap"));
            Assert.Equal(60, data.PageOverlapStitches.Count);
            var separated = PageLayout.Arrange(data, [new(data.Pages[0].Number, 0, 0), new(data.Pages[1].Number, 20, 0)]);
            Assert.Equal(stitches + 60, separated.Stitches.Count);
            Assert.Empty(separated.PageOverlapStitches);
        });
    }

    [Theory]
    [InlineData("conflict", "Repeated stitches disagree")]
    [InlineData("missing-page", "do not cover")]
    [InlineData("missing-labels", "row coordinates")]
    [InlineData("wrong-size", "outside the printed")]
    [InlineData("unknown-symbol", "missing from the key")]
    [InlineData("missing-code", "no readable DMC code")]
    [InlineData("duplicate", "multiple symbols")]
    public void AmbiguousOrIncompleteChartsAreRejected(string variant, string error)
        => WithFixture(variant, path => Assert.Contains(error, Assert.Throws<UserError>(() => new PdfImporter().Parse(path, DomainTests.Catalog())).Message));

    private static void WithFixture(string variant, Action<string> check)
    {
        var builder = new PdfDocumentBuilder();
        var text = builder.AddStandard14Font(Standard14Font.Helvetica);
        var symbol = builder.AddStandard14Font(Standard14Font.Courier);
        var key = builder.AddPage(600, 450);
        key.AddText("Produced using Cross Stitch Professional for Windows", 10, new PdfPoint(30, 420), text);
        key.AddText($"Stitches: {(variant == "wrong-size" ? 36 : 37)} x 20", 10, new PdfPoint(30, 395), text);
        key.AddText("Colours: DMC", 10, new PdfPoint(30, 375), text);
        key.AddText("Use 2 strands of thread for cross stitch", 10, new PdfPoint(30, 350), text);
        for (var column = 0; column < 2; column++)
        {
            var shift = column * 270;
            key.AddText("Sym", 8, new PdfPoint(30 + shift, 320), text);
            key.AddText("No.", 8, new PdfPoint(100 + shift, 320), text);
            key.AddText("Colour Name", 8, new PdfPoint(150 + shift, 320), text);
            key.AddText(column == 0 ? "X" : "Y", 9, new PdfPoint(35 + shift, 298.6), symbol);
            if (variant != "missing-code" || column != 0) key.AddText(column == 0 ? "310" : "321", 8, new PdfPoint(100 + shift, 300), text);
            key.AddText(column == 0 ? "Black" : "Red", 8, new PdfPoint(150 + shift, 300), text);
        }
        for (var tile = 0; tile < (variant == "missing-page" ? 1 : 2); tile++)
        {
            var page = builder.AddPage(300, 280); var offset = tile * 17;
            for (var i = 0; i <= 20; i++)
            {
                page.DrawLine(new PdfPoint(30 + i * 10, 30), new PdfPoint(30 + i * 10, 230));
                page.DrawLine(new PdfPoint(30, 30 + i * 10), new PdfPoint(230, 30 + i * 10));
            }
            for (var y = 0; y < 20; y++) for (var x = 0; x < 20; x++)
            {
                if (variant == "blank" && tile == 0 && x == 5 && y == 5) continue;
                var value = variant == "conflict" && tile == 1 && x == 0 && y == 0 ? "Y" : variant == "unknown-symbol" && tile == 0 && x == 0 && y == 0 ? "Z" : "X";
                page.AddText(value, 8, new PdfPoint(32 + x * 10, 222 - y * 10), symbol);
            }
            if (variant == "duplicate" && tile == 0) page.AddText("Y", 8, new PdfPoint(33, 223), symbol);
            foreach (var n in new[] { 10, 20, 30 }.Where(n => n > offset && n <= offset + 20))
                page.AddText(n.ToString(), 6, new PdfPoint(32 + (n - offset - 1) * 10, 234), text);
            if (variant != "missing-labels" || tile != 1)
                foreach (var n in new[] { 10, 20 }) page.AddText(n.ToString(), 6, new PdfPoint(12, 223 - (n - 1) * 10), text);
        }
        var path = Path.Combine(Path.GetTempPath(), $"professional-chart-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, builder.Build());
        try { check(path); } finally { File.Delete(path); }
    }
}

public sealed class IronManPdfFactAttribute : FactAttribute
{
    public IronManPdfFactAttribute() { if (!File.Exists(CrossStitchProfessionalImportTests.SamplePath)) Skip = "User-provided PDF is absent; synthetic importer checks still run."; }
}
