using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.RegularExpressions;

namespace StitchHelper;

public interface IRepository
{
    Project Get(string id);
    Project? GetState(string id, long? revision);
    List<Project> List();
    Pattern GetPattern(string id);
    Project Create(Pattern pattern, string? name = null, string status = "audit", PatternSourceAsset? asset = null, bool imported = false);
    Project Execute(string id, Command command);
    Project Progress(string id, ProgressBatch batch);
    void Delete(string id);
    Dictionary<string, ThreadEntry> Catalog();
    List<InventoryEntry> Inventory();
    InventoryEntry SaveInventory(InventoryEntry entry);
    PatternSourceAsset GetSource(string projectId);
}
public record StitchAssignment(string StitchId, bool Complete);
public record ProgressBatch(Guid RequestId, StitchAssignment[] Changes);

public class Repository(StitchDbContext db, ICurrentUserContext current) : IRepository
{
    private Guid Owner => current.UserId;
    private IQueryable<OwnedProject> OwnedProjects => db.Projects.Where(x => x.UserId == Owner);
    private OwnedProject Row(string id, bool forUpdate = false) =>
        (forUpdate
            ? db.Projects.FromSqlInterpolated($"SELECT * FROM \"Projects\" WHERE \"Id\"={id} AND \"UserId\"={Owner} FOR UPDATE")
            : OwnedProjects.Where(x => x.Id == id)).AsNoTracking().FirstOrDefault() ?? throw Missing();
    public static UserError Missing() => new("This item could not be found.", 404);
    public static UserError Conflict() => new("This item changed in another window. Reload the saved version before editing again.", 409);
    private Project Materialize(OwnedProject row)
    {
        var p = Json.Read<Project>(row.StateJson);
        p.Id = row.Id; p.PatternId = row.PatternId; p.Revision = row.Revision; p.DataRevision = row.DataRevision; p.UpdatedAt = row.UpdatedAt;
        p.Data = Json.Read<PatternData>(row.DataJson);
        p.Completed = db.StitchStates.Where(s => s.ProjectId == row.Id).Select(s => s.StitchId).ToHashSet();
        return ThreadCatalog.Normalize(p);
    }
    public Project Get(string id)
    {
        if (db.Database.CurrentTransaction is not null) return Materialize(Row(id));
        using var tx = db.Database.BeginTransaction(IsolationLevel.RepeatableRead);
        var p = Materialize(Row(id)); tx.Commit(); return p;
    }
    public Project? GetState(string id, long? revision)
    {
        using var tx = db.Database.BeginTransaction(IsolationLevel.RepeatableRead);
        var row = OwnedProjects.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Id, x.PatternId, x.Revision, x.DataRevision, x.StateJson, x.UpdatedAt }).SingleOrDefault() ?? throw Missing();
        if (revision == row.Revision) { tx.Commit(); return null; }
        var p = Json.Read<Project>(row.StateJson);
        p.Id = row.Id; p.PatternId = row.PatternId; p.Revision = row.Revision; p.DataRevision = row.DataRevision; p.UpdatedAt = row.UpdatedAt;
        p.Completed = db.StitchStates.Where(x => x.ProjectId == id).Select(x => x.StitchId).ToHashSet();
        tx.Commit(); return p;
    }
    public List<Project> List()
    {
        using var tx = db.Database.BeginTransaction(IsolationLevel.RepeatableRead);
        var list = OwnedProjects.AsNoTracking().OrderByDescending(x => x.UpdatedAt).ToList().Select(Materialize).ToList();
        tx.Commit(); return list;
    }
    public Pattern GetPattern(string id)
    {
        var p = db.Patterns.AsNoTracking().SingleOrDefault(x => x.Id == id && x.UserId == Owner) ?? throw Missing();
        return new(p.Id, p.Name, p.AssetId, ThreadCatalog.Normalize(Json.Read<PatternData>(p.DataJson)));
    }
    public PatternSourceAsset GetSource(string projectId)
    {
        var pattern = GetPattern(Row(projectId).PatternId);
        return db.Assets.AsNoTracking().SingleOrDefault(a => a.Id == pattern.SourceFile && a.UserId == Owner) ?? throw Missing();
    }
    public Dictionary<string, ThreadEntry> Catalog() => db.Catalog.AsNoTracking().Select(x => x.Json).ToList().Select(Json.Read<ThreadEntry>).ToDictionary(t => t.Code);
    public Project Create(Pattern pattern, string? name = null, string status = "audit", PatternSourceAsset? asset = null, bool imported = false)
    {
        using var tx = db.Database.BeginTransaction();
        var existing = db.Patterns.AsNoTracking().SingleOrDefault(x => x.Id == pattern.Id);
        if (existing is not null && existing.UserId != Owner) throw Missing();
        if (existing is null)
        {
            if (asset is not null)
            {
                asset.UserId = Owner; db.Assets.Add(asset);
                pattern = pattern with { SourceFile = asset.Id };
            }
            else if (pattern.SourceFile is not null && !db.Assets.Any(a => a.Id == pattern.SourceFile && a.UserId == Owner)) throw Missing();
            ThreadCatalog.Normalize(pattern.Data);
            db.Patterns.Add(new() { Id = pattern.Id, UserId = Owner, Name = pattern.Name, AssetId = pattern.SourceFile, DataJson = Json.Write(pattern.Data) });
            if (imported) db.Imports.Add(new() { UserId = Owner, PatternId = pattern.Id, Status = pattern.Data.Stitches.Count == 0 ? "unreadable" : "review" });
        }
        else pattern = GetPattern(pattern.Id);
        var project = new Project { PatternId = pattern.Id, Name = name ?? pattern.Name, Status = status, Data = Json.Copy(pattern.Data) };
        db.Projects.Add(new() { Id = project.Id, UserId = Owner, PatternId = pattern.Id, DataJson = Json.Write(project.Data), StateJson = State(project) });
        db.SaveChanges(); tx.Commit(); db.ChangeTracker.Clear(); return project;
    }
    private static string State(Project p) => Json.Write(new { p.Id, p.PatternId, p.Name, p.Status, p.Substitutions, p.Milestones, p.WorkingArea, p.Undo, p.Redo });
    public Project Execute(string id, Command command)
    {
        if (command.Kind == "complete") throw new UserError("Use the stitch progress endpoint.");
        using var tx = db.Database.BeginTransaction();
        var row = Row(id, true); var p = Materialize(row); var before = p.Completed.ToHashSet();
        var historyKind = command.Kind == "undo" ? p.Undo.LastOrDefault()?.Kind : command.Kind == "redo" ? p.Redo.LastOrDefault()?.Kind : null;
        var changesChart = command.Kind is "definition" or "stitch" or "page" or "layout" || historyKind is "definition" or "stitch" or "data" or "layout";
        ProjectCommands.Execute(p, command, Catalog());
        if (changesChart) p.DataRevision++;
        Persist(row, p, before, changesChart);
        tx.Commit(); return p;
    }
    public Project Progress(string id, ProgressBatch batch)
    {
        if (batch.RequestId == Guid.Empty || batch.Changes is null || batch.Changes.Length is < 1 or > 100000) throw new UserError("Submit between 1 and 100,000 stitch states with a request ID.");
        using var tx = db.Database.BeginTransaction();
        var row = Row(id, true); var p = Materialize(row);
        var hash = StorageKeys.Hash(System.Text.Encoding.UTF8.GetBytes(Json.Write(batch.Changes)));
        var prior = db.ProgressMutations.AsNoTracking().SingleOrDefault(x => x.ProjectId == id && x.RequestId == batch.RequestId);
        if (prior is not null)
        {
            if (prior.PayloadHash != hash) throw new UserError("This request ID was already used for different stitch changes.", 409);
            tx.Commit(); return p;
        }
        if (p.Status != "active") throw new UserError("Review and confirm this import before marking progress.");
        var valid = p.Data.Stitches.Select(s => s.Id).ToHashSet();
        if (batch.Changes.Any(c => c is null || !valid.Contains(c.StitchId))) throw new UserError("A selected stitch no longer exists.");
        var before = p.Completed.ToHashSet();
        var final = batch.Changes.GroupBy(c => c.StitchId).Select(g => g.Last()).ToArray();
        var inverse = final.Where(c => before.Contains(c.StitchId) != c.Complete).ToDictionary(c => c.StitchId, c => before.Contains(c.StitchId));
        foreach (var c in final) { if (c.Complete) p.Completed.Add(c.StitchId); else p.Completed.Remove(c.StitchId); }
        if (inverse.Count > 0)
        {
            p.Undo.Add(new() { Kind = "complete", Completion = inverse });
            if (p.Undo.Count > 100) p.Undo.RemoveAt(0);
            p.Redo.Clear();
        }
        foreach (var threshold in new[] { 10, 25, 50, 75, 90, 100 })
            if (p.Data.Stitches.Count > 0 && p.Completed.Count * 100.0 / p.Data.Stitches.Count >= threshold) p.Milestones.Add(threshold);
        p.Revision++; p.UpdatedAt = DateTimeOffset.UtcNow;
        Persist(row, p, before, false);
        db.ProgressMutations.Add(new() { ProjectId = id, RequestId = batch.RequestId, PayloadHash = hash });
        db.SaveChanges(); tx.Commit(); db.ChangeTracker.Clear(); return p;
    }
    private void Persist(OwnedProject row, Project p, HashSet<string> before, bool chartChanged)
    {
        var added = p.Completed.Except(before).ToArray(); var removed = before.Except(p.Completed).ToArray();
        if (removed.Length > 0) db.Database.ExecuteSqlInterpolated($"DELETE FROM \"StitchStates\" WHERE \"ProjectId\"={p.Id} AND \"StitchId\"=ANY({removed})");
        if (added.Length > 0) db.Database.ExecuteSqlInterpolated($"INSERT INTO \"StitchStates\" (\"ProjectId\", \"StitchId\", \"UpdatedAt\") SELECT {p.Id}, unnest({added}), {p.UpdatedAt} ON CONFLICT (\"ProjectId\", \"StitchId\") DO UPDATE SET \"UpdatedAt\"=EXCLUDED.\"UpdatedAt\"");
        var state = State(p);
        var updated = chartChanged
            ? db.Database.ExecuteSqlInterpolated($"UPDATE \"Projects\" SET \"StateJson\"={state}::jsonb, \"DataJson\"={Json.Write(p.Data)}::jsonb, \"Revision\"={p.Revision}, \"DataRevision\"={p.DataRevision}, \"UpdatedAt\"={p.UpdatedAt} WHERE \"Id\"={p.Id} AND \"UserId\"={Owner} AND \"Revision\"={row.Revision}")
            : db.Database.ExecuteSqlInterpolated($"UPDATE \"Projects\" SET \"StateJson\"={state}::jsonb, \"Revision\"={p.Revision}, \"UpdatedAt\"={p.UpdatedAt} WHERE \"Id\"={p.Id} AND \"UserId\"={Owner} AND \"Revision\"={row.Revision}");
        if (updated != 1) throw Conflict();
    }
    public void Delete(string id)
    {
        if (OwnedProjects.Where(x => x.Id == id).ExecuteDelete() == 0) throw Missing();
    }
    public List<InventoryEntry> Inventory() => db.Inventory.AsNoTracking().Where(x => x.UserId == Owner).Select(x => new InventoryEntry(x.Code, x.BobbinCount, x.Location, x.Revision)).ToList();
    public InventoryEntry SaveInventory(InventoryEntry entry)
    {
        entry = entry with { Code = ThreadCatalog.CanonicalCode(entry.Code) };
        if (entry.BobbinCount is < 0 or > 10000 || entry.Location is null || entry.Location.Length > 200 || !db.Catalog.Any(x => x.Code == entry.Code)) throw new UserError("Use a catalog thread, 0–10,000 whole bobbins, and a location under 200 characters.");
        using var tx = db.Database.BeginTransaction();
        db.Database.ExecuteSqlInterpolated($"SELECT pg_advisory_xact_lock(hashtextextended({Owner + ":inventory:" + entry.Code}, 0))");
        var row = db.Inventory.SingleOrDefault(x => x.UserId == Owner && x.Code == entry.Code);
        if ((row?.Revision ?? 0) != entry.Revision) throw Conflict();
        if (row is null) { row = new() { UserId = Owner, Code = entry.Code }; db.Inventory.Add(row); }
        row.BobbinCount = entry.BobbinCount; row.Location = entry.Location; row.Revision++;
        db.SaveChanges(); tx.Commit(); return entry with { Revision = row.Revision };
    }
    public static void ValidateThread(ThreadEntry t)
    {
        if (!Regex.IsMatch(t.Code, "^[A-Za-z0-9-]{1,24}$") || string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 100 || !Regex.IsMatch(t.DisplayColor, "^#[0-9a-fA-F]{6}$") || t.Brand != "DMC" || t.Family is not ("StrandedCotton" or "Variations")) throw new UserError("Enter a DMC code, name, family, and valid color.");
    }
}
