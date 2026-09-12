using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StitchHelper;
using Xunit;

namespace StitchHelper.Tests;

public class LegalDocumentTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly string root = Path.Combine(Path.GetTempPath(), "stitch-legal-" + Guid.NewGuid().ToString("N"));
    private void Editor()
    {
        using var db = database.Open(); var role = new IdentityRole<Guid>(LegalDocumentService.EditorRole) { Id = Guid.NewGuid(), NormalizedName = "LEGALEDITOR" };
        db.Roles.Add(role); db.UserRoles.Add(new() { UserId = database.A, RoleId = role.Id }); db.SaveChanges();
    }
    private async Task<LegalVersion> Publish(string slug = "terms-of-service", bool reaccept = true, bool required = true)
    {
        using var db = database.Open(); var service = new LegalDocumentService(db);
        var existing = await db.LegalDocuments.FindAsync(slug);
        var draft = await service.Save(database.A, slug, new(existing?.DraftRevision ?? 0, slug, "Approved test text <script>literal</script>", "Test publication", "I accept this test document", required, reaccept), existing is null);
        return await service.Publish(database.A, slug, new(draft.DraftRevision));
    }
    private static LegalAcceptInput Accept(params LegalVersion[] versions) => new(versions.Select(v => new LegalAcceptanceInput(v.Id, v.ContentSha256)).ToList());

    [PostgresFact]
    public async Task EmptyDraftsArePrivateAndOnlyEditorsCanPublishWithOptimisticConcurrency()
    {
        using var db = database.Open(); var s = new LegalDocumentService(db);
        Assert.Equal(2, await db.LegalDocuments.CountAsync()); Assert.Empty(await s.Published()); Assert.False(await s.HasPending(database.B));
        Assert.Equal(403, (await Assert.ThrowsAsync<UserError>(() => s.Drafts(database.B))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<UserError>(() => s.Publish(database.B, "terms-of-service", new(0)))).Status);
        Editor();
        await Assert.ThrowsAsync<UserError>(() => s.Publish(database.A, "terms-of-service", new(0)));
        var v = await Publish();
        Assert.Equal(1, v.Version); Assert.Equal(64, v.ContentSha256.Length);
        // A separate editor request cannot overwrite or republish the old draft revision.
        using var fresh = database.Open(); var service = new LegalDocumentService(fresh);
        Assert.Equal(409, (await Assert.ThrowsAsync<UserError>(() => service.Save(database.A, v.DocumentSlug, new(0, "Title", "text", "summary", "Accept", true), false))).Status);
        fresh.ChangeTracker.Clear();
        Assert.Equal(409, (await Assert.ThrowsAsync<UserError>(() => service.Publish(database.A, v.DocumentSlug, new(1)))).Status);
        Assert.Equal(v.Body, (await service.PublishedVersion(v.DocumentSlug, 1)).Body);
    }

    [PostgresFact]
    public async Task EditorialChangesPreserveExactAcceptanceAndMaterialChangesRequireNewAcceptance()
    {
        Editor(); var first = await Publish();
        using var db = database.Open(); var service = new LegalDocumentService(db);
        Assert.True(await service.HasPending(database.B)); await service.Accept(database.B, Accept(first));
        var receipt = Assert.Single(await service.History(database.B));
        await service.Accept(database.B, Accept(first)); Assert.Equal(receipt.AcceptedAt, Assert.Single(await service.History(database.B)).AcceptedAt);
        Assert.False(await service.HasPending(database.B)); Assert.Empty(await service.History(database.A));
        var editorial = await Publish(reaccept: false);
        Assert.Equal(first.AcceptanceGeneration, editorial.AcceptanceGeneration); Assert.False(await service.HasPending(database.B));
        var status = Assert.Single((await service.Status(database.B)).Documents);
        Assert.Equal(1, status.LastAcceptance!.Version); Assert.Equal(2, status.Current.Version); Assert.False(status.NeedsAcceptance);
        var material = await Publish(); Assert.True(await service.HasPending(database.B));
        Assert.Equal(409, (await Assert.ThrowsAsync<UserError>(() => service.Accept(database.B, Accept(editorial)))).Status);
        await service.Accept(database.B, Accept(material)); Assert.False(await service.HasPending(database.B));
        Assert.Equal(2, (await service.History(database.B)).Count); Assert.Equal(3, Assert.Single((await service.Status(database.B)).Documents).LastAcceptance!.Version);
        await Publish(required: false); Assert.False(await service.HasPending(database.B));
        var reenabled = await Publish(reaccept: false); Assert.True(reenabled.AcceptanceGeneration > material.AcceptanceGeneration); Assert.True(await service.HasPending(database.B));
    }

    [PostgresFact]
    public async Task BatchValidationIsAtomicAndConcurrentRetriesDoNotDuplicateReceipts()
    {
        Editor(); var terms = await Publish(); var privacy = await Publish("privacy-policy");
        using var db = database.Open(); var service = new LegalDocumentService(db);
        var bad = Accept(terms, privacy); bad.Documents[1] = bad.Documents[1] with { ContentSha256 = "wrong" };
        Assert.Equal(409, (await Assert.ThrowsAsync<UserError>(() => service.Accept(database.B, bad))).Status); Assert.Empty(await service.History(database.B));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ => { using var separate = database.Open(); await new LegalDocumentService(separate).Accept(database.B, Accept(terms, privacy)); }));
        Assert.Equal(2, (await service.History(database.B)).Count); Assert.False(await service.HasPending(database.B));
        await Publish("cancellation-policy"); Assert.True(await service.HasPending(database.B));
    }

    [PostgresFact]
    public async Task DatabaseRejectsPublishedVersionAndReceiptEditsButAccountDeletionRemovesPersonalEvidence()
    {
        Editor(); var version = await Publish();
        using var db = database.Open(); var service = new LegalDocumentService(db); await service.Accept(database.B, Accept(version));
        await Assert.ThrowsAsync<PostgresException>(() => db.LegalVersions.Where(v => v.Id == version.Id).ExecuteUpdateAsync(s => s.SetProperty(v => v.Body, "tampered")));
        await Assert.ThrowsAsync<PostgresException>(() => db.LegalVersions.Where(v => v.Id == version.Id).ExecuteDeleteAsync());
        await Assert.ThrowsAsync<PostgresException>(() => db.LegalAcceptances.Where(a => a.UserId == database.B).ExecuteUpdateAsync(s => s.SetProperty(a => a.ContentSha256, "tampered")));
        var sources = new LocalPatternAssetStore(root); var backups = new LocalBackupArtifactStore(root);
        await using (var export = await new BackupService(db, sources, backups, NullLogger<BackupService>.Instance).Export(database.B))
        {
            BackupService.Validate(export); export.Position = 0;
            using var zip = new ZipArchive(export); using var reader = new StreamReader(zip.GetEntry("data/legal-acceptances.json")!.Open());
            var evidence = Json.Read<List<LegalAcceptanceEvidence>>(await reader.ReadToEndAsync());
            Assert.Equal(version.Body, Assert.Single(evidence).Document.Body); Assert.Equal(version.ContentSha256, evidence[0].ContentSha256);
        }
        new RetentionService(db, sources, backups, NullLogger<RetentionService>.Instance).DeleteAccount(database.B);
        Assert.Empty(await service.History(database.B)); Assert.Single(await service.Published());
        Assert.Equal(401, (await Assert.ThrowsAsync<UserError>(() => service.Accept(database.B, Accept(version)))).Status);
    }

    [PostgresFact]
    public async Task ApiEnforcesAcceptanceAndEditorAuthorizationWhileKeepingExitRoutesAccessible()
    {
        Editor(); var version = await Publish();
        using var factory = new TestFactory(database, root); using var anonymous = factory.CreateClient(); using var user = await factory.User(database.B); using var editor = await factory.User(database.A);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/legal/documents/terms-of-service")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/legal/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/legal/drafts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/admin/legal/audit", new { query = database.A.ToString() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/api/admin/legal/drafts")).StatusCode);
        Assert.Equal(428, (int)(await user.PostAsync("/api/projects/sample", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.PostAsync("/api/exports/current", null)).StatusCode);
        using var noCsrf = await factory.User(database.B, false);
        Assert.Equal(HttpStatusCode.BadRequest, (await noCsrf.PostAsJsonAsync("/api/legal/accept", Accept(version))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.PostAsJsonAsync("/api/legal/accept", Accept(version))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/projects")).StatusCode);
        var audit = await editor.PostAsJsonAsync("/api/admin/legal/audit", new { query = database.B.ToString() }); audit.EnsureSuccessStatusCode();
        Assert.Contains(version.ContentSha256, await audit.Content.ReadAsStringAsync());
        await Publish(); Assert.Equal(428, (int)(await user.GetAsync("/api/projects")).StatusCode);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/account") { Content = JsonContent.Create(new { confirmation = "DELETE" }) };
        Assert.Equal(HttpStatusCode.NoContent, (await user.SendAsync(delete)).StatusCode);
        using var db = database.Open(); Assert.Empty(db.LegalAcceptances); Assert.Equal(2, db.LegalVersions.Count());
    }

    public void Dispose() { database.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
}
