using System.Security.Cryptography.X509Certificates;
using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StitchHelper;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole();
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning); // OAuth callback query values are private.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:5057");
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 50 * 1024 * 1024);
var configuration = builder.Configuration;
var maxUploadMb = configuration.GetValue("Import:MaxMegabytes", 45);
if (maxUploadMb is < 1 or > 45) throw new InvalidOperationException("Import:MaxMegabytes must be between 1 and 45.");
string Required(string key) => !string.IsNullOrWhiteSpace(configuration[key]) ? configuration[key]! : throw new InvalidOperationException($"Required configuration is missing: {key}");
var connection = Required("ConnectionStrings:StitchHelper");
builder.Services.AddDbContext<StitchDbContext>(o => o.UseNpgsql(connection));
if (args.Contains("--migrate"))
{
    await using var db = new StitchDbContext(new DbContextOptionsBuilder<StitchDbContext>().UseNpgsql(connection).Options);
    await db.Database.OpenConnectionAsync();
    await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(81764001)");
    try { await db.Database.MigrateAsync(); db.SeedCatalog(); }
    finally { await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(81764001)"); await db.Database.CloseConnectionAsync(); }
    Console.WriteLine("Migrations and reference catalog applied."); return;
}
var local = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
var publicUrl = configuration["PublicBaseUrl"];
Uri? publicOrigin = null;
if (publicUrl is not null && (!Uri.TryCreate(publicUrl, UriKind.Absolute, out publicOrigin) || publicOrigin.AbsolutePath != "/" || publicOrigin.Query != "" || publicOrigin.Fragment != "" || publicOrigin.UserInfo != "")) throw new InvalidOperationException("PublicBaseUrl must be an absolute origin without a path, query, or credentials.");
if (!local && publicOrigin?.Scheme != "https") throw new InvalidOperationException("Production requires an HTTPS PublicBaseUrl.");
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddScoped<IRepository, Repository>();
builder.Services.AddSingleton<IPatternImporter, PdfImporter>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<RetentionService>();
builder.Services.AddScoped<LegalDocumentService>();
var betaOptions = new BetaOptions(); configuration.GetSection("Beta").Bind(betaOptions); betaOptions.Validate();
builder.Services.AddSingleton(betaOptions);
builder.Services.AddScoped<BetaService>();
builder.Services.AddHostedService<RetentionWorker>();
builder.Services.AddScoped<GoogleIdentityResolver>();
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(o => { o.User.RequireUniqueEmail = false; })
    .AddEntityFrameworkStores<StitchDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = local ? "StitchHelper.Session" : "__Host-StitchHelper.Session";
    o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = local ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Lax; o.Cookie.Path = "/";
    o.ExpireTimeSpan = TimeSpan.FromDays(30); o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
var googleId = configuration["Authentication:Google:ClientId"];
if (!local) { Required("Authentication:Google:ClientId"); Required("Authentication:Google:ClientSecret"); }
if (!string.IsNullOrWhiteSpace(googleId)) builder.Services.AddAuthentication().AddGoogle(o =>
{
    o.ClientId = googleId; o.ClientSecret = Required("Authentication:Google:ClientSecret");
    o.SignInScheme = IdentityConstants.ExternalScheme; o.SaveTokens = false; o.CallbackPath = "/signin-google";
    o.Events.OnRemoteFailure = c => { c.HandleResponse(); c.Response.Redirect("/?signin=failed"); return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = local ? "StitchHelper.Csrf" : "__Host-StitchHelper.Csrf";
    o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = local ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
});
var protection = builder.Services.AddDataProtection().SetApplicationName("StitchHelper").PersistKeysToDbContext<StitchDbContext>();
var certificate = configuration["DataProtection:CertificateBase64"];
if (!local && string.IsNullOrWhiteSpace(certificate)) throw new InvalidOperationException("Production requires DataProtection:CertificateBase64 to encrypt the shared key ring.");
if (!string.IsNullOrWhiteSpace(certificate))
{
    var cert = new X509Certificate2(Convert.FromBase64String(certificate), configuration["DataProtection:CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet);
    if (!cert.HasPrivateKey) throw new InvalidOperationException("Data protection requires a certificate with its private key.");
    protection.ProtectKeysWithCertificate(cert);
}
var storage = configuration["Storage:Provider"] ?? (local ? "Local" : "");
if (storage == "Local" && local)
{
    var root = Path.GetFullPath(configuration["Storage:LocalRoot"] ?? Path.Combine(builder.Environment.ContentRootPath, "..", ".data", "private"));
    var webRoot = Path.GetFullPath(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));
    if (root.Equals(webRoot, StringComparison.OrdinalIgnoreCase) || root.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Private storage must be outside wwwroot.");
    builder.Services.AddSingleton<IPatternAssetStore>(new LocalPatternAssetStore(root));
    builder.Services.AddSingleton<IBackupArtifactStore>(new LocalBackupArtifactStore(root));
}
else if (storage == "GoogleCloudStorage")
{
    var patternBucket = Required("Storage:PatternBucketOrContainer"); var backupBucket = Required("Storage:BackupBucketOrContainer");
    builder.Services.AddSingleton(_ => StorageClient.Create()); // Application Default Credentials / workload identity.
    builder.Services.AddSingleton<IPatternAssetStore>(s => new GooglePatternAssetStore(s.GetRequiredService<StorageClient>(), patternBucket));
    builder.Services.AddSingleton<IBackupArtifactStore>(s => new GoogleBackupArtifactStore(s.GetRequiredService<StorageClient>(), backupBucket));
}
else throw new InvalidOperationException("Production requires Storage:Provider=GoogleCloudStorage and durable private buckets. Local storage is Development/Testing only.");
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o => o.SerializerOptions.PropertyNamingPolicy = Json.Options.PropertyNamingPolicy);
var app = builder.Build();
if (args.Contains("--beta-admin") || args.Contains("--remove-beta-admin"))
{
    var remove = args.Contains("--remove-beta-admin");
    var identifier = args.ElementAtOrDefault(Array.IndexOf(args, remove ? "--remove-beta-admin" : "--beta-admin") + 1);
    if (!Guid.TryParse(identifier, out var id)) throw new InvalidOperationException("Specify an existing internal account GUID.");
    using var scope = app.Services.CreateScope();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    var user = await users.FindByIdAsync(id.ToString()) ?? throw new InvalidOperationException("Account does not exist.");
    if (!await roles.RoleExistsAsync(BetaService.AdminRole))
        if (!(await roles.CreateAsync(new IdentityRole<Guid>(BetaService.AdminRole))).Succeeded) throw new InvalidOperationException("Could not create administrator role.");
    if (await users.IsInRoleAsync(user, BetaService.AdminRole) != !remove)
    {
        var result = remove ? await users.RemoveFromRoleAsync(user, BetaService.AdminRole) : await users.AddToRoleAsync(user, BetaService.AdminRole);
        if (!result.Succeeded) throw new InvalidOperationException("Could not update administrator role.");
    }
    Console.WriteLine($"Beta administrator access {(remove ? "removed" : "assigned")} for {id}."); return;
}
if (args.Contains("--legal-editor") || args.Contains("--remove-legal-editor"))
{
    var remove = args.Contains("--remove-legal-editor");
    var identifier = args.ElementAtOrDefault(Array.IndexOf(args, remove ? "--remove-legal-editor" : "--legal-editor") + 1);
    if (string.IsNullOrWhiteSpace(identifier)) throw new InvalidOperationException("Specify an existing account ID or exact email address.");
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<StitchDbContext>();
    var byId = Guid.TryParse(identifier, out var id); var email = identifier.ToUpperInvariant();
    var matches = await db.Users.Where(u => byId ? u.Id == id : u.NormalizedEmail == email).ToListAsync();
    if (matches.Count != 1) throw new InvalidOperationException("Expected exactly one existing account. Use an account ID if the email is shared.");
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    if (!await roles.RoleExistsAsync(LegalDocumentService.EditorRole))
    {
        var created = await roles.CreateAsync(new IdentityRole<Guid>(LegalDocumentService.EditorRole));
        if (!created.Succeeded) throw new InvalidOperationException("Could not create editor role.");
    }
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var assigned = await users.IsInRoleAsync(matches[0], LegalDocumentService.EditorRole);
    if (assigned != !remove)
    {
        var changed = remove ? await users.RemoveFromRoleAsync(matches[0], LegalDocumentService.EditorRole) : await users.AddToRoleAsync(matches[0], LegalDocumentService.EditorRole);
        if (!changed.Succeeded) throw new InvalidOperationException("Could not update editor access.");
    }
    Console.WriteLine($"Legal editor access {(remove ? "removed" : "assigned")} for account {matches[0].Id}."); return;
}
if (storage == "GoogleCloudStorage")
{
    var client = app.Services.GetRequiredService<StorageClient>();
    foreach (var bucketName in new[] { Required("Storage:PatternBucketOrContainer"), Required("Storage:BackupBucketOrContainer") }.Distinct())
    {
        var bucket = await client.GetBucketAsync(bucketName);
        if (bucket.IamConfiguration?.PublicAccessPrevention != "enforced" || bucket.IamConfiguration?.UniformBucketLevelAccess?.Enabled != true)
            throw new InvalidOperationException("Private storage requires enforced public access prevention and uniform bucket-level access on both configured buckets.");
        if (bucket.Versioning?.Enabled == true || bucket.DefaultEventBasedHold == true || bucket.RetentionPolicy?.RetentionPeriod > 0 || bucket.SoftDeletePolicy?.RetentionDurationSeconds > 604800)
            throw new InvalidOperationException("Deletion policy requires bucket versioning and default holds disabled, no bucket retention lock, and soft-delete recovery of at most 7 days. Review existing object holds separately.");
    }
}
if (args.Contains("--backup"))
{
    var position = Array.IndexOf(args, "--backup");
    var kind = args.ElementAtOrDefault(position + 1);
    if (kind is not ("daily" or "weekly")) throw new InvalidOperationException("Usage: --backup daily|weekly");
    using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<StitchDbContext>();
    var owners = await db.Users.Select(x => x.Id).ToListAsync(); var failed = false;
    foreach (var owner in owners)
    {
        using var jobScope = app.Services.CreateScope();
        try { await jobScope.ServiceProvider.GetRequiredService<BackupService>().Retain(owner, kind, DateTimeOffset.UtcNow); }
        catch (Exception ex) { failed = true; app.Logger.LogError(ex, "Backup {Kind} failed for user {UserId}", kind, owner); }
    }
    Environment.ExitCode = failed ? 1 : 0; return;
}
if (args.Contains("--cleanup") || args.Contains("--retention-status"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<StitchDbContext>();
    Console.WriteLine($"Retention status: {await db.Projects.CountAsync()} projects, {await db.Patterns.CountAsync(p => !db.Projects.Any(j => j.PatternId == p.Id))} unreferenced patterns, {await db.Assets.CountAsync(a => !db.Patterns.Any(p => p.AssetId == a.Id))} unreferenced source records, {await db.PendingObjectDeletions.CountAsync()} pending object deletions.");
    if (args.Contains("--cleanup")) await scope.ServiceProvider.GetRequiredService<RetentionService>().Maintain(CancellationToken.None);
    var overdue = await db.PendingObjectDeletions.CountAsync(d => d.QueuedAt < DateTimeOffset.UtcNow.AddHours(-24) && d.Attempts > 0);
    Console.WriteLine($"Pending object deletions: {await db.PendingObjectDeletions.CountAsync()}; overdue attempted deletions: {overdue}.");
    Environment.ExitCode = overdue > 0 ? 1 : 0; return;
}
// One canonical public origin makes redirects safe behind any HTTPS ingress, without trusting forwarded headers.
app.Use(async (context, next) =>
{
    if (publicOrigin is not null) { context.Request.Scheme = publicOrigin.Scheme; context.Request.Host = new HostString(publicOrigin.Authority); }
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    if (!local) context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000";
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/auth")) context.Response.Headers.CacheControl = "no-store";
    try
    {
        await next();
        if (context.Request.Path.StartsWithSegments("/api") && context.Response.StatusCode >= 400 && !context.Response.HasStarted && context.Response.ContentType is null)
            await BetaErrors.Write(context, context.Response.StatusCode);
    }
    catch (Exception ex) when (!context.Response.HasStarted)
    {
        var status = ex is UserError error ? error.Status : ex is AntiforgeryValidationException ? 400 : ex is DbUpdateConcurrencyException ? 409 : ex is BadHttpRequestException ? 400 : 500;
        await BetaErrors.Write(context, status, ex is UserError ? ex.Message : ex is AntiforgeryValidationException ? "Your session verification expired. Reload and try again." : null);
    }
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.UseAuthentication(); app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true && (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/auth")))
    {
        var current = context.RequestServices.GetRequiredService<ICurrentUserContext>();
        var db = context.RequestServices.GetRequiredService<StitchDbContext>();
        if (!await db.Users.AnyAsync(u => u.Id == current.UserId, context.RequestAborted))
        {
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            context.Response.StatusCode = 401; return;
        }
    }
    if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS") &&
        (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/auth")))
    {
        if (context.User.Identity?.IsAuthenticated != true) { context.Response.StatusCode = 401; return; }
        if (context.Request.Headers.TryGetValue("X-Account-Id", out var expectedAccount) && expectedAccount != context.RequestServices.GetRequiredService<ICurrentUserContext>().UserId.ToString())
            throw new UserError("A different account is signed in. Return to the original account before retrying work from this tab.", 409);
        await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
    }
    if (context.User.Identity?.IsAuthenticated == true && context.Request.Path.StartsWithSegments("/api") && context.Request.Path != "/api/beta/public" && !LegalEndpoints.ExemptFromAcceptance(context.Request))
    {
        var user = context.RequestServices.GetRequiredService<ICurrentUserContext>();
        if (context.Request.Path != "/api/beta/state" && context.Request.Path != "/api/beta/public")
            await context.RequestServices.GetRequiredService<BetaService>().RequireAccess(user.UserId);
        if (await context.RequestServices.GetRequiredService<LegalDocumentService>().HasPending(user.UserId, context.RequestAborted))
            throw new UserError("Review and accept the updated legal documents to continue. Your saved work remains available after acceptance.", 428);
    }
    await next();
});
app.MapStitchAuthentication();
app.MapLegalDocuments();
app.MapBeta();
app.MapGet("/health/live", () => new { status = "live" }).AllowAnonymous();
app.MapGet("/health/ready", async (StitchDbContext db) =>
{
    try { return await db.Database.CanConnectAsync() && !(await db.Database.GetPendingMigrationsAsync()).Any() && await db.Catalog.AnyAsync() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503); }
    catch { return Results.StatusCode(503); }
}).AllowAnonymous();
object View(Project p, IRepository repo) => new { p.Id, p.PatternId, p.Name, p.Status, p.Data, p.Completed, p.Substitutions, p.Milestones, p.WorkingArea, p.Revision, p.DataRevision, p.UpdatedAt, sourceAvailable = repo.GetPattern(p.PatternId).SourceFile is not null, canUndo = p.Undo.Count > 0, canRedo = p.Redo.Count > 0 };
object State(Project p) => new { p.Id, p.Name, p.Status, p.Completed, p.Substitutions, p.Milestones, p.WorkingArea, p.Revision, p.DataRevision, p.UpdatedAt, canUndo = p.Undo.Count > 0, canRedo = p.Redo.Count > 0 };
var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/capabilities", () => new { maxUploadMegabytes = maxUploadMb });
api.MapGet("/projects", (IRepository repo) => repo.List().Select(p => new { p.Id, p.Name, p.Status, p.UpdatedAt, width = p.Data.Width, height = p.Data.Height, total = p.Data.Stitches.Count, completed = p.Completed.Count, colors = p.Data.Definitions.Count, preview = p.Data.Stitches.Count <= 15000 ? p.Data : null }));
api.MapGet("/projects/{id}", (string id, IRepository repo) => View(repo.Get(id), repo));
api.MapGet("/projects/{id}/state", (string id, long? revision, IRepository repo) => { var p = repo.GetState(id, revision); return p is null ? Results.NoContent() : Results.Ok(State(p)); });
api.MapGet("/patterns/{id}", (string id, IRepository repo) => repo.GetPattern(id));
api.MapPost("/patterns/{id}/projects", (string id, IRepository repo) => View(repo.Create(repo.GetPattern(id), requireExisting: true), repo));
api.MapPost("/projects/sample", (IRepository repo, int? size) => View(repo.Create(SamplePattern.Create(Math.Clamp(size ?? 100, 20, 1000)), status: "active"), repo));
api.MapPost("/projects/{id}/duplicate", (string id, IRepository repo) => { var original = repo.Get(id); return View(repo.Create(repo.GetPattern(original.PatternId), original.Name + " · new start", "audit", requireExisting: true), repo); });
api.MapPost("/projects/{id}/commands", (string id, Command command, IRepository repo) => View(repo.Execute(id, command), repo));
api.MapPost("/projects/{id}/progress", (string id, ProgressBatch batch, IRepository repo) => State(repo.Progress(id, batch)));
api.MapDelete("/projects/{id}", async (string id, IRepository repo, RetentionService retention, CancellationToken ct) =>
{
    repo.Delete(id);
    // The committed queue survives cancellation or storage failure; the worker completes cleanup.
    await retention.TryCleanupAfterDeletion(ct); return Results.NoContent();
});
api.MapDelete("/account", async ([Microsoft.AspNetCore.Mvc.FromBody] AccountDeletion request, ICurrentUserContext current, RetentionService retention, SignInManager<ApplicationUser> signIn, CancellationToken ct) =>
{
    if (request.Confirmation != "DELETE") throw new UserError("Type DELETE to confirm permanent account deletion.");
    retention.DeleteAccount(current.UserId);
    await signIn.SignOutAsync();
    await retention.TryCleanupAfterDeletion(ct); return Results.NoContent();
});
api.MapGet("/projects/{id}/source", async (string id, IRepository repo, IPatternAssetStore store, CancellationToken ct) =>
{
    var source = repo.GetSource(id); return Results.File(await store.Read(source.StorageKey, ct), source.MediaType, enableRangeProcessing: true);
});
api.MapPost("/imports", async (HttpRequest request, IRepository repo, IPatternImporter importer, IPatternAssetStore store, StitchDbContext db, CancellationToken ct) =>
{
    var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file") ?? throw new UserError("Choose a PDF to import.");
    if (file.Length == 0 || file.Length > maxUploadMb * 1024 * 1024 || !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new UserError($"Choose a PDF no larger than {maxUploadMb} MB.");
    using var memory = new MemoryStream(); await file.CopyToAsync(memory, ct); var bytes = memory.ToArray();
    if (!System.Text.Encoding.ASCII.GetString(bytes.Take(1024).ToArray()).Contains("%PDF-")) throw new UserError("This file does not appear to be a PDF.");
    var originalName = Path.GetFileName(file.FileName.Replace('\\', '/')); if (originalName.Length > 200) originalName = originalName[^200..];
    var asset = new PatternSourceAsset { OriginalFileName = originalName, ByteSize = bytes.Length, Sha256 = StorageKeys.Hash(bytes) };
    var path = TemporaryFiles.CreatePath("import");
    try
    {
        await File.WriteAllBytesAsync(path, bytes, ct); PatternData data;
        try { data = importer.Parse(path, repo.Catalog()); }
        catch (Exception ex)
        {
            app.Logger.LogWarning("PDF import {AssetId} failed with {FailureType}", asset.Id, ex.GetType().Name);
            data = new() { Warnings = [new(ex is UserError ? ex.Message : "This PDF could not be read. It may be encrypted, damaged, or unsupported. The original is retained.")] };
        }
        ContentLifetime.Queue(db, "source", asset.StorageKey, DateTimeOffset.UtcNow.AddHours(24));
        memory.Position = 0; await store.Put(asset.StorageKey, memory, asset.MediaType, ct);
        var name = Path.GetFileNameWithoutExtension(originalName); name = name[..Math.Min(160, name.Length)];
        // A failed/ambiguous commit is handled by the durable queue, which checks references before deleting.
        return View(repo.Create(new(Guid.NewGuid().ToString("N"), name, null, data), asset: asset, imported: true), repo);
    }
    finally { if (File.Exists(path)) File.Delete(path); }
});
api.MapGet("/catalog", (IRepository repo) => repo.Catalog().Values);
api.MapGet("/inventory", (IRepository repo) => repo.Inventory());
api.MapPut("/inventory/{code}", (string code, InventoryEntry entry, IRepository repo) => { if (code != entry.Code) throw new UserError("Thread codes do not match."); return repo.SaveInventory(entry); });
api.MapGet("/preferences", (StitchDbContext db, ICurrentUserContext user) => { var p = db.Preferences.AsNoTracking().SingleOrDefault(x => x.UserId == user.UserId); return new { revision = p?.Revision ?? 0, values = Json.Read<System.Text.Json.JsonElement>(p?.Json ?? "{}") }; });
api.MapPut("/preferences", (PreferenceRequest request, StitchDbContext db, ICurrentUserContext user) =>
{
    if (request.Values.ValueKind != System.Text.Json.JsonValueKind.Object || request.Values.GetRawText().Length > 16000) throw new UserError("Preferences must be an object under 16 KB.");
    using var tx = db.Database.BeginTransaction();
    db.Database.ExecuteSqlInterpolated($"SELECT pg_advisory_xact_lock(hashtextextended({user.UserId + ":preferences"}, 0))");
    var p = db.Preferences.SingleOrDefault(x => x.UserId == user.UserId);
    if ((p?.Revision ?? 0) != request.Revision) throw Repository.Conflict();
    if (p is null) { p = new() { UserId = user.UserId }; db.Preferences.Add(p); }
    p.Json = request.Values.GetRawText(); p.Revision++; db.SaveChanges(); tx.Commit();
    return new { p.Revision, request.Values };
});
api.MapGet("/backups", (BackupService backups, ICurrentUserContext user) => backups.List(user.UserId).Select(b => new { b.Id, b.Kind, b.CreatedAt, bytes = b.ByteSize }));
api.MapGet("/backups/{id}/download", async (string id, BackupService backups, ICurrentUserContext user, CancellationToken ct) => new PrivateDownload(await backups.Download(user.UserId, id, ct), $"stitch-helper-backup-{id}.zip"));
api.MapPost("/exports/current", async (BackupService backups, ICurrentUserContext user, CancellationToken ct) => new PrivateDownload(await backups.Export(user.UserId, ct: ct), $"stitch-helper-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip"));
api.MapFallback("/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");
app.Run();
public record PreferenceRequest(long Revision, System.Text.Json.JsonElement Values);
public record AccountDeletion(string Confirmation);
public partial class Program { }
