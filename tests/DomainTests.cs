using StitchHelper;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using Xunit;

namespace StitchHelper.Tests;

public class DomainTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "stitch-tests-" + Guid.NewGuid().ToString("N"));
    private Repository Repo() => new(root);
    private static void Execute(Repository repo, Project p, Command c) { var revision = p.Revision; ProjectCommands.Execute(p, c with { Revision = revision }, repo.Catalog()); repo.Save(p, revision); }
    [Fact]
    public void CompletionIsAtomicReversibleDurableAndIsolated()
    {
        var repo = Repo(); var pattern = SamplePattern.Create(); var p = repo.Create(pattern, status: "active"); var second = repo.Create(pattern);
        var ids = p.Data.Stitches.Take(30).Select(s => s.Id).ToArray();
        Execute(repo, p, new(0, "complete", ids));
        Assert.Equal(30, new Repository(root).Get(p.Id).Completed.Count);
        Assert.Empty(repo.Get(second.Id).Completed);
        Assert.Throws<UserError>(() => ProjectCommands.Execute(p, new(0, "complete", ids), repo.Catalog()));
        Assert.Throws<UserError>(() => ProjectCommands.Execute(p, new(p.Revision, "complete", ["bad"]), repo.Catalog()));
        Execute(repo, p, new(0, "undo")); Assert.Empty(p.Completed);
        p = repo.Get(p.Id); Execute(repo, p, new(0, "redo")); Assert.Equal(30, p.Completed.Count);
        Execute(repo, p, new(0, "undo")); Execute(repo, p, new(0, "complete", [ids[0]])); Assert.Empty(p.Redo);
    }
    [Fact]
    public void SubstitutionAndEditsPreserveSourceAndUndoCompletionOfDeletedStitch()
    {
        var repo = Repo(); var source = SamplePattern.Create(); var p = repo.Create(source, status: "active"); var other = repo.Create(source);
        var d = p.Data.Definitions[0]; var s = p.Data.Stitches.First(st => st.DefinitionId == d.Id);
        Execute(repo, p, new(0, "substitute", Code: d.ThreadCode, Replacement: "310"));
        Assert.Equal("310", p.Substitutions[d.ThreadCode]); Assert.Empty(repo.Get(other.Id).Substitutions);
        Assert.Equal(d.ThreadCode, repo.GetPattern(source.Id).Data.Definitions[0].ThreadCode);
        Execute(repo, p, new(0, "undo")); Assert.Empty(p.Substitutions);
        Execute(repo, p, new(0, "redo")); Assert.Equal("310", p.Substitutions[d.ThreadCode]);
        Execute(repo, p, new(0, "definition", Definition: d with { Symbol = "Z", StitchType = "HalfCross" }));
        Assert.Equal("Z", p.Data.Definitions[0].Symbol); Execute(repo, p, new(0, "undo")); Assert.Equal(d, p.Data.Definitions[0]);
        Execute(repo, p, new(0, "complete", [s.Id])); Execute(repo, p, new(0, "stitch", StitchId: s.Id));
        Assert.DoesNotContain(s.Id, p.Completed); Execute(repo, p, new(0, "undo")); Assert.Contains(s.Id, p.Completed); Assert.Contains(s, p.Data.Stitches);
        Execute(repo, p, new(0, "redo")); Assert.DoesNotContain(s.Id, p.Completed); Assert.DoesNotContain(s, p.Data.Stitches);
    }
    [Fact]
    public void MilestonesAreOncePerProjectEvenAfterUndoAndReopen()
    {
        var repo = Repo(); var p = repo.Create(SamplePattern.Create(), status: "active");
        var ids = p.Data.Stitches.Select(s => s.Id).ToArray(); Execute(repo, p, new(0, "complete", ids));
        Assert.Equal(new[] { 10, 25, 50, 75, 90, 100 }, p.Milestones.Order());
        Execute(repo, p, new(0, "undo")); Assert.Empty(p.Completed); Assert.Equal(6, p.Milestones.Count);
        p = repo.Get(p.Id); Execute(repo, p, new(0, "redo")); Assert.Equal(6, p.Milestones.Count);
    }
    [Fact]
    public void CompleteBackupRestoresProgressInventorySourceAndHistory()
    {
        var repo = Repo(); var source = SamplePattern.Create() with { SourceFile = "original.pdf" };
        File.WriteAllText(Path.Combine(root, "sources", "original.pdf"), "%PDF-test-fixture");
        var p = repo.Create(source, status: "active"); Execute(repo, p, new(0, "complete", p.Data.Stitches.Take(120).Select(s => s.Id).ToArray()));
        Execute(repo, p, new(0, "substitute", Code: "500", Replacement: "310")); repo.SaveInventory(new("310", 4, "Box 1 / Row 3"));
        var backup = new BackupService(repo, NullLogger<BackupService>.Instance); var name = backup.Create();
        var target = Path.Combine(root, "restored"); BackupService.Restore(Path.Combine(root, "backups", name), target);
        var restored = new Repository(target); var actual = restored.Get(p.Id);
        Assert.Equal(120, actual.Completed.Count); Assert.Equal("310", actual.Substitutions["500"]); Assert.Equal(4, restored.Inventory().Single().BobbinCount);
        Assert.Equal("%PDF-test-fixture", File.ReadAllText(Path.Combine(target, "sources", "original.pdf")));
        Execute(restored, actual, new(0, "undo")); Assert.Empty(actual.Substitutions);
        Assert.Throws<UserError>(() => BackupService.Restore(Path.Combine(root, "backups", name), target));
        for (var i = 0; i < 9; i++) backup.Create(true);
        Assert.Equal(7, Directory.GetFiles(Path.Combine(root, "backups"), "auto-*.zip").Length);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "backups"), "manual-*.zip"));
    }
    [Fact]
    public void CorruptBackupDoesNotReplaceTarget()
    {
        var repo = Repo(); repo.Create(SamplePattern.Create()); var backup = new BackupService(repo, NullLogger<BackupService>.Instance); var name = backup.Create(); var path = Path.Combine(root, "backups", name);
        using (var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update))
        {
            var db = archive.GetEntry("stitch-helper.db")!; db.Delete(); var entry = archive.CreateEntry("stitch-helper.db"); using var writer = new StreamWriter(entry.Open()); writer.Write("corrupt");
        }
        var target = Path.Combine(root, "bad-restore"); Assert.Throws<UserError>(() => BackupService.Restore(path, target)); Assert.False(Directory.Exists(target));
    }
    [Fact]
    public void StructuredMultipagePdfExtractsChartAndLegend()
    {
        var repo = Repo(); var path = Path.Combine(root, "fixture.pdf"); File.WriteAllBytes(path, CreatePdf());
        var data = new PdfImporter().Parse(path, repo.Catalog());
        Assert.Equal(10, data.Width); Assert.Equal(20, data.Height); Assert.Equal(200, data.Stitches.Count); Assert.Equal(2, data.Pages.Count);
        Assert.All(data.Definitions, d => Assert.Equal("310", d.ThreadCode)); Assert.Contains(data.Warnings, w => w.Message.Contains("stacked"));
        var p = repo.Create(new("pdf", "fixture", "fixture.pdf", data));
        Execute(repo, p, new(0, "page", PageNumber: 2, X: 10, Y: 0)); Assert.Equal(20, p.Data.Width); Assert.Equal(10, p.Data.Height);
        Execute(repo, p, new(0, "undo")); Assert.Equal(10, p.Data.Width); Assert.Equal(20, p.Data.Height);
        Execute(repo, p, new(0, "confirm")); Assert.Equal("active", p.Status);
        // Keep a reviewable synthetic PDF in artifacts when explicitly requested by the test runner.
        var artifact = Environment.GetEnvironmentVariable("STITCH_FIXTURE_PATH"); if (artifact is not null) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(artifact))!); File.Copy(path, artifact, true); }
    }
    [Fact]
    public void EmptyPdfAndUnresolvedThreadCannotBeFinalized()
    {
        var repo = Repo(); var builder = new PdfDocumentBuilder(); builder.AddPage(200, 200); var path = Path.Combine(root, "empty.pdf"); File.WriteAllBytes(path, builder.Build());
        var data = new PdfImporter().Parse(path, repo.Catalog()); Assert.Empty(data.Stitches); Assert.NotEmpty(data.Warnings);
        var p = repo.Create(new("empty", "empty", "empty.pdf", data)); Assert.Throws<UserError>(() => ProjectCommands.Execute(p, new(0, "confirm"), repo.Catalog()));
        p = repo.Create(SamplePattern.Create()); p.Data.Definitions[0] = p.Data.Definitions[0] with { ThreadCode = "UNKNOWN" };
        Assert.Throws<UserError>(() => ProjectCommands.Execute(p, new(0, "confirm"), repo.Catalog()));
    }
    [Fact]
    public void InventoryValidationAndLargePatternCommandWork()
    {
        var repo = Repo(); Assert.Throws<UserError>(() => repo.SaveInventory(new("310", -1, "box")));
        repo.SaveInventory(new("310", 2, "Box 2")); Assert.Equal(2, repo.Inventory().Single().BobbinCount);
        repo.SaveInventory(new("310", 0, "")); Assert.Empty(repo.Inventory());
        repo.AddThread(new("9999", "My color", "#abcdef")); Assert.Contains("9999", repo.Catalog().Keys);
        var p = repo.Create(SamplePattern.Create(1000), status: "active"); Assert.True(p.Data.Stitches.Count > 100000);
        var before = System.Diagnostics.Stopwatch.StartNew(); Execute(repo, p, new(0, "complete", p.Data.Stitches.Take(10000).Select(s => s.Id).ToArray()));
        Assert.Equal(10000, repo.Get(p.Id).Completed.Count); Assert.True(before.Elapsed < TimeSpan.FromSeconds(10), $"10k save took {before.Elapsed}");
    }
    public static byte[] CreatePdf()
    {
        var builder = new PdfDocumentBuilder(); var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var p = 0; p < 2; p++)
        {
            var page = builder.AddPage(250, 250);
            for (var i = 0; i <= 10; i++) { page.DrawLine(new PdfPoint(30 + i * 12, 30), new PdfPoint(30 + i * 12, 150)); page.DrawLine(new PdfPoint(30, 30 + i * 12), new PdfPoint(150, 30 + i * 12)); }
            for (var y = 0; y < 10; y++) for (var x = 0; x < 10; x++) page.AddText("X", 8, new PdfPoint(33 + x * 12, 33 + y * 12), font);
            page.AddText("X   DMC 310 Black", 10, new PdfPoint(30, 190), font);
        }
        return builder.Build();
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
