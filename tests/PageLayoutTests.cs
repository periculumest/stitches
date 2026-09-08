using StitchHelper;
using Xunit;

namespace StitchHelper.Tests;

public class PageLayoutTests
{
    private static Project Project()
    {
        var data = new PatternData { Width = 2, Height = 4, PageSourcesComplete = true, Pages = [new(1, 0, 0, 2, 2), new(2, 0, 2, 2, 2)], Definitions = [new("d1", "X", "310"), new("d2", "O", "321")] };
        foreach (var page in data.Pages) for (var y = 0; y < 2; y++) for (var x = 0; x < 2; x++) data.Stitches.Add(new($"p{page.Number}-{x}-{y}", x, page.Y + y, "d1"));
        return new() { Data = data };
    }
    private static void Arrange(Project p, int x, int y) => ProjectCommands.Execute(p, new(p.Revision, "layout", Pages: [new(1, 0, 0), new(2, x, y)]), DomainTests.Catalog());

    [Fact]
    public void ArrangementIsAtomicAndUndoRedoRestoreAllCoordinates()
    {
        var p = Project(); var before = Json.Write(p.Data);
        Arrange(p, 2, 0);
        Assert.Equal((4, 2), (p.Data.Width, p.Data.Height)); Assert.Equal(8, p.Data.Stitches.Count);
        Assert.Equal((2d, 0d), (p.Data.Stitches.Single(s => s.Id == "p2-0-0").X, p.Data.Stitches.Single(s => s.Id == "p2-0-0").Y));
        Assert.Single(p.Undo); Assert.Equal("audit", p.Status);
        ProjectCommands.Execute(p, new(p.Revision, "undo"), DomainTests.Catalog()); Assert.Equal(before, Json.Write(p.Data));
        ProjectCommands.Execute(p, new(p.Revision, "redo"), DomainTests.Catalog()); Assert.Equal(4, p.Data.Width);
    }
    [Fact]
    public void MatchingOverlapsCanBeSeparatedAfterSerializationWithoutLosingStitches()
    {
        var p = Project(); var ids = p.Data.Stitches.Select(s => s.Id).Order().ToArray();
        Arrange(p, 1, 0); Assert.Equal(6, p.Data.Stitches.Count); Assert.Equal(2, p.Data.PageOverlapStitches.Count);
        p = Json.Copy(p); Arrange(p, 2, 0);
        Assert.Equal(8, p.Data.Stitches.Count); Assert.Empty(p.Data.PageOverlapStitches);
        Assert.Equal(ids, p.Data.Stitches.Select(s => s.Id).Order().ToArray());
        Arrange(p, 1, 0); ProjectCommands.Execute(p, new(p.Revision, "confirm"), DomainTests.Catalog());
        Assert.Equal("active", p.Status); Assert.Equal(6, p.Data.Stitches.Count);
    }
    [Fact]
    public void ConflictRejectsEntireArrangementAndPreservesHistory()
    {
        var p = Project(); p.Data.Stitches[4] = p.Data.Stitches[4] with { DefinitionId = "d2" };
        var before = Json.Write(p);
        Assert.Contains("conflicts", Assert.Throws<UserError>(() => Arrange(p, 1, 0)).Message);
        Assert.Equal(before, Json.Write(p));
    }
    [Theory]
    [InlineData("missing")] [InlineData("duplicate")] [InlineData("unknown")] [InlineData("negative")] [InlineData("huge")] [InlineData("null")]
    public void InvalidPlacementPayloadsAreRejected(string kind)
    {
        var p = Project(); List<PagePlacement>? pages = kind switch {
            "missing" => [new(1, 0, 0)], "duplicate" => [new(1, 0, 0), new(1, 2, 0)], "unknown" => [new(1, 0, 0), new(3, 2, 0)],
            "negative" => [new(1, 0, 0), new(2, -1, 0)], "huge" => [new(1, 0, 0), new(2, int.MaxValue, 0)], _ => null };
        Assert.Throws<UserError>(() => ProjectCommands.Execute(p, new(0, "layout", Pages: pages), DomainTests.Catalog()));
        Assert.Equal(0, p.Revision); Assert.Empty(p.Undo);
    }
    [Fact]
    public void ActiveAndStaleProjectsCannotBeRearranged()
    {
        var p = Project(); p.Status = "active";
        Assert.Throws<UserError>(() => Arrange(p, 2, 0));
        p.Status = "audit"; p.Revision = 1;
        Assert.Equal(409, Assert.Throws<UserError>(() => ProjectCommands.Execute(p, new(0, "layout", Pages: [new(1, 0, 0), new(2, 2, 0)]), DomainTests.Catalog())).Status);
    }
    [Fact]
    public void LayoutHistoryCannotDetachProgressWrittenByAnotherWindow()
    {
        var p = Project(); Arrange(p, 1, 0); p.Status = "active"; p.Completed.Add(p.Data.Stitches[0].Id);
        var before = Json.Write(p);
        Assert.Throws<UserError>(() => ProjectCommands.Execute(p, new(p.Revision, "undo"), DomainTests.Catalog()));
        Assert.Equal(before, Json.Write(p));
    }
    [Fact]
    public void LegacyOverlappingImportsRequireReimportButNonOverlappingImportsWork()
    {
        var p = Project(); p.Data.PageSourcesComplete = false; Arrange(p, 2, 0); Assert.Equal(8, p.Data.Stitches.Count);
        Arrange(p, 1, 0); p.Data.PageSourcesComplete = false; p.Data.PageOverlapStitches.Clear();
        Assert.Contains("Re-import", Assert.Throws<UserError>(() => Arrange(p, 2, 0)).Message);
    }
    [Fact]
    public void EditingAndUndoingAnOverlappedStitchPreservesItsSourceInstances()
    {
        var p = Project(); Arrange(p, 1, 0);
        var stitch = p.Data.Stitches.Single(s => s.X == 1 && s.Y == 0);
        ProjectCommands.Execute(p, new(p.Revision, "stitch", Stitch: stitch with { DefinitionId = "d2" }), DomainTests.Catalog());
        Assert.Contains(p.Data.PageOverlapStitches, s => s.X == 1 && s.Y == 0 && s.DefinitionId == "d2");
        ProjectCommands.Execute(p, new(p.Revision, "undo"), DomainTests.Catalog());
        Assert.All(p.Data.PageOverlapStitches, s => Assert.Equal("d1", s.DefinitionId));
        ProjectCommands.Execute(p, new(p.Revision, "stitch", StitchId: stitch.Id), DomainTests.Catalog());
        Arrange(p, 2, 0); Assert.Equal(6, p.Data.Stitches.Count);
    }
}
