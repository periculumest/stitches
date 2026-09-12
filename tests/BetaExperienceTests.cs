using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using StitchHelper;
using Xunit;

namespace StitchHelper.Tests;

public class BetaExperienceTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly string root = Path.Combine(Path.GetTempPath(), "stitch-beta-" + Guid.NewGuid().ToString("N"));
    private sealed class EnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "StitchHelper";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private BetaService Service(StitchDbContext db, BetaOptions? options = null) => new(db, options ?? new BetaOptions(), DataProtectionProvider.Create(new DirectoryInfo(root)), new ConfigurationBuilder().Build(), new EnvironmentStub());
    private static FeedbackInput Input(string? project = null) => new(Guid.NewGuid(), "Bug", "The pattern was confusing.", "workspace", false, 1, ProjectId: project);
    private void Admin()
    {
        using var db = database.Open(); var role = new IdentityRole<Guid>(BetaService.AdminRole) { Id = Guid.NewGuid(), NormalizedName = "BETAADMIN" };
        db.Roles.Add(role); db.UserRoles.Add(new() { UserId = database.A, RoleId = role.Id }); db.SaveChanges();
    }
    private static byte[] Screenshot()
    {
        using var image = new Image<Rgba32>(8, 8); image.Metadata.ExifProfile = new ExifProfile(); image.Metadata.ExifProfile.SetValue(ExifTag.Software, "PRIVATE_METADATA");
        using var stream = new MemoryStream(); image.SaveAsPng(stream); return stream.ToArray();
    }
    [PostgresFact]
    public async Task OwnershipConsentAndDiagnosticsAreServerControlled()
    {
        using var db = database.Open(); var repo = database.Repo(db);
        var asset = new PatternSourceAsset { ByteSize = 12 };
        var project = repo.Create(SamplePattern.Create(), asset: asset, imported: true);
        var service = Service(db); var context = await service.Context(database.A, project.Id);
        Assert.Equal(404, (await Assert.ThrowsAsync<UserError>(() => service.Context(database.B, project.Id))).Status);
        await Assert.ThrowsAsync<UserError>(() => service.Submit(database.B, Input(project.Id), [], null));
        await Assert.ThrowsAsync<UserError>(() => service.Submit(database.A, Input(project.Id) with { AttachPattern = true, SourceAssetId = "foreign" }, [], null));
        var report = await service.Submit(database.A, Input(project.Id) with { Route = "/?token=secret#private" }, [], "browser");
        Assert.Equal(database.A, report.UserId); Assert.Equal("unknown", report.Route); Assert.Null(report.BrowserMetadata); Assert.Null(report.ImportMetadata); Assert.Null(report.PatternAttachmentConsentAt);
        Assert.Empty(db.Set<FeedbackAttachment>());
        var withSource = await service.Submit(database.A, Input(project.Id) with { AttachPattern = true, SourceAssetId = context.SourceAssetId, DiagnosticsIncluded = true }, [], "browser");
        Assert.NotNull(withSource.PatternAttachmentConsentAt); Assert.NotNull(withSource.ImportMetadata);
        Assert.Single(db.Assets); Assert.Equal(asset.Id, db.Set<FeedbackAttachment>().Single().AssetId);
        repo.Delete(project.Id);
        Assert.Null(db.Set<FeedbackAttachment>().AsNoTracking().Single().AssetId);
        Assert.Equal(2, db.Set<FeedbackSubmission>().Count());
    }
    [PostgresFact]
    public async Task RetriesAreAtomicAndIdempotentIncludingConcurrentLostResponses()
    {
        var input = Input() with { AttachScreenshots = true }; var screenshot = Screenshot();
        var ids = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ => { await using var db = database.Open(); return (await Service(db).Submit(database.A, input, [screenshot], null)).Id; }));
        Assert.Equal(ids[0], ids[1]);
        using var verify = database.Open(); Assert.Single(verify.Set<FeedbackSubmission>()); Assert.Single(verify.Set<FeedbackAttachment>());
        Assert.Equal(409, (await Assert.ThrowsAsync<UserError>(() => Service(verify).Submit(database.A, input with { Message = "Changed message" }, [screenshot], null))).Status);
        using var decoded = Image.Load(verify.Set<FeedbackAttachment>().Single().Screenshot!); Assert.Null(decoded.Metadata.ExifProfile);
    }
    [PostgresFact]
    public async Task InvalidAttachmentsAndFullQuotaLeaveNoContentAndPermitText()
    {
        using var db = database.Open(); var s = Service(db, new BetaOptions { StorageQuotaBytes = 0 });
        await Assert.ThrowsAsync<UserError>(() => s.Submit(database.A, Input() with { AttachScreenshots = true }, [[1, 2, 3]], null));
        await Assert.ThrowsAsync<UserError>(() => s.Submit(database.A, Input(), [Screenshot()], null));
        Assert.Equal(413, (await Assert.ThrowsAsync<UserError>(() => s.Submit(database.A, Input() with { AttachScreenshots = true }, [Screenshot()], null))).Status);
        Assert.Empty(db.Set<FeedbackAttachment>()); Assert.Empty(db.Set<FeedbackSubmission>());
        await s.Submit(database.A, Input(), [], null); Assert.Single(db.Set<FeedbackSubmission>());
        var bounded = Service(db, new BetaOptions { ScreenshotMaxPixels = 4 });
        Assert.Equal(413, Assert.Throws<UserError>(() => bounded.SanitizeScreenshot(Screenshot())).Status);
    }
    [PostgresFact]
    public async Task ErrorTokensCannotAssociateAnotherAccountAndOptOutOmitsThem()
    {
        using var db = database.Open(); var s = Service(db); var token = s.ProtectError(database.A, "CONFLICT", "reference-1");
        await Assert.ThrowsAsync<UserError>(() => s.Submit(database.B, Input() with { DiagnosticsIncluded = true, ErrorToken = token }, [], null));
        var report = await s.Submit(database.A, Input() with { DiagnosticsIncluded = true, ErrorToken = token }, [], null);
        Assert.Equal("CONFLICT", report.ErrorCode); Assert.Equal("reference-1", report.CorrelationId);
        var minimal = await s.Submit(database.B, Input() with { ErrorToken = token }, [], null); Assert.Null(minimal.CorrelationId);
    }
    [PostgresFact]
    public async Task CompletionIsExplicitArchivePreservesAndReopeningClears()
    {
        Admin(); using var db = database.Open(); var s = Service(db); var report = await s.Submit(database.B, Input(), [], null);
        Assert.Equal("Unread", report.Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<UserError>(() => s.SetStatus(database.B, report.Id, "Completed"))).Status);
        await s.SetStatus(database.A, report.Id, "Read"); Assert.Null(report.CompletedAt);
        await s.SetStatus(database.A, report.Id, "Completed"); var completed = report.CompletedAt; Assert.NotNull(completed); Assert.Equal(database.A, report.CompletedByAdminUserId);
        await s.SetStatus(database.A, report.Id, "Archived"); Assert.Equal(completed, report.CompletedAt);
        await s.SetStatus(database.A, report.Id, "Unread"); Assert.Null(report.CompletedAt); Assert.Null(report.CompletedByAdminUserId);
    }
    [PostgresFact]
    public async Task AnnouncementSelectionDoesNotResurrectOlderDismissedContent()
    {
        using var db = database.Open(); var now = DateTimeOffset.UtcNow;
        var old = new BetaAnnouncement { Title = "Old", Active = true, PublishedAt = now.AddHours(-2) };
        var latest = new BetaAnnouncement { Title = "Latest", Active = true, PublishedAt = now.AddHours(-1), StartsAt = now.AddHours(-1), EndsAt = now.AddHours(1) };
        db.AddRange(old, latest, new BetaAnnouncement { Title = "Future", Active = true, PublishedAt = now.AddHours(1) }, new BetaAnnouncement { Title = "Draft", Active = true }); await db.SaveChangesAsync();
        var s = Service(db); Assert.Equal(latest.Id, (await s.Announcement(database.B))!.Id);
        db.Add(new AnnouncementDismissal { UserId = database.B, AnnouncementId = latest.Id }); await db.SaveChangesAsync(); Assert.Null(await s.Announcement(database.B));
        var next = new BetaAnnouncement { Title = "New", Active = true, PublishedAt = now }; db.Add(next); await db.SaveChangesAsync(); Assert.Equal(next.Id, (await s.Announcement(database.B))!.Id);
        Assert.False(BetaService.SafeLink("javascript:alert(1)")); Assert.False(BetaService.SafeLink("//evil.test")); Assert.True(BetaService.SafeLink("/whats-new"));
    }
    private static MultipartFormDataContent Form(FeedbackInput input)
    {
        var form = new MultipartFormDataContent(); form.Add(new StringContent(Json.Write(input)), "input"); return form;
    }
    [PostgresFact]
    public async Task HttpBoundariesEnforceAuthCsrfAdminAndPublishedReleases()
    {
        Admin(); using var factory = new TestFactory(database, root);
        using var anonymous = factory.CreateClient(); using var user = await factory.User(database.B); using var admin = await factory.User(database.A); using var noCsrf = await factory.User(database.B, false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/beta/feedback", Form(Input()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await noCsrf.PostAsync("/api/beta/feedback", Form(Input()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/beta/feedback")).StatusCode);
        var input = Input(); var response = await user.PostAsync("/api/beta/feedback", Form(input)); response.EnsureSuccessStatusCode();
        var first = await response.Content.ReadAsStringAsync(); Assert.Equal(first, await (await user.PostAsync("/api/beta/feedback", Form(input))).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync("/api/beta/feedback")).StatusCode);
        (await user.PostAsJsonAsync("/api/beta/onboarding", new { version = BetaOptions.OnboardingVersion })).EnsureSuccessStatusCode();
        var state = await user.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/beta/state"); Assert.Equal(BetaOptions.OnboardingVersion, state.GetProperty("completedOnboardingVersion").GetInt32());
        foreach (var publish in new[] { false, true }) (await admin.PostAsJsonAsync("/api/admin/beta/releases", new ReleaseInput("test", DateOnly.FromDateTime(DateTime.Today), publish ? "Published" : "Draft", "<script>literal</script>", publish))).EnsureSuccessStatusCode();
        var releases = await user.GetFromJsonAsync<BetaRelease[]>("/api/beta/releases"); Assert.Single(releases!); Assert.Equal("Published", releases![0].Title);
        var missing = await user.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/beta/public"); Assert.True(missing.TryGetProperty("appVersion", out _));
        var denied = await user.GetAsync("/api/admin/beta/feedback"); var details = await denied.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(); Assert.Equal("ACCESS_REQUIRED", details.GetProperty("code").GetString()); Assert.False(string.IsNullOrEmpty(details.GetProperty("referenceId").GetString()));
        using (var db = database.Open())
        {
            db.Add(new LegalVersion { DocumentSlug = "terms-of-service", Version = 1, RequiresAcceptance = true, AcceptanceGeneration = 1 }); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await user.GetAsync("/api/beta/releases")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/beta/public")).StatusCode); // Support remains available during legal acceptance failures.
    }
    [PostgresFact]
    public async Task AccountDeletionCascadesFeedbackScreenshotsAndOnboarding()
    {
        using var db = database.Open(); await Service(db).Submit(database.A, Input() with { AttachScreenshots = true }, [Screenshot()], null);
        db.Add(new UserOnboardingState { UserId = database.A, CompletedOnboardingVersion = 1 }); db.SaveChanges();
        new RetentionService(db, new LocalPatternAssetStore(root), new LocalBackupArtifactStore(root), NullLogger<RetentionService>.Instance).DeleteAccount(database.A);
        Assert.Empty(db.Set<FeedbackSubmission>()); Assert.Empty(db.Set<FeedbackAttachment>()); Assert.Empty(db.Set<UserOnboardingState>());
    }
    [PostgresFact]
    public async Task AttachmentDownloadsRequireAdminAndExactReportAndAreAudited()
    {
        Admin(); string reportId, attachmentId;
        using (var db = database.Open())
        {
            reportId = (await Service(db).Submit(database.B, Input() with { AttachScreenshots = true }, [Screenshot()], null)).Id;
            attachmentId = db.Set<FeedbackAttachment>().Single().Id;
        }
        using var factory = new TestFactory(database, root); using var admin = await factory.User(database.A); using var user = await factory.User(database.B);
        var url = $"/api/admin/beta/feedback/{reportId}/attachments/{attachmentId}";
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/beta/feedback/other/attachments/{attachmentId}")).StatusCode);
        var response = await admin.GetAsync(url); response.EnsureSuccessStatusCode(); Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        using var verify = database.Open(); Assert.Single(verify.Set<BetaAuditEvent>(), a => a.Action == "FeedbackAttachmentOpened" && a.TargetId == attachmentId);
        admin.DefaultRequestHeaders.Add("X-Account-Id", database.B.ToString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync("/api/beta/feedback", Form(Input()))).StatusCode);
    }
    [PostgresFact]
    public async Task ProductionAdmissionRequiresConfiguredServerEmailOrExplicitAdministrator()
    {
        using var db = database.Open(); var user = await db.Users.FindAsync(database.B); user!.Email = "Tester@Example.test"; await db.SaveChangesAsync();
        var environment = new EnvironmentStub { EnvironmentName = "Production" };
        var denied = new BetaService(db, new(), DataProtectionProvider.Create(new DirectoryInfo(root)), new ConfigurationBuilder().Build(), environment);
        Assert.False(await denied.HasAccess(database.B));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Beta:AllowedEmails:0"] = "tester@example.test" }).Build();
        var allowed = new BetaService(db, new(), DataProtectionProvider.Create(new DirectoryInfo(root)), configuration, environment);
        Assert.True(await allowed.HasAccess(database.B)); Assert.False(await allowed.HasAccess(database.A));
        Admin(); Assert.True(await allowed.HasAccess(database.A));
    }
    public void Dispose() { database.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
}
