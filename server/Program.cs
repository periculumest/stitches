using StitchHelper;

if (args.Length >= 3 && args[0] == "--restore")
{
    BackupService.Restore(args[1], args[2]);
    Console.WriteLine($"Backup verified and restored to {Path.GetFullPath(args[2])}"); return;
}
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:5057");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 50 * 1024 * 1024);
var root = builder.Configuration["STITCH_DATA_DIR"] ?? Path.Combine(builder.Environment.ContentRootPath, "..", ".data");
builder.Services.AddSingleton<IRepository>(new Repository(root));
builder.Services.AddSingleton<IPatternImporter, PdfImporter>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService(p => p.GetRequiredService<BackupService>());
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o => o.SerializerOptions.PropertyNamingPolicy = Json.Options.PropertyNamingPolicy);
var app = builder.Build();
app.Use(async (context, next) =>
{
    try
    {
        // A local single-user service: reject browser writes from other origins, including DNS rebinding.
        var host = context.Request.Host.Host;
        if (host is not ("127.0.0.1" or "localhost" or "::1")) throw new UserError("Use localhost to open Stitch Helper.", 403);
        if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (origin.Length > 0 && (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Host is not ("127.0.0.1" or "localhost" or "::1") || (uri.Port != context.Request.Host.Port && uri.Port != 5173))) throw new UserError("This request did not come from Stitch Helper.", 403);
        }
        await next();
    }
    catch (Exception ex)
    {
        var status = ex is UserError ue ? ue.Status : ex is BadHttpRequestException ? 400 : 500;
        if (status == 500) app.Logger.LogError(ex, "Request failed");
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error = status == 500 ? "Something went wrong. Your last saved work is safe. Try again or check the server log." : ex.Message });
    }
});
app.UseDefaultFiles(); app.UseStaticFiles();
object View(Project p) => new { p.Id, p.PatternId, p.Name, p.Status, p.Data, p.Completed, p.Substitutions, p.Milestones, p.WorkingArea, p.Revision, p.UpdatedAt, sourceAvailable = app.Services.GetRequiredService<IRepository>().GetPattern(p.PatternId).SourceFile is not null, canUndo = p.Undo.Count > 0, canRedo = p.Redo.Count > 0 };
app.MapGet("/api/health", (BackupService backup) => new { status = "ok", backupError = backup.LastError });
app.MapGet("/api/projects", (IRepository repo) => repo.List().Select(p => new { p.Id, p.Name, p.Status, p.UpdatedAt, width = p.Data.Width, height = p.Data.Height, total = p.Data.Stitches.Count, completed = p.Completed.Count, colors = p.Data.Definitions.Count, preview = p.Data.Stitches.Count <= 15000 ? p.Data : null }));
app.MapGet("/api/projects/{id}", (string id, IRepository repo) => View(repo.Get(id)));
app.MapPost("/api/projects/sample", (IRepository repo, int? size) => View(repo.Create(SamplePattern.Create(Math.Clamp(size ?? 100, 20, 1000)), status: "active")));
app.MapPost("/api/projects/{id}/duplicate", (string id, IRepository repo) => { var original = repo.Get(id); return View(repo.Create(repo.GetPattern(original.PatternId), original.Name + " · new start", "audit")); });
app.MapPost("/api/projects/{id}/commands", (string id, Command command, IRepository repo) =>
{
    lock (repo.Gate) { var p = repo.Get(id); ProjectCommands.Execute(p, command, repo.Catalog()); repo.Save(p, command.Revision); return View(p); }
});
app.MapDelete("/api/projects/{id}", (string id, IRepository repo, BackupService backup) => { backup.Create(); repo.Delete(id); return Results.NoContent(); });
app.MapGet("/api/projects/{id}/source", (string id, IRepository repo) =>
{
    var source = repo.GetPattern(repo.Get(id).PatternId).SourceFile;
    return source is null ? Results.NotFound() : Results.File(Path.Combine(repo.Root, "sources", source), "application/pdf", enableRangeProcessing: true);
});
app.MapPost("/api/imports", async (HttpRequest request, IRepository repo, IPatternImporter importer) =>
{
    var form = await request.ReadFormAsync(); var file = form.Files.GetFile("file") ?? throw new UserError("Choose a PDF to import.");
    if (file.Length == 0 || file.Length > 45 * 1024 * 1024 || !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new UserError("Choose a PDF smaller than 45 MB.");
    var id = Guid.NewGuid().ToString("N"); var name = id + ".pdf"; var path = Path.Combine(repo.Root, "sources", name);
    // Retention and candidate persistence share the backup lock, keeping every backup self-contained.
    using var memory = new MemoryStream(); await file.CopyToAsync(memory);
    if (!System.Text.Encoding.ASCII.GetString(memory.ToArray().Take(1024).ToArray()).Contains("%PDF-")) throw new UserError("This file does not appear to be a PDF.");
    lock (repo.Gate)
    {
        File.WriteAllBytes(path, memory.ToArray());
        PatternData data;
        try { data = importer.Parse(path, repo.Catalog()); }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "PDF import failed for {SourceId}", id);
            data = new() { Warnings = [new(ex is UserError ? ex.Message : "This PDF could not be read. It may be encrypted, damaged, or use an unsupported format. The original is retained.")] };
            File.WriteAllText(Path.Combine(repo.Root, "sources", id + ".diagnostic.txt"), ex.ToString());
        }
        return View(repo.Create(new(id, Path.GetFileNameWithoutExtension(file.FileName)[..Math.Min(160, Path.GetFileNameWithoutExtension(file.FileName).Length)], name, data)));
    }
}).DisableAntiforgery();
app.MapGet("/api/catalog", (IRepository repo) => repo.Catalog().Values);
app.MapPost("/api/catalog", (ThreadEntry entry, IRepository repo) => { repo.AddThread(entry); return Results.Ok(entry); });
app.MapGet("/api/inventory", (IRepository repo) => repo.Inventory());
app.MapPut("/api/inventory/{code}", (string code, InventoryEntry entry, IRepository repo) => { if (code != entry.Code) throw new UserError("Thread codes do not match."); repo.SaveInventory(entry); return Results.Ok(entry); });
app.MapGet("/api/backups", (BackupService backup) => backup.List());
app.MapPost("/api/backups", (BackupService backup) => new { name = backup.Create() });
app.MapGet("/api/backups/{name}", (string name, IRepository repo) =>
{
    if (Path.GetFileName(name) != name || !name.EndsWith(".zip")) throw new UserError("Invalid backup name.");
    var path = Path.Combine(repo.Root, "backups", name);
    return File.Exists(path) ? Results.File(path, "application/zip", name) : Results.NotFound();
});
app.MapFallbackToFile("index.html");
app.Run();
public partial class Program { }
