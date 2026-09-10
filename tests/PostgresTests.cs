using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using StitchHelper;
using Xunit;

namespace StitchHelper.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STITCH_TEST_POSTGRES"))) Skip = "Set STITCH_TEST_POSTGRES to an isolated PostgreSQL admin connection."; }
}
public sealed record TestUser(Guid UserId) : ICurrentUserContext;
public sealed class TestDatabase : IDisposable
{
    public string Connection { get; }
    private readonly string admin;
    private readonly string name = "stitch_test_" + Guid.NewGuid().ToString("N");
    public readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();
    public TestDatabase()
    {
        admin = Environment.GetEnvironmentVariable("STITCH_TEST_POSTGRES") ?? throw new InvalidOperationException("STITCH_TEST_POSTGRES is required.");
        using var connection = new NpgsqlConnection(admin); connection.Open();
        using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection); command.ExecuteNonQuery();
        Connection = new NpgsqlConnectionStringBuilder(admin) { Database = name }.ToString();
        using var db = Open(); db.Database.Migrate(); db.SeedCatalog();
        db.Users.AddRange(new ApplicationUser { Id = A, UserName = "A", NormalizedUserName = "A", DisplayName = "Alice", SecurityStamp = "a-secret-stamp", PasswordHash = "never-export-a" }, new ApplicationUser { Id = B, UserName = "B", NormalizedUserName = "B", DisplayName = "Bob", SecurityStamp = "b-secret-stamp", PasswordHash = "never-export-b" }); db.SaveChanges();
    }
    public StitchDbContext Open() => new(new DbContextOptionsBuilder<StitchDbContext>().UseNpgsql(Connection).Options);
    public Repository Repo(StitchDbContext db, Guid? owner = null) => new(db, new TestUser(owner ?? A));
    public void Dispose()
    {
        NpgsqlConnection.ClearAllPools();
        using var connection = new NpgsqlConnection(admin); connection.Open();
        using var command = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", connection); command.ExecuteNonQuery();
    }
}
public sealed class TestFactory(TestDatabase database, string storageRoot) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing").UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../server")))
            .UseSetting("ConnectionStrings:StitchHelper", database.Connection).UseSetting("Storage:Provider", "Local").UseSetting("Storage:LocalRoot", storageRoot);
    }
    public async Task<HttpClient> User(Guid id, bool csrf = true)
    {
        var client = CreateClient(new() { AllowAutoRedirect = false });
        var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var stamp = id == database.A ? "a-secret-stamp" : "b-secret-stamp";
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, id.ToString()), new(ClaimTypes.Name, "Test user"), new("AspNet.Identity.SecurityStamp", stamp)], IdentityConstants.ApplicationScheme));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IsPersistent = true, IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30) }, IdentityConstants.ApplicationScheme);
        client.DefaultRequestHeaders.Add("Cookie", "StitchHelper.Session=" + options.TicketDataFormat.Protect(ticket));
        if (csrf)
        {
            var response = await client.GetAsync("/api/antiforgery"); response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", json.GetProperty("token").GetString());
        }
        return client;
    }
}
public sealed class FailingBackupStore(string root) : LocalObjectStore(root), IBackupArtifactStore
{
    public bool Fail { get; set; }
    public new Task Put(string key, Stream source, string mediaType, CancellationToken cancellation = default) => Fail ? throw new IOException("Simulated object store failure") : base.Put(key, source, mediaType, cancellation);
    Task IPrivateObjectStore.Put(string key, Stream source, string mediaType, CancellationToken cancellation) => Put(key, source, mediaType, cancellation);
}

public class PostgresTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly string root = Path.Combine(Path.GetTempPath(), "stitch-private-tests-" + Guid.NewGuid().ToString("N"));
    [PostgresFact]
    public void MigrationsPersistAcrossContextsAndPreventCrossOwnerRelationships()
    {
        using var db = database.Open(); var repo = database.Repo(db); var p = repo.Create(SamplePattern.Create(), status: "active");
        using var second = database.Open(); Assert.Equal(p.Id, database.Repo(second).Get(p.Id).Id);
        Assert.Empty(second.Database.GetPendingMigrations()); Assert.Equal(489, second.Catalog.Count());
        second.Projects.Add(new() { Id = Guid.NewGuid().ToString("N"), UserId = database.B, PatternId = p.PatternId });
        Assert.Throws<DbUpdateException>(() => second.SaveChanges());
    }
    [PostgresFact]
    public async Task DisjointWritesMergeAndSameStitchUsesLastCommitWithoutChartReplacement()
    {
        string id; string[] stitches; string chart;
        using (var db = database.Open()) { var p = database.Repo(db).Create(SamplePattern.Create(), status: "active"); id = p.Id; stitches = p.Data.Stitches.Take(2).Select(x => x.Id).ToArray(); chart = db.Projects.Single().DataJson; }
        await Task.WhenAll(stitches.Select(stitch => Task.Run(() => { using var db = database.Open(); database.Repo(db).Progress(id, new(Guid.NewGuid(), [new(stitch, true)])); })));
        using var read = database.Open(); var repo = database.Repo(read); var p2 = repo.Get(id);
        Assert.Equal(2, p2.Completed.Count); Assert.Equal(chart, read.Projects.Single().DataJson); Assert.Equal(0, p2.DataRevision);
        repo.Progress(id, new(Guid.NewGuid(), [new(stitches[0], false)])); Assert.Single(repo.Get(id).Completed); Assert.Single(read.StitchStates);
        Assert.Equal(409, Assert.Throws<UserError>(() => repo.Execute(id, new(0, "rename", Name: "stale"))).Status);
        Assert.NotNull(repo.GetState(id, 0)); Assert.Null(repo.GetState(id, repo.Get(id).Revision));
    }
    [PostgresFact]
    public void ProgressRetryIsIdempotentAndUndoDoesNotRemoveOtherStitches()
    {
        using var db = database.Open(); var repo = database.Repo(db); var p = repo.Create(SamplePattern.Create(), status: "active");
        var ids = p.Data.Stitches.Take(2).Select(x => x.Id).ToArray();
        var batch = new ProgressBatch(Guid.NewGuid(), [new(ids[0], true)]);
        repo.Progress(p.Id, batch); var second = repo.Progress(p.Id, new(Guid.NewGuid(), [new(ids[1], true)]));
        var retry = repo.Progress(p.Id, batch); Assert.Equal(second.Revision, retry.Revision); Assert.Equal(2, retry.Undo.Count);
        Assert.Equal(409, Assert.Throws<UserError>(() => repo.Progress(p.Id, batch with { Changes = [new(ids[0], false)] })).Status);
        var undone = repo.Execute(p.Id, new(second.Revision, "undo")); Assert.Equal(new[] { ids[0] }, undone.Completed); Assert.Equal(0, undone.DataRevision);
        Assert.Throws<UserError>(() => repo.Progress(p.Id, new(Guid.NewGuid(), [new(ids[1], true), new("missing", true)])));
        Assert.Single(repo.Get(p.Id).Completed);
    }
    [PostgresFact]
    public void OwnedInventoryHasConcurrencyAndCannotAffectAnotherUser()
    {
        using var db = database.Open(); var a = database.Repo(db); var b = database.Repo(db, database.B);
        var saved = a.SaveInventory(new("310", 3, "Box 1")); Assert.Equal(1, saved.Revision); Assert.Empty(b.Inventory());
        Assert.Equal(409, Assert.Throws<UserError>(() => a.SaveInventory(new("310", 8, "stale"))).Status);
        Assert.Throws<UserError>(() => a.SaveInventory(new("310", -1, "box", 1)));
        a.SaveInventory(saved with { BobbinCount = 0, Location = "" });
        Assert.Equal(409, Assert.Throws<UserError>(() => a.SaveInventory(saved)).Status); // tombstone preserves version
    }
    [PostgresFact]
    public async Task AnonymousCsrfAndCrossUserApiAccessAreRejected()
    {
        using var factory = new TestFactory(database, root);
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/projects/sample", new { })).StatusCode);
        using var noCsrf = await factory.User(database.A, false);
        Assert.Equal(HttpStatusCode.BadRequest, (await noCsrf.PostAsJsonAsync("/api/projects/sample", new { })).StatusCode);
        using var a = await factory.User(database.A); using var b = await factory.User(database.B);
        var created = await b.PostAsJsonAsync("/api/projects/sample", new { ownerUserId = database.A }); created.EnsureSuccessStatusCode();
        var p = await created.Content.ReadFromJsonAsync<Project>(); Assert.NotNull(p);
        Assert.Empty((await a.GetFromJsonAsync<Project[]>("/api/projects"))!);
        foreach (var url in new[] { $"/api/projects/{p.Id}", $"/api/projects/{p.Id}/state", $"/api/patterns/{p.PatternId}", $"/api/projects/{p.Id}/source", "/api/backups/not-owned/download" }) Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync(url)).StatusCode);
        foreach (var url in new[] { $"/api/projects/{p.Id}/duplicate", $"/api/patterns/{p.PatternId}/projects" }) Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsJsonAsync(url, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsJsonAsync($"/api/projects/{p.Id}/commands", new Command(0, "rename", Name: "attack"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsJsonAsync($"/api/projects/{p.Id}/progress", new ProgressBatch(Guid.NewGuid(), [new(p.Data.Stitches[0].Id, true)]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.DeleteAsync($"/api/projects/{p.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsJsonAsync("/api/catalog", new ThreadEntry("9999", "attack", "#000000"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync("/auth/logout", null)).StatusCode);
    }
    [PostgresFact]
    public async Task GoogleSubjectResolvesSameUserDespiteEmailChangesAndRacingCallbacks()
    {
        using var factory = new TestFactory(database, root); _ = factory.CreateClient();
        async Task<Guid> Resolve(string email)
        {
            using var scope = factory.Services.CreateScope();
            var info = new ExternalLoginInfo(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Email, email), new(ClaimTypes.Name, "Test Google")])), "Google", "stable-subject", "Google");
            return (await scope.ServiceProvider.GetRequiredService<GoogleIdentityResolver>().Resolve(info)).Id;
        }
        var ids = await Task.WhenAll(Resolve("original@example.test"), Resolve("changed@example.test"));
        Assert.Equal(ids[0], ids[1]); Assert.Equal(ids[0], await Resolve("third@example.test"));
        using var db = database.Open(); Assert.Equal(3, db.Users.Count()); Assert.Single(db.UserLogins); Assert.Empty(db.UserTokens);
        var cookie = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        Assert.Equal(TimeSpan.FromDays(30), cookie.ExpireTimeSpan); Assert.True(cookie.SlidingExpiration); Assert.True(cookie.Cookie.HttpOnly);
    }
    [PostgresFact]
    public async Task ImportedAssetAndPortableExportArePrivateCompleteAndSecretFree()
    {
        using var factory = new TestFactory(database, root); using var a = await factory.User(database.A); using var b = await factory.User(database.B);
        using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(DomainTests.CreatePdf()), "file", "original.pdf");
        var imported = await a.PostAsync("/api/imports", form); imported.EnsureSuccessStatusCode(); var p = (await imported.Content.ReadFromJsonAsync<Project>())!;
        Assert.Equal(200, p.Data.Stitches.Count);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/projects/{p.Id}/source")).StatusCode);
        Assert.Equal(DomainTests.CreatePdf().Length, (await a.GetByteArrayAsync($"/api/projects/{p.Id}/source")).Length);
        await b.PostAsJsonAsync("/api/projects/sample", new { });
        using var export = await a.PostAsync("/api/exports/current", null); export.EnsureSuccessStatusCode();
        using var stream = new MemoryStream(await export.Content.ReadAsByteArrayAsync()); BackupService.Validate(stream); stream.Position = 0;
        using var zip = new ZipArchive(stream); Assert.NotNull(zip.GetEntry("manifest.json")); Assert.Single(zip.Entries.Where(x => x.FullName.StartsWith("assets/")));
        var data = string.Join("\n", zip.Entries.Where(x => x.FullName.EndsWith(".json")).Select(x => { using var reader = new StreamReader(x.Open()); return reader.ReadToEnd(); }));
        Assert.Contains(p.Id, data); Assert.DoesNotContain(database.B.ToString(), data); Assert.DoesNotContain("never-export", data); Assert.DoesNotContain("secret-stamp", data); Assert.DoesNotContain("PasswordHash", data, StringComparison.OrdinalIgnoreCase);
        using var db = database.Open(); var service = new BackupService(db, new LocalPatternAssetStore(root), new LocalBackupArtifactStore(root), NullLogger<BackupService>.Instance);
        Assert.True(await service.Retain(database.A, "daily", DateTimeOffset.UtcNow)); var artifact = service.List(database.A).Single();
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/backups/{artifact.Id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync($"/api/backups/{artifact.Id}/download")).StatusCode);
    }
    [PostgresFact]
    public async Task FailedDailyAndWeeklyReplacementsPreservePreviousArtifactsAndRetention()
    {
        using var db = database.Open(); database.Repo(db).Create(SamplePattern.Create());
        var store = new FailingBackupStore(Path.Combine(root, "archives"));
        var service = new BackupService(db, new LocalPatternAssetStore(root), store, NullLogger<BackupService>.Instance);
        var now = DateTimeOffset.UtcNow;
        foreach (var kind in new[] { "daily", "weekly" })
        {
            Assert.True(await service.Retain(database.A, kind, now));
            Assert.False(await service.Retain(database.A, kind, now));
            var previous = service.List(database.A).Single(x => x.Kind == kind);
            store.Fail = true;
            await Assert.ThrowsAsync<IOException>(() => service.Retain(database.A, kind, now.AddDays(8)));
            Assert.Equal(previous.Id, service.List(database.A).Single(x => x.Kind == kind).Id);
            await using (var good = await service.Download(database.A, previous.Id, default)) { BackupService.Validate(good); }
            store.Fail = false; Assert.True(await service.Retain(database.A, kind, now.AddDays(8)));
            Assert.NotEqual(previous.Id, service.List(database.A).Single(x => x.Kind == kind).Id);
        }
        Assert.Equal(2, service.List(database.A).Count); Assert.Equal(2, Directory.GetFiles(Path.Combine(root, "archives")).Length);
    }
    [PostgresFact]
    public async Task MultipleWorkersProduceOneBackupPerScheduleWindow()
    {
        using (var db = database.Open()) database.Repo(db).Create(SamplePattern.Create());
        var now = DateTimeOffset.UtcNow;
        async Task<bool> Run() { using var db = database.Open(); return await new BackupService(db, new LocalPatternAssetStore(root), new LocalBackupArtifactStore(root), NullLogger<BackupService>.Instance).Retain(database.A, "daily", now); }
        var outcomes = await Task.WhenAll(Run(), Run()); Assert.Single(outcomes.Where(x => x));
        using var read = database.Open(); Assert.Single(read.Backups); Assert.Single(read.BackupJobs);
    }
    [PostgresFact]
    public async Task AssetTraversalAndTamperedArchiveAreRejected()
    {
        var store = new LocalPatternAssetStore(root); await Assert.ThrowsAsync<InvalidDataException>(async () => await store.Read("../secret"));
        using var db = database.Open(); database.Repo(db).Create(SamplePattern.Create());
        var service = new BackupService(db, store, new LocalBackupArtifactStore(root), NullLogger<BackupService>.Instance);
        await using var export = await service.Export(database.A); using var copy = new MemoryStream(); await export.CopyToAsync(copy); copy.Position = 0;
        using (var zip = new ZipArchive(copy, ZipArchiveMode.Update, true)) { zip.GetEntry("data/projects.json")!.Delete(); using var writer = new StreamWriter(zip.CreateEntry("data/projects.json").Open()); writer.Write("[]"); }
        copy.Position = 0; Assert.Throws<InvalidDataException>(() => BackupService.Validate(copy));
    }
    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
