using StitchHelper;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using Xunit;

namespace StitchHelper.Tests;

public class DomainTests
{
    public static Dictionary<string, ThreadEntry> Catalog() => ThreadCatalog.Load(Path.Combine(AppContext.BaseDirectory, "rgb-dmc.json")).ToDictionary(x => x.Code);
    [Fact]
    public void CompletionUndoRedoAndMilestonesArePreserved()
    {
        var p = new Project { Data = SamplePattern.Create().Data, Status = "active" };
        var ids = p.Data.Stitches.Select(x => x.Id).ToArray();
        ProjectCommands.Execute(p, new(0, "complete", ids), Catalog());
        Assert.Equal(ids.Length, p.Completed.Count); Assert.Equal(6, p.Milestones.Count);
        ProjectCommands.Execute(p, new(p.Revision, "undo"), Catalog()); Assert.Empty(p.Completed);
        ProjectCommands.Execute(p, new(p.Revision, "redo"), Catalog()); Assert.Equal(ids.Length, p.Completed.Count);
        ProjectCommands.Execute(p, new(p.Revision, "stitch", StitchId: ids[0]), Catalog()); Assert.DoesNotContain(ids[0], p.Completed);
        ProjectCommands.Execute(p, new(p.Revision, "undo"), Catalog()); Assert.Contains(ids[0], p.Completed);
        Assert.Equal(409, Assert.Throws<UserError>(() => ProjectCommands.Execute(p, new(0, "rename", Name: "stale"), Catalog())).Status);
        Assert.Throws<UserError>(() => ProjectCommands.Execute(p, new(p.Revision, "complete", ["missing"]), Catalog()));
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void EveryUsageSupportsIndependentReversibleComponentSubstitution(int size)
    {
        var cs = new[] { new ThreadUsageComponent("c1", "310"), new ThreadUsageComponent("c2", "321", 1), new ThreadUsageComponent("c3", "500", 2) }.Take(size).ToList();
        var p = new Project { Data = new() { Width = 1, Height = 1, Definitions = [new("d1", "X", "310", Components: cs)], Stitches = [new("s1", 0, 0, "d1")] }, Status = "active" };
        ProjectCommands.ValidateComponents(p.Data.Definitions[0], Catalog());
        Assert.Equal(size == 1 ? "Single" : "Blend", p.Data.Definitions[0].UsageKind);
        ProjectCommands.Execute(p, new(0, "substitute", Code: "c1", Replacement: "White"), Catalog());
        Assert.Single(p.Substitutions); Assert.Equal("White", p.Substitutions["c1"]); Assert.Equal("310", cs[0].ThreadCode);
        ProjectCommands.Execute(p, new(p.Revision, "undo"), Catalog()); Assert.Empty(p.Substitutions);
        ProjectCommands.Execute(p, new(p.Revision, "redo"), Catalog()); Assert.Single(p.Substitutions);
        ProjectCommands.Execute(p, new(p.Revision, "complete", ["s1"]), Catalog()); Assert.Single(p.Completed);
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)]
    public void InvalidStrandCountsAreRejected(int count) => Assert.Throws<UserError>(() => ProjectCommands.ValidateComponents(new("d", "X", "310", Components: [new("c", "310", count)]), Catalog()));
    [Fact]
    public void EmptyDuplicateAndUnknownComponentsAreRejected()
    {
        foreach (var cs in new List<ThreadUsageComponent>[] { [], [new("c", "310"), new("c2", "310")], [new("c", "310"), new("c", "321")], [new("c", "UNKNOWN")] })
            Assert.Throws<UserError>(() => ProjectCommands.ValidateComponents(new("d", "X", "310", Components: cs), Catalog()));
    }
    [Fact]
    public void ExplicitBlendImportPreservesUnknownAndKnownStrands()
    {
        var cs = ThreadUsageParser.Parse("DMC 310 (1) + 321 + 500 (2)", "d", Catalog());
        Assert.Equal(3, cs.Count); Assert.Equal(1, cs[0].StrandCount); Assert.Null(cs[1].StrandCount); Assert.Equal(2, cs[2].StrandCount);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, CreatePdf("X   DMC 310 (1) + 321 + 500 (2)"));
            var data = new PdfImporter().Parse(path, Catalog());
            Assert.Equal(200, data.Stitches.Count); Assert.Equal(3, data.Definitions[0].GetComponents().Count);
            Assert.Null(data.Definitions[0].GetComponents()[1].StrandCount);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void MultipageImportAndPageCorrectionRemainReversible()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, CreatePdf()); var data = new PdfImporter().Parse(path, Catalog());
            Assert.Equal(10, data.Width); Assert.Equal(20, data.Height); Assert.Equal(200, data.Stitches.Count);
            var p = new Project { Data = data };
            ProjectCommands.Execute(p, new(0, "page", PageNumber: 2, X: 10, Y: 0), Catalog()); Assert.Equal(20, p.Data.Width);
            ProjectCommands.Execute(p, new(p.Revision, "undo"), Catalog()); Assert.Equal(10, p.Data.Width);
            ProjectCommands.Execute(p, new(p.Revision, "confirm"), Catalog()); Assert.Equal("active", p.Status);
            var fixture = Environment.GetEnvironmentVariable("STITCH_FIXTURE_PATH"); if (fixture is not null) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(fixture))!); File.Copy(path, fixture, true); }
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void EmptyPdfCannotBeConfirmed()
    {
        var path = Path.GetTempFileName();
        try { var builder = new PdfDocumentBuilder(); builder.AddPage(200, 200); File.WriteAllBytes(path, builder.Build()); var data = new PdfImporter().Parse(path, Catalog()); Assert.Empty(data.Stitches); Assert.Throws<UserError>(() => ProjectCommands.Execute(new() { Data = data }, new(0, "confirm"), Catalog())); }
        finally { File.Delete(path); }
    }
    public static byte[] CreatePdf(string legend = "X   DMC 310 Black")
    {
        var builder = new PdfDocumentBuilder(); var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var p = 0; p < 2; p++)
        {
            var page = builder.AddPage(350, 250);
            for (var i = 0; i <= 10; i++) { page.DrawLine(new PdfPoint(30 + i * 12, 30), new PdfPoint(30 + i * 12, 150)); page.DrawLine(new PdfPoint(30, 30 + i * 12), new PdfPoint(150, 30 + i * 12)); }
            for (var y = 0; y < 10; y++) for (var x = 0; x < 10; x++) page.AddText("X", 8, new PdfPoint(33 + x * 12, 33 + y * 12), font);
            page.AddText(legend, 10, new PdfPoint(30, 190), font);
        }
        return builder.Build();
    }
}
