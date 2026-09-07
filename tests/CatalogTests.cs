using StitchHelper;
using Xunit;

namespace StitchHelper.Tests;

public class CatalogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "stitch-catalog-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CatalogUsesEverySuppliedCodeDescriptionAndRgb()
    {
        var source = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rgb-dmc.json")));
        var catalog = new Repository(root).Catalog();
        Assert.Equal(454, catalog.Count);
        foreach (var row in source.RootElement.EnumerateArray())
        {
            var thread = catalog[row.GetProperty("floss").GetString()!];
            Assert.Equal(row.GetProperty("description").GetString(), thread.Name);
            Assert.Equal($"#{row.GetProperty("r").GetInt32():X2}{row.GetProperty("g").GetInt32():X2}{row.GetProperty("b").GetInt32():X2}", thread.DisplayColor);
        }
        Assert.Equal("#000000", catalog["310"].DisplayColor);
        Assert.Equal("#564A4A", catalog["309"].DisplayColor); // hex column disagrees; RGB wins
        Assert.Equal("#883E43", catalog["221"].DisplayColor); // hex column contains spreadsheet notation
        Assert.DoesNotContain("Blanc", catalog.Keys);
    }

    [Fact]
    public void StartupReplacesStaleCatalogDataAndPreservesInventoryAndCustomThreads()
    {
        var repo = new Repository(root);
        repo.SaveInventory(new("761", 3, "Box 2 / Row 1"));
        repo.AddThread(new("custom-1", "Custom thread", "#123456"));
        using (var db = repo.Open())
        using (var command = db.CreateCommand())
        {
            command.CommandText = "UPDATE catalog SET json=$json WHERE code='761'";
            command.Parameters.AddWithValue("$json", Json.Write(new ThreadEntry("761", "Outdated description", "#111111")));
            command.ExecuteNonQuery();
        }
        repo = new Repository(root);
        Assert.Equal("Salmon Light", repo.Catalog()["761"].Name);
        Assert.Equal("#FFC9C9", repo.Catalog()["761"].DisplayColor);
        Assert.Equal(new InventoryEntry("761", 3, "Box 2 / Row 1"), repo.Inventory().Single());
        Assert.Equal("Custom thread", repo.Catalog()["custom-1"].Name);
    }

    [Fact]
    public void LegacyWhiteAliasPreservesInventoryProjectAndUndoReferences()
    {
        var repo = new Repository(root);
        var pattern = SamplePattern.Create();
        pattern.Data.Definitions[0] = pattern.Data.Definitions[0] with { ThreadCode = "Blanc" };
        var project = repo.Create(pattern, status: "active");
        // Simulate an older database, including old snapshot/history references.
        project.Data.Definitions[0] = project.Data.Definitions[0] with { ThreadCode = "Blanc" };
        project.Substitutions["Blanc"] = "310";
        project.Undo.Add(new() { Kind = "substitute", Code = "Blanc", Replacement = null });
        repo.Save(project, 0);
        using (var db = repo.Open())
        using (var command = db.CreateCommand())
        {
            command.CommandText = "INSERT INTO catalog VALUES('Blanc',$thread); INSERT INTO inventory VALUES('Blanc',$blanc); INSERT INTO inventory VALUES('White',$white)";
            command.Parameters.AddWithValue("$thread", Json.Write(new ThreadEntry("Blanc", "White", "#F7F5ED")));
            command.Parameters.AddWithValue("$blanc", Json.Write(new InventoryEntry("Blanc", 2, "Box 1")));
            command.Parameters.AddWithValue("$white", Json.Write(new InventoryEntry("White", 3, "Box 2")));
            command.ExecuteNonQuery();
        }
        repo = new Repository(root);
        Assert.Equal(new InventoryEntry("White", 5, "Box 1; Box 2"), repo.Inventory().Single());
        Assert.DoesNotContain("Blanc", repo.Catalog().Keys);
        project = repo.Get(project.Id);
        Assert.Equal("White", project.Data.Definitions[0].ThreadCode);
        Assert.Equal("310", project.Substitutions["White"]);
        ProjectCommands.Execute(project, new(project.Revision, "undo"), repo.Catalog());
        Assert.Empty(project.Substitutions);
        Assert.Equal("White", repo.GetPattern(pattern.Id).Data.Definitions[0].ThreadCode);
        // Startup is idempotent; bobbins are not merged again.
        Assert.Equal(5, new Repository(root).Inventory().Single().BobbinCount);
        using var connection = repo.Open(); using var read = connection.CreateCommand();
        read.CommandText = "SELECT json FROM patterns WHERE id=$id"; read.Parameters.AddWithValue("$id", pattern.Id);
        Assert.Contains("Blanc", (string)read.ExecuteScalar()!); // immutable source snapshot is retained
    }

    [Theory]
    [InlineData("[{\"floss\":\"310\",\"description\":\"Black\",\"r\":256,\"g\":0,\"b\":0}]")]
    [InlineData("[{\"floss\":\"310\",\"description\":\"Black\",\"r\":0,\"g\":0,\"b\":0},{\"floss\":\"310\",\"description\":\"Duplicate\",\"r\":1,\"g\":1,\"b\":1}]")]
    [InlineData("[]")]
    public void InvalidCatalogIsRejected(string json)
    {
        Directory.CreateDirectory(root); var path = Path.Combine(root, "bad.json"); File.WriteAllText(path, json);
        Assert.Throws<InvalidDataException>(() => ThreadCatalog.Load(path));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
