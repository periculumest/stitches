using System.Data;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace StitchHelper;

public sealed class BackupService(StitchDbContext db, IPatternAssetStore assets, IBackupArtifactStore artifacts, ILogger<BackupService> logger, RetentionService? retention = null)
{
    public const long MaxExportBytes = 2_000_000_000;
    private IQueryable<BackupArtifact> Available(Guid owner)
    {
        var dailyCutoff = DateTimeOffset.UtcNow.AddDays(-7); var weeklyCutoff = DateTimeOffset.UtcNow.AddDays(-30);
        return db.Backups.AsNoTracking().Where(x => x.UserId == owner && (x.Kind == "daily" ? x.CreatedAt > dailyCutoff : x.CreatedAt > weeklyCutoff));
    }
    public List<BackupArtifact> List(Guid owner) => Available(owner).OrderBy(x => x.Kind).ToList();
    public BackupArtifact Get(Guid owner, string id) => Available(owner).SingleOrDefault(x => x.Id == id) ?? throw Repository.Missing();
    public async Task<Stream> Download(Guid owner, string id, CancellationToken ct)
    {
        await using var lease = await ContentLifetime.Acquire(db, owner, ct);
        var backup = Get(owner, id);
        var stream = await artifacts.Read(backup.StorageKey, ct);
        try
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            if (hash != backup.Sha256 || stream.Length != backup.ByteSize) throw new InvalidDataException("The stored backup failed its integrity check.");
            stream.Position = 0; return stream;
        }
        catch { await stream.DisposeAsync(); throw; }
    }
    public async Task<Stream> Export(Guid owner, string source = "current-export", CancellationToken ct = default)
    {
        await using var lease = await ContentLifetime.Acquire(db, owner, ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var patterns = await db.Patterns.AsNoTracking().Where(x => x.UserId == owner).ToListAsync(ct);
        var projects = await db.Projects.AsNoTracking().Where(x => x.UserId == owner).ToListAsync(ct);
        var progress = await db.StitchStates.AsNoTracking().Where(x => db.Projects.Any(p => p.Id == x.ProjectId && p.UserId == owner)).ToListAsync(ct);
        var inventory = await db.Inventory.AsNoTracking().Where(x => x.UserId == owner).ToListAsync(ct);
        var preferences = await db.Preferences.AsNoTracking().Where(x => x.UserId == owner).ToListAsync(ct);
        var imports = await db.Imports.AsNoTracking().Where(x => x.UserId == owner).ToListAsync(ct);
        var ownedAssets = await db.Assets.AsNoTracking().Where(x => x.UserId == owner).ToListAsync(ct);
        var catalog = await db.Catalog.AsNoTracking().Select(x => x.Json).ToListAsync(ct);
        var legalAcceptances = await new LegalDocumentService(db).History(owner, ct);
        await tx.CommitAsync(ct);
        var chartData = patterns.Select(x => Json.Read<PatternData>(x.DataJson)).Concat(projects.Select(x => Json.Read<PatternData>(x.DataJson))).ToList();
        var states = projects.Select(x => Json.Read<Project>(x.StateJson)).ToList();
        var codes = chartData.SelectMany(x => x.Definitions).SelectMany(x => x.GetComponents()).Select(x => x.ThreadCode)
            .Concat(inventory.Select(x => x.Code)).Concat(states.SelectMany(x => x.Substitutions.Values)).ToHashSet();
        // History can refer to catalog colors no longer present in the current chart.
        foreach (var history in states.SelectMany(x => x.Undo.Concat(x.Redo)))
        {
            if (history.Replacement is not null) codes.Add(history.Replacement);
            if (history.Definition is not null) foreach (var c in history.Definition.GetComponents()) codes.Add(c.ThreadCode);
            if (history.Data is not null) foreach (var c in history.Data.Definitions.SelectMany(x => x.GetComponents())) codes.Add(c.ThreadCode);
        }
        if (ownedAssets.Sum(x => x.ByteSize) > MaxExportBytes) throw new UserError("Your export exceeds the current 2 GB limit. Contact the operator for an assisted export.", 413);
        var file = new FileStream(TemporaryFiles.CreatePath("export"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            var files = new Dictionary<string, string>(); var sections = new Dictionary<string, int>();
            var assetManifest = new List<object>(); long uncompressed = 0;
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                void Section<T>(string name, T[] values)
                {
                    var bytes = Encoding.UTF8.GetBytes(Json.Write(values)); uncompressed += bytes.Length;
                    if (uncompressed > MaxExportBytes) throw new UserError("Export exceeds the current 2 GB limit.", 413);
                    using var entry = zip.CreateEntry("data/" + name + ".json").Open(); entry.Write(bytes);
                    files["data/" + name + ".json"] = StorageKeys.Hash(bytes); sections[name] = values.Length;
                }
                Section("patterns", patterns.Select(x => new { x.Id, x.Name, x.AssetId, x.CreatedAt, data = Json.Read<PatternData>(x.DataJson) }).ToArray());
                Section("legal-acceptances", legalAcceptances.ToArray());
                Section("projects", projects.Select(x => new { x.Id, x.PatternId, x.Revision, x.DataRevision, x.UpdatedAt, data = Json.Read<PatternData>(x.DataJson), metadata = Json.Read<System.Text.Json.JsonElement>(x.StateJson) }).ToArray());
                Section("project-progress", progress.Select(x => new { x.ProjectId, x.StitchId, state = "complete", x.UpdatedAt }).ToArray());
                Section("inventory", inventory.Select(x => new { x.Code, x.BobbinCount, x.Location, x.Revision }).ToArray());
                Section("preferences", preferences.Select(x => new { x.Revision, values = Json.Read<System.Text.Json.JsonElement>(x.Json) }).ToArray());
                Section("imports", imports.Select(x => new { x.Id, x.PatternId, x.Status, x.CreatedAt }).ToArray());
                Section("substitutions", states.SelectMany(x => x.Substitutions.Select(s => new { projectId = x.Id, componentId = s.Key, replacementThreadCode = s.Value })).ToArray());
                Section("thread-catalog", catalog.Select(Json.Read<ThreadEntry>).Where(x => codes.Contains(x.Code)).ToArray());
                foreach (var asset in ownedAssets)
                {
                    var path = $"assets/{asset.Id}/original.pdf";
                    await using var input = await assets.Read(asset.StorageKey, ct);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    await using var output = zip.CreateEntry(path).Open();
                    var buffer = new byte[81920]; long size = 0; int read;
                    while ((read = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        size += read; uncompressed += read;
                        if (uncompressed > MaxExportBytes) throw new UserError("Export exceeds the current 2 GB limit.", 413);
                        hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                    var sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                    if (sha != asset.Sha256 || size != asset.ByteSize) throw new InvalidDataException("A source asset failed its integrity check.");
                    files[path] = sha;
                    assetManifest.Add(new { asset.Id, path, asset.OriginalFileName, asset.MediaType, asset.ByteSize, asset.Sha256 });
                }
                using var manifest = new StreamWriter(zip.CreateEntry("manifest.json").Open(), new UTF8Encoding(false));
                manifest.Write(Json.Write(new { format = "stitch-helper-backup", formatVersion = 1, createdAt = DateTimeOffset.UtcNow, applicationVersion = typeof(BackupService).Assembly.GetName().Version?.ToString(), schemaVersion = 1, source, sections, assets = assetManifest, files }));
            }
            file.Position = 0; Validate(file); file.Position = 0; return file;
        }
        catch { await file.DisposeAsync(); throw; }
    }
    public static void Validate(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        using var manifestStream = zip.GetEntry("manifest.json")?.Open() ?? throw new InvalidDataException("Missing manifest.");
        using var manifest = System.Text.Json.JsonDocument.Parse(manifestStream);
        if (manifest.RootElement.GetProperty("format").GetString() != "stitch-helper-backup" || manifest.RootElement.GetProperty("formatVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported backup format.");
        var files = manifest.RootElement.GetProperty("files");
        if (zip.Entries.Sum(x => x.Length) > MaxExportBytes + 1024 * 1024 || zip.Entries.Count != files.EnumerateObject().Count() + 1) throw new InvalidDataException("Invalid archive size or entries.");
        foreach (var item in files.EnumerateObject())
        {
            if (item.Name.Contains("..") || item.Name.Contains('\\') || item.Name.StartsWith('/') || item.Name.Contains(':')) throw new InvalidDataException("Invalid archive path.");
            using var content = zip.GetEntry(item.Name)?.Open() ?? throw new InvalidDataException("Missing backup section.");
            if (Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant() != item.Value.GetString()) throw new InvalidDataException("Backup integrity check failed.");
        }
    }
    public async Task<bool> Retain(Guid owner, string kind, DateTimeOffset now, CancellationToken ct = default)
    {
        if (kind is not ("daily" or "weekly")) throw new ArgumentException("Use daily or weekly.", nameof(kind));
        await using var lease = await ContentLifetime.Acquire(db, owner, ct);
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        if (kind == "weekly") date = date.AddDays(-((int)date.DayOfWeek + 6) % 7); // Monday UTC
        await db.Database.OpenConnectionAsync(ct);
        var lockName = $"backup:{owner}:{kind}";
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(hashtextextended(@key, 0))";
            var key = command.CreateParameter(); key.ParameterName = "key"; key.Value = lockName; command.Parameters.Add(key);
            if (await command.ExecuteScalarAsync(ct) is not true) return false;
            try
            {
                if (await db.BackupJobs.AnyAsync(x => x.UserId == owner && x.Kind == kind && x.ScheduleDate == date, ct)) return false;
                await using var export = await Export(owner, kind, ct);
                var candidate = new BackupArtifact { UserId = owner, Kind = kind, ScheduleDate = date, CreatedAt = now, ByteSize = export.Length, Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(export, ct)).ToLowerInvariant() };
                export.Position = 0;
                ContentLifetime.Queue(db, "backup", candidate.StorageKey, DateTimeOffset.UtcNow.AddHours(24));
                await artifacts.Put(candidate.StorageKey, export, "application/zip", ct);
                var promoted = false;
                try
                {
                    await using (var stored = await artifacts.Read(candidate.StorageKey, ct))
                    {
                        if (stored.Length != candidate.ByteSize || Convert.ToHexString(await SHA256.HashDataAsync(stored, ct)).ToLowerInvariant() != candidate.Sha256) throw new InvalidDataException("Uploaded backup failed verification.");
                        stored.Position = 0; Validate(stored);
                    }
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    var previous = await db.Backups.SingleOrDefaultAsync(x => x.UserId == owner && x.Kind == kind, ct);
                    if (previous is not null)
                    {
                        ContentLifetime.Queue(db, "backup", previous.StorageKey);
                        db.Backups.Remove(previous); await db.SaveChangesAsync(ct);
                    }
                    db.Backups.Add(candidate);
                    await db.PendingObjectDeletions.Where(d => d.StoreKind == "backup" && d.StorageKey == candidate.StorageKey).ExecuteDeleteAsync(ct);
                    db.BackupJobs.Add(new() { UserId = owner, Kind = kind, ScheduleDate = date, CompletedAt = now });
                    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); promoted = true;
                    logger.LogInformation("Backup {Kind} succeeded for user {UserId}", kind, owner);
                }
                finally
                {
                    if (!promoted)
                    {
                        // If commit acknowledgement was lost, a successfully retained object must survive.
                        try { if (!await db.Backups.AsNoTracking().AnyAsync(x => x.StorageKey == candidate.StorageKey)) await artifacts.Delete(candidate.StorageKey, CancellationToken.None); }
                        catch (Exception ex) { logger.LogWarning(ex, "Candidate cleanup failed for {StorageKey}", candidate.StorageKey); }
                    }
                    db.ChangeTracker.Clear();
                }
                await Cleanup(ct); return true;
            }
            finally
            {
                command.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@key, 0))";
                await command.ExecuteScalarAsync(CancellationToken.None);
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
    public Task Cleanup(CancellationToken ct) => (retention ?? new RetentionService(db, assets, artifacts, Microsoft.Extensions.Logging.Abstractions.NullLogger<RetentionService>.Instance)).Cleanup(ct);
}
