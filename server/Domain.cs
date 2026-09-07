using System.Text.Json;

namespace StitchHelper;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)!;
    public static T Copy<T>(T value) => Read<T>(Write(value));
}
public record ThreadEntry(string Code, string Name, string DisplayColor, string Brand = "DMC", string Family = "StrandedCotton");
public record SymbolGlyph(string Path, double MinX, double MinY, double Width, double Height);
public record StitchDefinition(string Id, string Symbol, string ThreadCode, string StitchType = "FullCross", SymbolGlyph? SymbolGlyph = null);
public record Stitch(string Id, double X, double Y, string DefinitionId, double? EndX = null, double? EndY = null);
public record SourcePage(int Number, int X, int Y, int Width, int Height);
public record ImportWarning(string Message, int? Page = null, double? X = null, double? Y = null);
public record Region(int MinX, int MinY, int MaxX, int MaxY);
public class PatternData
{
    public int Width { get; set; }
    public int Height { get; set; }
    public List<StitchDefinition> Definitions { get; set; } = [];
    public List<Stitch> Stitches { get; set; } = [];
    public List<SourcePage> Pages { get; set; } = [];
    public List<ImportWarning> Warnings { get; set; } = [];
}
public record Pattern(string Id, string Name, string? SourceFile, PatternData Data);
public class Project
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PatternId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "audit";
    public PatternData Data { get; set; } = new();
    public HashSet<string> Completed { get; set; } = [];
    public Dictionary<string, string> Substitutions { get; set; } = [];
    public HashSet<int> Milestones { get; set; } = [];
    public Region? WorkingArea { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Change> Undo { get; set; } = [];
    public List<Change> Redo { get; set; } = [];
}
public record InventoryEntry(string Code, int BobbinCount, string Location);
public class Change
{
    public string Kind { get; set; } = "";
    public Dictionary<string, bool>? Completion { get; set; }
    public string? Code { get; set; }
    public string? Replacement { get; set; }
    public StitchDefinition? Definition { get; set; }
    public Stitch? Stitch { get; set; }
    public string? StitchId { get; set; }
    public PatternData? Data { get; set; }
}
public record Command(long Revision, string Kind, string[]? StitchIds = null, bool Complete = true,
    string? Code = null, string? Replacement = null, StitchDefinition? Definition = null,
    Stitch? Stitch = null, string? StitchId = null, Region? Area = null, string? Name = null,
    int? PageNumber = null, int X = 0, int Y = 0);
public class UserError(string message, int status = 400) : Exception(message) { public int Status { get; } = status; }

public static class ProjectCommands
{
    public static readonly string[] StitchTypes = ["FullCross", "HalfCross", "QuarterCross", "ThreeQuarterCross", "Backstitch", "FrenchKnot", "Bead", "Other"];
    public static void Execute(Project project, Command command, IReadOnlyDictionary<string, ThreadEntry> catalog)
    {
        if (command.Revision != project.Revision) throw new UserError("This project changed in another window. Reload to use the saved version.", 409);
        Change? change = null;
        switch (command.Kind)
        {
            case "complete":
                if (project.Status != "active") throw new UserError("Review and confirm this import before marking progress.");
                var valid = project.Data.Stitches.Select(s => s.Id).ToHashSet();
                var ids = command.StitchIds ?? [];
                if (ids.Any(id => !valid.Contains(id))) throw new UserError("A selected stitch no longer exists.");
                change = new() { Kind = "complete", Completion = ids.Distinct().Where(id => project.Completed.Contains(id) != command.Complete).ToDictionary(id => id, _ => command.Complete) };
                if (change.Completion.Count == 0) return;
                break;
            case "substitute":
                if (command.Code is null || !project.Data.Definitions.Any(d => d.ThreadCode == command.Code)) throw new UserError("Select a source thread.");
                if (command.Replacement is not null && !catalog.ContainsKey(command.Replacement)) throw new UserError("Choose a thread from the catalog.");
                change = new() { Kind = "substitute", Code = command.Code, Replacement = command.Replacement == command.Code ? null : command.Replacement };
                break;
            case "definition":
                var definition = command.Definition ?? throw new UserError("A stitch definition is required.");
                if (string.IsNullOrWhiteSpace(definition.Symbol) || definition.Symbol.Length > 4 || !catalog.ContainsKey(definition.ThreadCode) || !StitchTypes.Contains(definition.StitchType)) throw new UserError("Choose a symbol, catalog thread, and stitch type.");
                if (!project.Data.Definitions.Any(d => d.Id == definition.Id)) throw new UserError("Unknown stitch definition.");
                var originalDefinition = project.Data.Definitions.First(d => d.Id == definition.Id);
                definition = definition with { SymbolGlyph = originalDefinition.Symbol == definition.Symbol ? originalDefinition.SymbolGlyph : null };
                change = new() { Kind = "definition", Definition = definition };
                break;
            case "stitch":
                var stitch = command.Stitch;
                if (stitch is not null) ValidateStitch(stitch, project.Data);
                if (stitch is not null && project.Data.Stitches.Any(s => s.Id != stitch.Id && s.X == stitch.X && s.Y == stitch.Y && s.EndX == stitch.EndX && s.EndY == stitch.EndY)) throw new UserError("A stitch already occupies this position. Select it to change its definition.");
                var stitchId = stitch?.Id ?? command.StitchId ?? throw new UserError("Select a stitch.");
                change = new() { Kind = "stitch", StitchId = stitchId, Stitch = stitch };
                break;
            case "page":
                var page = project.Data.Pages.FirstOrDefault(p => p.Number == command.PageNumber) ?? throw new UserError("Unknown page.");
                if (command.X < 0 || command.Y < 0 || command.X + page.Width > 5000 || command.Y + page.Height > 5000) throw new UserError("Page offsets must stay within 5,000 × 5,000.");
                var data = Json.Copy(project.Data);
                var dx = command.X - page.X; var dy = command.Y - page.Y;
                data.Stitches = data.Stitches.Select(s => s.Id.StartsWith($"p{page.Number}-", StringComparison.Ordinal) ? s with { X = s.X + dx, Y = s.Y + dy, EndX = s.EndX + dx, EndY = s.EndY + dy } : s).ToList();
                data.Pages = data.Pages.Select(p => p.Number == page.Number ? p with { X = command.X, Y = command.Y } : p).ToList();
                data.Width = data.Pages.Max(p => p.X + p.Width); data.Height = data.Pages.Max(p => p.Y + p.Height);
                change = new() { Kind = "data", Data = data };
                break;
            case "undo":
            case "redo":
                var from = command.Kind == "undo" ? project.Undo : project.Redo;
                var to = command.Kind == "undo" ? project.Redo : project.Undo;
                if (from.Count == 0) return;
                var operation = from[^1]; from.RemoveAt(from.Count - 1);
                to.Add(Apply(project, operation));
                break;
            case "area":
                if (command.Area is { } area && (area.MinX < 0 || area.MinY < 0 || area.MaxX < area.MinX || area.MaxY < area.MinY || area.MaxX >= project.Data.Width || area.MaxY >= project.Data.Height)) throw new UserError("Working area is outside the pattern.");
                project.WorkingArea = command.Area; break;
            case "confirm":
                if (project.Data.Stitches.Count == 0) throw new UserError("No readable stitches were detected. Try another PDF.");
                if (project.Data.Definitions.Any(d => !catalog.ContainsKey(d.ThreadCode))) throw new UserError("Assign a catalog thread to each symbol before starting.");
                if (project.Data.Stitches.GroupBy(s => (s.X, s.Y, s.EndX, s.EndY)).Any(g => g.Count() > 1)) throw new UserError("Overlapping stitches need correction before starting. Check the page offsets.");
                project.Status = "active"; break;
            case "rename":
                if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Length > 160) throw new UserError("Use a project name between 1 and 160 characters.");
                project.Name = command.Name.Trim(); break;
            default: throw new UserError("Unknown project action.");
        }
        if (change is not null)
        {
            project.Undo.Add(Apply(project, change));
            project.Redo.Clear();
            if (project.Undo.Count > 100) project.Undo.RemoveAt(0);
        }
        if (project.Status == "active" && project.Data.Stitches.Count > 0)
            foreach (var threshold in new[] { 10, 25, 50, 75, 90, 100 })
                if (project.Completed.Count * 100.0 / project.Data.Stitches.Count >= threshold) project.Milestones.Add(threshold);
        project.Revision++;
        project.UpdatedAt = DateTimeOffset.UtcNow;
    }
    public static void ValidateStitch(Stitch s, PatternData data)
    {
        if (string.IsNullOrWhiteSpace(s.Id) || s.Id.Length > 100 || !double.IsFinite(s.X) || !double.IsFinite(s.Y) || s.X < 0 || s.Y < 0 || s.X >= data.Width || s.Y >= data.Height || !data.Definitions.Any(d => d.Id == s.DefinitionId)) throw new UserError("Invalid stitch position or definition.");
        if (s.EndX.HasValue != s.EndY.HasValue || (s.EndX is { } x && (!double.IsFinite(x) || x < 0 || x > data.Width)) || (s.EndY is { } y && (!double.IsFinite(y) || y < 0 || y > data.Height))) throw new UserError("Invalid stitch endpoint.");
    }
    private static Change Apply(Project p, Change c)
    {
        switch (c.Kind)
        {
            case "complete":
                var inverse = c.Completion!.Keys.ToDictionary(id => id, id => p.Completed.Contains(id));
                foreach (var (id, complete) in c.Completion) { if (complete) p.Completed.Add(id); else p.Completed.Remove(id); }
                return new() { Kind = c.Kind, Completion = inverse };
            case "substitute":
                var previous = p.Substitutions.GetValueOrDefault(c.Code!);
                if (c.Replacement is null) p.Substitutions.Remove(c.Code!); else p.Substitutions[c.Code!] = c.Replacement;
                return new() { Kind = c.Kind, Code = c.Code, Replacement = previous };
            case "definition":
                var index = p.Data.Definitions.FindIndex(d => d.Id == c.Definition!.Id);
                var original = p.Data.Definitions[index]; p.Data.Definitions[index] = c.Definition!;
                return new() { Kind = c.Kind, Definition = original };
            case "stitch":
                var old = p.Data.Stitches.FirstOrDefault(s => s.Id == c.StitchId);
                var wasComplete = p.Completed.Contains(c.StitchId!);
                p.Data.Stitches.RemoveAll(s => s.Id == c.StitchId);
                if (c.Stitch is not null) p.Data.Stitches.Add(c.Stitch);
                if (c.Stitch is null) p.Completed.Remove(c.StitchId!);
                else if (c.Completion?.GetValueOrDefault(c.StitchId!) == true) p.Completed.Add(c.StitchId!);
                return new() { Kind = c.Kind, StitchId = c.StitchId, Stitch = old, Completion = new() { [c.StitchId!] = wasComplete } };
            case "data":
                var before = p.Data; p.Data = c.Data!; return new() { Kind = c.Kind, Data = before };
            default: throw new InvalidOperationException("Invalid history operation.");
        }
    }
}
