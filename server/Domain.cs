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
public record ThreadUsageComponent(string Id, string ThreadCode, int? StrandCount = null);
public record StitchDefinition(string Id, string Symbol, string ThreadCode, string StitchType = "FullCross", SymbolGlyph? SymbolGlyph = null,
    List<ThreadUsageComponent>? Components = null)
{
    public string UsageKind => Components?.Count > 1 ? "Blend" : "Single";
    public List<ThreadUsageComponent> GetComponents() => Components ?? [new(Id + "-c0", ThreadCode)];
}
public record Stitch(string Id, double X, double Y, string DefinitionId, double? EndX = null, double? EndY = null);
public record SourcePage(int Number, int X, int Y, int Width, int Height);
public record PagePlacement(int Number, int X, int Y);
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
    // Retain only the source instances hidden by overlap deduplication, not a second full chart.
    public List<Stitch> PageOverlapStitches { get; set; } = [];
    public bool PageSourcesComplete { get; set; }
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
    public long DataRevision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Change> Undo { get; set; } = [];
    public List<Change> Redo { get; set; } = [];
}
public record InventoryEntry(string Code, int BobbinCount, string Location, long Revision = 0);
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
    int? PageNumber = null, int X = 0, int Y = 0, List<PagePlacement>? Pages = null);
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
                var component = project.Data.Definitions.SelectMany(d => d.GetComponents()).FirstOrDefault(c => c.Id == command.Code) ?? throw new UserError("Select a thread component.");
                if (command.Replacement is not null && !catalog.ContainsKey(command.Replacement)) throw new UserError("Choose a thread from the catalog.");
                change = new() { Kind = "substitute", Code = component.Id, Replacement = command.Replacement == component.ThreadCode ? null : command.Replacement };
                break;
            case "definition":
                var definition = command.Definition ?? throw new UserError("A stitch definition is required.");
                if (string.IsNullOrWhiteSpace(definition.Symbol) || definition.Symbol.Length > 4 || !StitchTypes.Contains(definition.StitchType)) throw new UserError("Choose a symbol, catalog thread, and stitch type.");
                ValidateComponents(definition, catalog);
                if (project.Data.Definitions.Where(d => d.Id != definition.Id).SelectMany(d => d.GetComponents()).Any(c => definition.GetComponents().Any(n => n.Id == c.Id))) throw new UserError("Thread component IDs must be unique in this pattern.");
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
                if (project.Status == "audit" && project.Data.PageOverlapStitches.Count > 0)
                {
                    var edited = Json.Copy(project.Data);
                    var oldStitch = edited.Stitches.FirstOrDefault(s => s.Id == stitchId);
                    edited.Stitches.RemoveAll(s => s.Id == stitchId);
                    if (stitch is not null) edited.Stitches.Add(stitch);
                    if (oldStitch is not null)
                        edited.PageOverlapStitches = edited.PageOverlapStitches.Select(s => s.X == oldStitch.X && s.Y == oldStitch.Y && s.EndX == oldStitch.EndX && s.EndY == oldStitch.EndY ? stitch is null ? null : stitch with { Id = s.Id } : s).OfType<Stitch>().ToList();
                    change = new() { Kind = "data", Data = edited };
                    break;
                }
                change = new() { Kind = "stitch", StitchId = stitchId, Stitch = stitch };
                break;
            case "page":
                var page = project.Data.Pages.FirstOrDefault(p => p.Number == command.PageNumber) ?? throw new UserError("Unknown page.");
                if (project.Status == "audit")
                {
                    change = new() { Kind = "layout", Data = PageLayout.Arrange(project.Data, project.Data.Pages.Select(p => new PagePlacement(p.Number, p.Number == page.Number ? command.X : p.X, p.Number == page.Number ? command.Y : p.Y)).ToList()) };
                    break;
                }
                if (command.X < 0 || command.Y < 0 || command.X + page.Width > 5000 || command.Y + page.Height > 5000) throw new UserError("Page offsets must stay within 5,000 × 5,000.");
                var data = Json.Copy(project.Data);
                var dx = command.X - page.X; var dy = command.Y - page.Y;
                data.Stitches = data.Stitches.Select(s => s.Id.StartsWith($"p{page.Number}-", StringComparison.Ordinal) ? s with { X = s.X + dx, Y = s.Y + dy, EndX = s.EndX + dx, EndY = s.EndY + dy } : s).ToList();
                data.Pages = data.Pages.Select(p => p.Number == page.Number ? p with { X = command.X, Y = command.Y } : p).ToList();
                data.Width = data.Pages.Max(p => p.X + p.Width); data.Height = data.Pages.Max(p => p.Y + p.Height);
                change = new() { Kind = "data", Data = data };
                break;
            case "layout":
                if (project.Status != "audit" || project.Completed.Count > 0) throw new UserError("Page arrangement is available during import review, before stitching begins.");
                change = new() { Kind = "layout", Data = PageLayout.Arrange(project.Data, command.Pages) };
                break;
            case "undo":
            case "redo":
                var from = command.Kind == "undo" ? project.Undo : project.Redo;
                var to = command.Kind == "undo" ? project.Redo : project.Undo;
                if (from.Count == 0) return;
                if (from[^1].Kind == "layout" && project.Completed.Count > 0) throw new UserError("Undo stitch completion before undoing a page arrangement, so your progress stays attached to the correct stitches.");
                var operation = from[^1]; from.RemoveAt(from.Count - 1);
                to.Add(Apply(project, operation));
                break;
            case "area":
                if (command.Area is { } area && (area.MinX < 0 || area.MinY < 0 || area.MaxX < area.MinX || area.MaxY < area.MinY || area.MaxX >= project.Data.Width || area.MaxY >= project.Data.Height)) throw new UserError("Working area is outside the pattern.");
                project.WorkingArea = command.Area; break;
            case "confirm":
                if (project.Data.Stitches.Count == 0) throw new UserError("No readable stitches were detected. Try another PDF.");
                foreach (var d in project.Data.Definitions) ValidateComponents(d, catalog);
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
    public static void ValidateComponents(StitchDefinition d, IReadOnlyDictionary<string, ThreadEntry> catalog)
    {
        var components = d.GetComponents();
        if (components.Count is < 1 or > 12 || components.Any(c => c is null || string.IsNullOrWhiteSpace(c.Id) || c.Id.Length > 100 || !catalog.ContainsKey(c.ThreadCode) || c.StrandCount is <= 0) || components.Select(c => c.Id).Distinct().Count() != components.Count || components.Select(c => c.ThreadCode).Distinct().Count() != components.Count)
            throw new UserError("Use one or more distinct catalog threads. Strand counts must be blank or positive whole numbers.");
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
            case "layout":
            case "data":
                var before = p.Data; p.Data = c.Data!; return new() { Kind = c.Kind, Data = before };
            default: throw new InvalidOperationException("Invalid history operation.");
        }
    }
}
