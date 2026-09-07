using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace StitchHelper;

public interface IRepository
{
    object Gate { get; }
    string Root { get; }
    SqliteConnection Open();
    Project Get(string id);
    List<Project> List();
    Pattern GetPattern(string id);
    Project Create(Pattern pattern, string? name = null, string status = "audit");
    void Save(Project project, long expectedRevision);
    void Delete(string id);
    Dictionary<string, ThreadEntry> Catalog();
    void AddThread(ThreadEntry entry);
    List<InventoryEntry> Inventory();
    void SaveInventory(InventoryEntry entry);
}

public class Repository : IRepository
{
    public object Gate { get; } = new();
    public string Root { get; }
    public Repository(string root)
    {
        // Validate the entire authoritative asset before touching the database.
        var catalog = ThreadCatalog.Load(Path.Combine(AppContext.BaseDirectory, "rgb-dmc.json"));
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.Combine(Root, "sources"));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT OR IGNORE INTO metadata VALUES('schemaVersion', '1');
            CREATE TABLE IF NOT EXISTS patterns(id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS projects(id TEXT PRIMARY KEY, revision INTEGER NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS catalog(code TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS inventory(code TEXT PRIMARY KEY REFERENCES catalog(code), json TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        command.CommandText = "SELECT value FROM metadata WHERE key='schemaVersion'";
        if (command.ExecuteScalar()?.ToString() != "1") throw new UserError("This database uses a newer schema. Open it with a compatible Stitch Helper version.");
        using var transaction = connection.BeginTransaction();
        foreach (var item in catalog)
        {
            using var seed = connection.CreateCommand();
            seed.Transaction = transaction;
            seed.CommandText = "INSERT INTO catalog VALUES($id,$json) ON CONFLICT(code) DO UPDATE SET json=excluded.json";
            seed.Parameters.AddWithValue("$id", item.Code); seed.Parameters.AddWithValue("$json", Json.Write(item)); seed.ExecuteNonQuery();
        }
        // Retire the old Blanc spelling without losing bobbins or storage notes.
        using var aliases = connection.CreateCommand(); aliases.Transaction = transaction;
        aliases.CommandText = "SELECT json FROM inventory WHERE code IN ('Blanc','White') ORDER BY code";
        List<InventoryEntry> whiteEntries = [];
        using (var reader = aliases.ExecuteReader()) while (reader.Read()) whiteEntries.Add(Json.Read<InventoryEntry>(reader.GetString(0)));
        if (whiteEntries.Any(i => i.Code == "Blanc"))
        {
            var merged = new InventoryEntry("White", whiteEntries.Sum(i => i.BobbinCount), string.Join("; ", whiteEntries.Select(i => i.Location).Where(l => l.Length > 0).Distinct()));
            aliases.CommandText = "DELETE FROM inventory WHERE code='Blanc'; INSERT INTO inventory VALUES('White',$json) ON CONFLICT(code) DO UPDATE SET json=excluded.json";
            aliases.Parameters.AddWithValue("$json", Json.Write(merged)); aliases.ExecuteNonQuery(); aliases.Parameters.Clear();
        }
        aliases.CommandText = "DELETE FROM catalog WHERE code='Blanc'"; aliases.ExecuteNonQuery();
        transaction.Commit();
    }
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(Root, "stitch-helper.db"), ForeignKeys = true, Pooling = false }.ToString());
        connection.Open();
        using var c = connection.CreateCommand(); c.CommandText = "PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;"; c.ExecuteNonQuery();
        return connection;
    }
    private T Get<T>(string table, string id)
    {
        using var connection = Open(); using var c = connection.CreateCommand();
        c.CommandText = $"SELECT json FROM {table} WHERE id=$id"; c.Parameters.AddWithValue("$id", id);
        var value = c.ExecuteScalar() as string ?? throw new UserError("This project or pattern could not be found.", 404);
        return Json.Read<T>(value);
    }
    private List<T> All<T>(string table)
    {
        using var connection = Open(); using var c = connection.CreateCommand(); c.CommandText = $"SELECT json FROM {table}";
        using var reader = c.ExecuteReader(); List<T> result = [];
        while (reader.Read()) result.Add(Json.Read<T>(reader.GetString(0)));
        return result;
    }
    public Project Get(string id) => ThreadCatalog.Normalize(Get<Project>("projects", id));
    public Pattern GetPattern(string id) { var pattern = Get<Pattern>("patterns", id); return pattern with { Data = ThreadCatalog.Normalize(pattern.Data) }; }
    public List<Project> List() => All<Project>("projects").Select(ThreadCatalog.Normalize).OrderByDescending(p => p.UpdatedAt).ToList();
    public Dictionary<string, ThreadEntry> Catalog() => All<ThreadEntry>("catalog").ToDictionary(t => t.Code);
    public List<InventoryEntry> Inventory() => All<InventoryEntry>("inventory");
    public Project Create(Pattern pattern, string? name = null, string status = "audit")
    {
        lock (Gate)
        {
            var project = new Project { PatternId = pattern.Id, Name = name ?? pattern.Name, Data = ThreadCatalog.Normalize(Json.Copy(pattern.Data)), Status = status };
            using var connection = Open(); using var transaction = connection.BeginTransaction();
            using var c = connection.CreateCommand(); c.Transaction = transaction;
            c.CommandText = "INSERT OR IGNORE INTO patterns VALUES($pid,$pattern); INSERT INTO projects VALUES($id,0,$project)";
            c.Parameters.AddWithValue("$pid", pattern.Id); c.Parameters.AddWithValue("$pattern", Json.Write(pattern));
            c.Parameters.AddWithValue("$id", project.Id); c.Parameters.AddWithValue("$project", Json.Write(project));
            c.ExecuteNonQuery(); transaction.Commit(); return project;
        }
    }
    public void Save(Project project, long expectedRevision)
    {
        using var connection = Open(); using var c = connection.CreateCommand();
        c.CommandText = "UPDATE projects SET revision=$revision,json=$json WHERE id=$id AND revision=$expected";
        c.Parameters.AddWithValue("$revision", project.Revision); c.Parameters.AddWithValue("$json", Json.Write(project));
        c.Parameters.AddWithValue("$id", project.Id); c.Parameters.AddWithValue("$expected", expectedRevision);
        if (c.ExecuteNonQuery() != 1) throw new UserError("This project changed in another window. Reload before continuing.", 409);
    }
    public void Delete(string id)
    {
        lock (Gate) { using var connection = Open(); using var c = connection.CreateCommand(); c.CommandText = "DELETE FROM projects WHERE id=$id"; c.Parameters.AddWithValue("$id", id); c.ExecuteNonQuery(); }
    }
    public static void ValidateThread(ThreadEntry t)
    {
        if (!Regex.IsMatch(t.Code, "^[A-Za-z0-9-]{1,24}$") || string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 100 || !Regex.IsMatch(t.DisplayColor, "^#[0-9a-fA-F]{6}$") || t.Brand != "DMC" || t.Family is not ("StrandedCotton" or "Variations")) throw new UserError("Enter a DMC code, name, family, and valid color.");
    }
    public void AddThread(ThreadEntry entry)
    {
        entry = entry with { Code = ThreadCatalog.CanonicalCode(entry.Code) };
        ValidateThread(entry);
        lock (Gate)
        {
            using var connection = Open(); using var c = connection.CreateCommand();
            c.CommandText = "INSERT OR IGNORE INTO catalog VALUES($id,$json)";
            c.Parameters.AddWithValue("$id", entry.Code); c.Parameters.AddWithValue("$json", Json.Write(entry));
            if (c.ExecuteNonQuery() == 0) throw new UserError("That DMC code already exists.");
        }
    }
    public void SaveInventory(InventoryEntry entry)
    {
        entry = entry with { Code = ThreadCatalog.CanonicalCode(entry.Code) };
        if (entry.BobbinCount < 0 || entry.BobbinCount > 10000 || entry.Location.Length > 200 || !Catalog().ContainsKey(entry.Code)) throw new UserError("Use a catalog thread, 0–10,000 whole bobbins, and a location under 200 characters.");
        lock (Gate)
        {
            using var connection = Open(); using var c = connection.CreateCommand();
            c.CommandText = entry.BobbinCount == 0 && entry.Location.Length == 0 ? "DELETE FROM inventory WHERE code=$code" : "INSERT INTO inventory VALUES($code,$json) ON CONFLICT(code) DO UPDATE SET json=$json";
            c.Parameters.AddWithValue("$code", entry.Code); c.Parameters.AddWithValue("$json", Json.Write(entry)); c.ExecuteNonQuery();
        }
    }
}
