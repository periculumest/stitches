using StitchHelper;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace StitchHelper.Tests;

public class ImportSymbolTests
{
    [Fact]
    public void ReplacementsAvoidAllExistingSymbolsAndPreserveThreadAndStitchIdentity()
    {
        var glyph = new SymbolGlyph("M 0 0 L 1 1 Z", 0, 0, 1, 1);
        var data = new PatternData {
            Width = 3, Height = 1,
            Definitions = [new("missing", "?", "310", Components: [new("c1", "310", 1), new("c2", "321", 2)]),
                new("existing", "A", "500"), new("outline", "B", "321", SymbolGlyph: glyph), new("missing2", "\uE000", "310")],
            Stitches = [new("s1", 0, 0, "missing"), new("s2", 1, 0, "missing"), new("s3", 2, 0, "missing2")],
            PageOverlapStitches = [new("overlap", 0, 0, "missing")]
        };
        var before = Json.Copy(data);
        ImportSymbols.AssignMissing(data);
        Assert.Equal(4, data.Definitions.Select(d => d.Symbol).Distinct().Count());
        Assert.Equal(Json.Write(before.Definitions[0] with { Symbol = data.Definitions[0].Symbol }), Json.Write(data.Definitions[0]));
        Assert.Equal(before.Definitions[1], data.Definitions[1]);
        Assert.Equal(before.Definitions[2], data.Definitions[2]);
        Assert.Equal(before.Stitches, data.Stitches);
        Assert.Equal(before.PageOverlapStitches, data.PageOverlapStitches);
        Assert.Equal(2, data.Warnings.Count);
        var after = Json.Write(data);
        ImportSymbols.AssignMissing(data);
        Assert.Equal(after, Json.Write(data));
        var project = new Project { Data = data };
        ProjectCommands.Execute(project, new(0, "confirm"), DomainTests.Catalog());
        Assert.Equal("active", project.Status);
    }

    [Fact]
    public void ReplacementDoesNotInventAnUnknownThreadMapping()
    {
        var data = new PatternData { Width = 1, Height = 1,
            Definitions = [new("d", "?", "UNKNOWN")], Stitches = [new("s", 0, 0, "d")] };
        ImportSymbols.AssignMissing(data);
        Assert.NotEqual("?", data.Definitions[0].Symbol);
        Assert.Equal("UNKNOWN", data.Definitions[0].ThreadCode);
        Assert.Throws<UserError>(() => ProjectCommands.Execute(new Project { Data = data }, new(0, "confirm"), DomainTests.Catalog()));
    }

    [Fact]
    public void MissingFontShapesGetDistinctLabelsEvenWhenTheSingleCharacterPoolIsUsed()
    {
        var data = new PatternData {
            Definitions = Enumerable.Range(0, 200).Select(i => new StitchDefinition($"d{i}", "?", "310")).ToList()
        };
        ImportSymbols.AssignMissing(data, requireSourceGlyph: true);
        Assert.Equal(200, data.Definitions.Select(d => d.Symbol).Distinct().Count());
        Assert.All(data.Definitions, d => { Assert.InRange(d.Symbol.Length, 1, 4); Assert.DoesNotContain("?", d.Symbol); });
        var text = new PatternData { Definitions = [new("d", "X", "310")] };
        ImportSymbols.AssignMissing(text, requireSourceGlyph: true);
        Assert.NotEqual("X", text.Definitions[0].Symbol);
    }

    [Fact]
    public void PdfQuestionMarksGetUnusedSymbolsWithoutMergingDifferentSourceFonts()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var otherFont = builder.AddStandard14Font(Standard14Font.Courier);
        var page = builder.AddPage(250, 250);
        for (var i = 0; i <= 5; i++) {
            page.DrawLine(new PdfPoint(30 + i * 12, 30), new PdfPoint(30 + i * 12, 90));
            page.DrawLine(new PdfPoint(30, 30 + i * 12), new PdfPoint(90, 30 + i * 12));
        }
        page.AddText("?", 8, new PdfPoint(33, 33), font);
        page.AddText("?", 8, new PdfPoint(45, 33), font);
        page.AddText("?", 8, new PdfPoint(57, 33), otherFont);
        page.AddText("A", 8, new PdfPoint(69, 33), font);
        page.AddText("?   DMC 310 Black", 10, new PdfPoint(30, 190), font);
        page.AddText("A   DMC 321 Red", 10, new PdfPoint(30, 170), font);
        var path = Path.GetTempFileName();
        try {
            File.WriteAllBytes(path, builder.Build());
            var data = new PdfImporter().Parse(path, DomainTests.Catalog());
            Assert.Equal(4, data.Stitches.Count);
            Assert.Equal(3, data.Definitions.Count);
            Assert.Equal(3, data.Definitions.Select(d => d.Symbol).Distinct().Count());
            Assert.DoesNotContain(data.Definitions, d => d.Symbol == "?");
            Assert.Equal("A", data.Definitions.Single(d => d.ThreadCode == "321").Symbol);
            Assert.Equal(data.Stitches[0].DefinitionId, data.Stitches[1].DefinitionId);
            Assert.NotEqual(data.Stitches[0].DefinitionId, data.Stitches[2].DefinitionId);
            var project = new Project { Data = data };
            ProjectCommands.Execute(project, new(0, "confirm"), DomainTests.Catalog());
            Assert.Equal("active", project.Status);
        } finally { File.Delete(path); }
    }
}
