using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace StitchHelper;

public class BackupService(IRepository repository, ILogger<BackupService> logger) : BackgroundService
{
    public string? LastError { get; private set; }
    public object[] List() => Directory.Exists(Path.Combine(repository.Root, "backups"))
        ? Directory.GetFiles(Path.Combine(repository.Root, "backups"), "*.zip").OrderDescending().Select(f => (object)new { name = Path.GetFileName(f), bytes = new FileInfo(f).Length, createdAt = File.GetLastWriteTimeUtc(f) }).ToArray() : [];
    public string Create(bool automatic = false)
    {
        lock (repository.Gate)
        {
            var directory = Path.Combine(repository.Root, "backups"); Directory.CreateDirectory(directory);
            var name = $"{(automatic ? "auto" : "manual")}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..6]}.zip";
            var staging = Path.Combine(directory, $".staging-{Guid.NewGuid():N}"); Directory.CreateDirectory(staging);
            try
            {
                using (var source = repository.Open())
                using (var target = new SqliteConnection($"Data Source={Path.Combine(staging, "stitch-helper.db")};Pooling=False")) { target.Open(); source.BackupDatabase(target); }
                Directory.CreateDirectory(Path.Combine(staging, "sources"));
                foreach (var source in Directory.GetFiles(Path.Combine(repository.Root, "sources"))) File.Copy(source, Path.Combine(staging, "sources", Path.GetFileName(source)));
                var hashes = Directory.GetFiles(staging, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(staging, f).Replace('\\', '/'), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
                File.WriteAllText(Path.Combine(staging, "manifest.json"), Json.Write(new BackupManifest(1, DateTimeOffset.UtcNow, hashes)));
                ZipFile.CreateFromDirectory(staging, Path.Combine(directory, name + ".partial"));
                File.Move(Path.Combine(directory, name + ".partial"), Path.Combine(directory, name));
                if (automatic) foreach (var old in Directory.GetFiles(directory, "auto-*.zip").OrderDescending().Skip(7)) File.Delete(old);
                LastError = null; return name;
            }
            finally { Directory.Delete(staging, true); }
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var folder = Path.Combine(repository.Root, "backups");
                if (!Directory.Exists(folder) || !Directory.GetFiles(folder, $"auto-{DateTime.UtcNow:yyyyMMdd}-*.zip").Any()) Create(true);
            }
            catch (Exception ex) { LastError = "Automatic backup failed. Check available disk space and try Back up now."; logger.LogError(ex, "Automatic backup failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
    public static void Restore(string archive, string target)
    {
        target = Path.GetFullPath(target);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()) throw new UserError("Restore requires a new or empty data directory. Existing data is never overwritten.");
        var staging = target + ".restore-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            using (var zip = ZipFile.OpenRead(archive))
            {
                if (zip.Entries.Sum(e => e.Length) > 2_000_000_000) throw new UserError("Backup exceeds the 2 GB restore limit.");
                foreach (var e in zip.Entries)
                {
                    var file = Path.GetFullPath(Path.Combine(staging, e.FullName));
                    if (!file.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || e.FullName.Contains(':')) throw new UserError("Invalid backup path.");
                }
                zip.ExtractToDirectory(staging);
            }
            var manifest = Json.Read<BackupManifest>(File.ReadAllText(Path.Combine(staging, "manifest.json")));
            if (manifest.SchemaVersion != 1 || !manifest.Files.ContainsKey("stitch-helper.db")) throw new UserError("Unsupported or incomplete backup.");
            foreach (var (relative, hash) in manifest.Files)
            {
                var file = Path.GetFullPath(Path.Combine(staging, relative));
                if (!file.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(file) || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) != hash) throw new UserError("Backup integrity check failed.");
            }
            using (var db = new SqliteConnection($"Data Source={Path.Combine(staging, "stitch-helper.db")};Pooling=False;Mode=ReadOnly"))
            {
                db.Open(); using var c = db.CreateCommand(); c.CommandText = "PRAGMA integrity_check";
                if (c.ExecuteScalar()?.ToString() != "ok") throw new UserError("The backup database is damaged.");
                c.CommandText = "SELECT value FROM metadata WHERE key='schemaVersion'";
                if (c.ExecuteScalar()?.ToString() != "1") throw new UserError("Unsupported database version.");
                c.CommandText = "SELECT json FROM patterns"; using var reader = c.ExecuteReader();
                while (reader.Read())
                {
                    var pattern = Json.Read<Pattern>(reader.GetString(0));
                    if (pattern.SourceFile is not null && !manifest.Files.ContainsKey("sources/" + pattern.SourceFile)) throw new UserError("A source PDF is missing from the backup.");
                }
            }
            if (Directory.Exists(target)) Directory.Delete(target); // verified empty above; nonrecursive
            Directory.Move(staging, target);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
public record BackupManifest(int SchemaVersion, DateTimeOffset CreatedAt, Dictionary<string, string> Files);
