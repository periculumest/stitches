using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StitchHelper;
using Xunit;

namespace StitchHelper.Tests;

public class RetentionTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly string root = Path.Combine(Path.GetTempPath(), "stitch-retention-" + Guid.NewGuid().ToString("N"));
    private LocalPatternAssetStore Sources => new(root);
    private LocalBackupArtifactStore Backups => new(root);
    private RetentionService Service(StitchDbContext db, IPatternAssetStore? sources = null) => new(db, sources ?? Sources, Backups, NullLogger<RetentionService>.Instance);
    private BackupService Archives(StitchDbContext db, IBackupArtifactStore? store = null) => new(db, Sources, store ?? Backups, NullLogger<BackupService>.Instance);
    private async Task<Project> Import(Guid owner)
    {
        var bytes = DomainTests.CreatePdf();
        var asset = new PatternSourceAsset { ByteSize = bytes.Length, Sha256 = StorageKeys.Hash(bytes) };
        await Sources.Put(asset.StorageKey, new MemoryStream(bytes), "application/pdf");
        using var db = database.Open();
        return database.Repo(db, owner).Create(SamplePattern.Create(), asset: asset, imported: true);
    }

    [PostgresFact]
    public async Task DeletionPreservesSharedSourcesUntilLastProjectAndInvalidatesOldExports()
    {
        var first = await Import(database.A); var other = await Import(database.B);
        using var db = database.Open(); var repo = database.Repo(db);
        var pattern = repo.GetPattern(first.PatternId);
        var second = repo.Create(pattern, requireExisting: true);
        var sourceKey = repo.GetSource(first.Id).StorageKey;
        var backups = Archives(db); var now = DateTimeOffset.UtcNow;
        await backups.Retain(database.A, "daily", now); await backups.Retain(database.A, "weekly", now);
        var oldArchive = backups.List(database.A)[0];
        Assert.Equal(404, Assert.Throws<UserError>(() => repo.Delete(other.Id)).Status);
        Assert.Equal(2, backups.List(database.A).Count);
        repo.Delete(first.Id);
        Assert.Empty(backups.List(database.A));
        Assert.Equal(404, Assert.Throws<UserError>(() => backups.Get(database.A, oldArchive.Id)).Status);
        Assert.Equal(sourceKey, repo.GetSource(second.Id).StorageKey);
        await Service(db).Cleanup(default);
        await using (var pdf = await Sources.Read(sourceKey)) Assert.True(pdf.Length > 0);
        await using (var export = await backups.Export(database.A))
        {
            using var zip = new ZipArchive(export, ZipArchiveMode.Read, true);
            using var reader = new StreamReader(zip.GetEntry("data/projects.json")!.Open());
            var json = await reader.ReadToEndAsync(); Assert.DoesNotContain(first.Id, json); Assert.Contains(second.Id, json);
        }
        Assert.True(await backups.Retain(database.A, "daily", now)); // Same-day replacement is allowed.
        repo.Delete(second.Id);
        Assert.Empty(db.Patterns.Where(p => p.UserId == database.A));
        Assert.Empty(db.Assets.Where(p => p.UserId == database.A));
        Assert.Empty(db.Imports.Where(p => p.UserId == database.A));
        Assert.Equal(404, Assert.Throws<UserError>(() => repo.Create(pattern, requireExisting: true)).Status);
        await Service(db).Cleanup(default);
        await Assert.ThrowsAsync<FileNotFoundException>(() => Sources.Read(sourceKey));
        Assert.Empty(db.PendingObjectDeletions);
        Assert.Single(db.Projects.Where(p => p.UserId == database.B));
        await using var empty = await backups.Export(database.A);
        using var emptyZip = new ZipArchive(empty);
        Assert.DoesNotContain(emptyZip.Entries, e => e.FullName.StartsWith("assets/"));
    }

    private sealed class FailingSources(string folder) : LocalObjectStore(Path.Combine(folder, "sources")), IPatternAssetStore
    {
        public bool Fail { get; set; } = true;
        public new Task Delete(string key, CancellationToken ct = default) => Fail ? throw new IOException("Storage unavailable") : base.Delete(key, ct);
        Task IPrivateObjectStore.Delete(string key, CancellationToken ct) => Delete(key, ct);
    }

    [PostgresFact]
    public async Task FailedFileDeletionIsDurableAndRetryRemovesItAfterRestart()
    {
        var project = await Import(database.A); string key;
        using (var db = database.Open())
        {
            var repo = database.Repo(db); key = repo.GetSource(project.Id).StorageKey;
            repo.Delete(project.Id);
            await Service(db, new FailingSources(root)).Cleanup(default);
            var queued = Assert.Single(db.PendingObjectDeletions);
            Assert.Equal("source", queued.StoreKind); Assert.Equal(1, queued.Attempts);
            Assert.True(queued.NotBefore > DateTimeOffset.UtcNow);
            Assert.Empty(repo.List());
        }
        using (var restarted = database.Open())
        {
            await restarted.PendingObjectDeletions.ExecuteUpdateAsync(s => s.SetProperty(d => d.NotBefore, DateTimeOffset.UtcNow.AddMinutes(-1)));
            await Service(restarted).Cleanup(default);
            Assert.Empty(restarted.PendingObjectDeletions);
        }
        await Assert.ThrowsAsync<FileNotFoundException>(() => Sources.Read(key));
    }

    [PostgresFact]
    public async Task MaintenancePurgesLegacyOrphansExpiredBackupsAndAbandonedUploads()
    {
        var project = await Import(database.A);
        using var db = database.Open(); var repo = database.Repo(db); var key = repo.GetSource(project.Id).StorageKey;
        var backups = Archives(db);
        await backups.Retain(database.A, "daily", DateTimeOffset.UtcNow.AddDays(-8));
        await backups.Retain(database.A, "weekly", DateTimeOffset.UtcNow.AddDays(-31));
        var expiredId = db.Backups.AsNoTracking().First().Id;
        Assert.Empty(backups.List(database.A));
        Assert.Equal(404, Assert.Throws<UserError>(() => backups.Get(database.A, expiredId)).Status);
        await Service(db).Maintain(default);
        Assert.Empty(db.Backups); Assert.Single(db.Projects);
        await db.Projects.Where(p => p.Id == project.Id).ExecuteDeleteAsync(); // Old delete behavior.
        var abandoned = Guid.NewGuid().ToString("N");
        await Sources.Put(abandoned, new MemoryStream([1, 2, 3]), "application/pdf");
        ContentLifetime.Queue(db, "source", abandoned, DateTimeOffset.UtcNow.AddHours(-25));
        await Service(db).Maintain(default);
        Assert.Empty(db.Assets); Assert.Empty(db.Patterns); Assert.Empty(db.Imports); Assert.Empty(db.PendingObjectDeletions);
        await Assert.ThrowsAsync<FileNotFoundException>(() => Sources.Read(key));
        await Assert.ThrowsAsync<FileNotFoundException>(() => Sources.Read(abandoned));
    }

    [PostgresFact]
    public async Task AccountDeletionRequiresConfirmationAndCsrfAndInvalidatesOtherSessions()
    {
        var project = await Import(database.A); await Import(database.B);
        using (var db = database.Open())
        {
            database.Repo(db).SaveInventory(new("5", 1, "basket", 0));
            db.Preferences.Add(new() { UserId = database.A });
            db.UserLogins.Add(new() { UserId = database.A, LoginProvider = "Google", ProviderKey = "deletion-test-subject" });
            db.SaveChanges(); await Archives(db).Retain(database.A, "daily", DateTimeOffset.UtcNow);
        }
        using var factory = new TestFactory(database, root);
        using var first = await factory.User(database.A); using var stale = await factory.User(database.A);
        using var noCsrf = await factory.User(database.A, csrf: false);
        using var invalid = new HttpRequestMessage(HttpMethod.Delete, "/api/account") { Content = JsonContent.Create(new { confirmation = "no" }) };
        Assert.Equal(HttpStatusCode.BadRequest, (await first.SendAsync(invalid)).StatusCode);
        using var unverified = new HttpRequestMessage(HttpMethod.Delete, "/api/account") { Content = JsonContent.Create(new { confirmation = "DELETE" }) };
        Assert.Equal(HttpStatusCode.BadRequest, (await noCsrf.SendAsync(unverified)).StatusCode);
        using var confirmed = new HttpRequestMessage(HttpMethod.Delete, "/api/account") { Content = JsonContent.Create(new { confirmation = "DELETE" }) };
        Assert.Equal(HttpStatusCode.NoContent, (await first.SendAsync(confirmed)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.GetAsync($"/api/projects/{project.Id}/source")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.PostAsync("/api/exports/current", null)).StatusCode);
        using var read = database.Open();
        Assert.False(read.Users.Any(u => u.Id == database.A)); Assert.Empty(read.UserLogins);
        Assert.Empty(read.Inventory); Assert.Empty(read.Preferences); Assert.Empty(read.Backups); Assert.Empty(read.BackupJobs);
        Assert.Empty(read.PendingObjectDeletions);
        Assert.Single(read.Users); Assert.Single(read.Patterns); Assert.Single(read.Assets); Assert.Single(read.Projects);
    }

    private sealed class PausedBackupStore(string folder) : LocalObjectStore(Path.Combine(folder, "backups")), IBackupArtifactStore
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public new async Task Put(string key, Stream source, string mediaType, CancellationToken ct = default)
        {
            Entered.TrySetResult(); await Release.Task.WaitAsync(ct); await base.Put(key, source, mediaType, ct);
        }
        Task IPrivateObjectStore.Put(string key, Stream source, string mediaType, CancellationToken ct) => Put(key, source, mediaType, ct);
    }

    [PostgresFact]
    public async Task BackupCannotPublishDeletedContentAfterDeletionCompletes()
    {
        var project = await Import(database.A); var paused = new PausedBackupStore(root);
        using var db = database.Open();
        var backup = Archives(db, paused).Retain(database.A, "daily", DateTimeOffset.UtcNow);
        await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var deleting = Task.Run(() => { using var separate = database.Open(); database.Repo(separate).Delete(project.Id); });
        try { await Task.Delay(150); Assert.False(deleting.IsCompleted); }
        finally { paused.Release.TrySetResult(); }
        await backup.WaitAsync(TimeSpan.FromSeconds(10)); await deleting.WaitAsync(TimeSpan.FromSeconds(10));
        using var read = database.Open(); Assert.Empty(read.Backups); Assert.Empty(read.Projects); Assert.Empty(read.Patterns);
        await Service(read).Cleanup(default); Assert.Empty(read.PendingObjectDeletions);
    }

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
