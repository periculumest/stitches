using Microsoft.EntityFrameworkCore;

namespace StitchHelper;

public static class BetaEndpoints
{
    public static void MapBeta(this WebApplication app)
    {
        app.MapGet("/api/beta/public", (BetaOptions options) => new { options.AppVersion, options.SupportContact }).AllowAnonymous();
        var beta = app.MapGroup("/api/beta").RequireAuthorization();
        beta.MapGet("/state", async (ICurrentUserContext user, BetaService service, BetaOptions options, StitchDbContext db) => new
        {
            options.AppVersion, options.SupportContact, options.MessageMinLength, options.MessageMaxLength, options.ScreenshotCount,
            options.ScreenshotMaxBytes, options.ScreenshotMaxPixels, disclosureVersion = BetaOptions.DisclosureVersion,
            onboardingVersion = BetaOptions.OnboardingVersion, hasAccess = await service.HasAccess(user.UserId), isAdmin = await service.IsAdmin(user.UserId),
            completedOnboardingVersion = (await db.Set<UserOnboardingState>().FindAsync(user.UserId))?.CompletedOnboardingVersion ?? 0
        });
        beta.MapGet("/context/{id}", (string id, ICurrentUserContext user, BetaService service) => service.Context(user.UserId, id));
        beta.MapPost("/feedback", async (HttpRequest request, ICurrentUserContext user, BetaService service, BetaOptions options) =>
        {
            var form = await request.ReadFormAsync();
            if (form["input"].ToString().Length > 32000) throw new UserError("Feedback details exceed the limit.");
            FeedbackInput input;
            try { input = Json.Read<FeedbackInput>(form["input"].ToString()) ?? throw new System.Text.Json.JsonException(); }
            catch (System.Text.Json.JsonException) { throw new UserError("The feedback form is invalid. Reopen it and try again."); }
            if (form.Files.Count > options.ScreenshotCount) throw new UserError("Too many screenshots.");
            var files = new List<byte[]>();
            foreach (var file in form.Files)
            {
                if (file.Length > options.ScreenshotMaxBytes) throw new UserError("A screenshot exceeds the configured byte limit.", 413);
                using var buffer = new MemoryStream(); await file.CopyToAsync(buffer); files.Add(buffer.ToArray());
            }
            var report = await service.Submit(user.UserId, input, files, request.Headers.UserAgent.ToString());
            return new { report.Id, report.CreatedAt };
        });
        beta.MapPost("/onboarding", async (OnboardingInput input, ICurrentUserContext user, StitchDbContext db) =>
        {
            if (input.Version != BetaOptions.OnboardingVersion) throw new UserError("Reload to see the current tour.");
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"UserOnboardingState\" (\"UserId\", \"CompletedOnboardingVersion\", \"CompletedOrDismissedAt\") VALUES ({user.UserId}, {input.Version}, {now}) ON CONFLICT (\"UserId\") DO UPDATE SET \"CompletedOnboardingVersion\" = GREATEST(\"UserOnboardingState\".\"CompletedOnboardingVersion\", EXCLUDED.\"CompletedOnboardingVersion\"), \"CompletedOrDismissedAt\" = EXCLUDED.\"CompletedOrDismissedAt\"");
            return Results.NoContent();
        });
        beta.MapGet("/announcement", (ICurrentUserContext user, BetaService service) => service.Announcement(user.UserId));
        beta.MapPost("/announcements/{id}/dismiss", async (string id, ICurrentUserContext user, StitchDbContext db) =>
        {
            if (!await db.Set<BetaAnnouncement>().AnyAsync(a => a.Id == id && a.Dismissible && a.PublishedAt != null)) throw Repository.Missing();
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"AnnouncementDismissal\" (\"UserId\", \"AnnouncementId\", \"DismissedAt\") VALUES ({user.UserId}, {id}, {now}) ON CONFLICT DO NOTHING");
            return Results.NoContent();
        });
        beta.MapGet("/releases", (StitchDbContext db) => db.Set<BetaRelease>().AsNoTracking().Where(r => r.PublishedAt <= DateTimeOffset.UtcNow).OrderByDescending(r => r.ReleaseDate).ThenByDescending(r => r.PublishedAt).ToListAsync());
        var admin = app.MapGroup("/api/admin/beta").RequireAuthorization();
        admin.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            await services.GetRequiredService<BetaService>().RequireAdmin(services.GetRequiredService<ICurrentUserContext>().UserId);
            return await next(context);
        });
        admin.MapGet("/feedback", async (string? status, bool? includeArchived, int? page, StitchDbContext db) =>
        {
            var query = db.Set<FeedbackSubmission>().AsNoTracking();
            if (status == "Completed") query = query.Where(f => f.CompletedAt != null && (includeArchived == true || f.Status != "Archived"));
            else if (!string.IsNullOrEmpty(status)) query = query.Where(f => f.Status == status);
            return await query.OrderByDescending(f => f.CreatedAt).Skip(Math.Clamp(page ?? 0, 0, 10000) * 50).Take(50)
                .Select(f => new { f.Id, f.UserId, f.Category, f.Status, f.CreatedAt, f.CompletedAt, hasAttachments = db.Set<FeedbackAttachment>().Any(a => a.FeedbackSubmissionId == f.Id) }).ToListAsync();
        });
        admin.MapGet("/feedback/{id}", async (string id, StitchDbContext db) => new
        {
            report = await db.Set<FeedbackSubmission>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == id) ?? throw Repository.Missing(),
            attachments = await db.Set<FeedbackAttachment>().Where(a => a.FeedbackSubmissionId == id).Select(a => new { a.Id, a.Kind, available = a.Screenshot != null || a.AssetId != null }).ToListAsync()
        });
        admin.MapPost("/feedback/{id}/status", async (string id, FeedbackStatusInput input, ICurrentUserContext user, BetaService service) => { await service.SetStatus(user.UserId, id, input.Status); return Results.NoContent(); });
        admin.MapGet("/feedback/{id}/attachments/{attachmentId}", async (string id, string attachmentId, ICurrentUserContext user, StitchDbContext db, BetaService service, IPatternAssetStore store) =>
        {
            var report = await db.Set<FeedbackSubmission>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == id) ?? throw Repository.Missing();
            await using var lease = await ContentLifetime.Acquire(db, report.UserId, CancellationToken.None);
            var attachment = await db.Set<FeedbackAttachment>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == attachmentId && a.FeedbackSubmissionId == id) ?? throw Repository.Missing();
            service.Audit(user.UserId, "FeedbackAttachmentOpened", attachment.Id); await db.SaveChangesAsync();
            if (attachment.Screenshot is not null) return Results.File(attachment.Screenshot, "image/png", "feedback-screenshot.png");
            var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == attachment.AssetId && a.UserId == report.UserId) ?? throw Repository.Missing();
            if (report.PatternAttachmentConsentAt is null) throw Repository.Missing();
            using var stream = await store.Read(asset.StorageKey); using var bytes = new MemoryStream(); await stream.CopyToAsync(bytes);
            return Results.File(bytes.ToArray(), "application/pdf", "attached-pattern.pdf");
        });
        admin.MapGet("/announcements", (StitchDbContext db) => db.Set<BetaAnnouncement>().AsNoTracking().OrderByDescending(a => a.PublishedAt).Take(100).ToListAsync());
        admin.MapPost("/announcements", async (AnnouncementInput input, ICurrentUserContext user, StitchDbContext db, BetaService service) =>
        {
            if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 120 || string.IsNullOrWhiteSpace(input.Message) || input.Message.Length > 1000 ||
                !BetaService.SafeLink(input.LinkUrl) || input.LinkText?.Length > 100 || !string.IsNullOrEmpty(input.LinkUrl) && string.IsNullOrWhiteSpace(input.LinkText) || input.StartsAt >= input.EndsAt)
                throw new UserError("Provide a title, short message, valid window, and an HTTPS link or /whats-new with link text.");
            var entry = new BetaAnnouncement { Title = input.Title, Message = input.Message, LinkText = input.LinkText, LinkUrl = input.LinkUrl,
                StartsAt = input.StartsAt?.ToUniversalTime(), EndsAt = input.EndsAt?.ToUniversalTime(), Active = input.Active, Dismissible = input.Dismissible, PublishedAt = input.Publish ? DateTimeOffset.UtcNow : null };
            db.Add(entry); service.Audit(user.UserId, input.Publish ? "AnnouncementPublished" : "AnnouncementDrafted", entry.Id); await db.SaveChangesAsync(); return entry;
        });
        admin.MapPost("/announcements/{id}/state", async (string id, AnnouncementStateInput input, ICurrentUserContext user, StitchDbContext db, BetaService service) =>
        {
            var entry = await db.Set<BetaAnnouncement>().FindAsync(id) ?? throw Repository.Missing(); entry.Active = input.Active;
            if (input.Publish && entry.PublishedAt is null) entry.PublishedAt = DateTimeOffset.UtcNow;
            service.Audit(user.UserId, "AnnouncementStateChanged", id); await db.SaveChangesAsync(); return Results.NoContent();
        });
        admin.MapGet("/releases", (StitchDbContext db) => db.Set<BetaRelease>().AsNoTracking().OrderByDescending(r => r.ReleaseDate).Take(100).ToListAsync());
        admin.MapPost("/releases", async (ReleaseInput input, ICurrentUserContext user, StitchDbContext db, BetaService service) =>
        {
            if (string.IsNullOrWhiteSpace(input.Version) || input.Version.Length > 80 || string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 160 ||
                string.IsNullOrWhiteSpace(input.BodyMarkdown) || input.BodyMarkdown.Length > 20000 || input.ReleaseDate == default)
                throw new UserError("Provide a version, date, title, and release notes under 20,000 characters.");
            // The renderer only creates escaped text, paragraphs, headings and lists. No raw HTML, images or links execute.
            var entry = new BetaRelease { Version = input.Version, ReleaseDate = input.ReleaseDate, Title = input.Title, BodyMarkdown = input.BodyMarkdown, PublishedAt = input.Publish ? DateTimeOffset.UtcNow : null };
            db.Add(entry); service.Audit(user.UserId, input.Publish ? "ReleasePublished" : "ReleaseDrafted", entry.Id); await db.SaveChangesAsync(); return entry;
        });
        admin.MapPost("/releases/{id}/publish", async (string id, ICurrentUserContext user, StitchDbContext db, BetaService service) =>
        {
            var entry = await db.Set<BetaRelease>().FindAsync(id) ?? throw Repository.Missing();
            if (entry.PublishedAt is null) { entry.PublishedAt = DateTimeOffset.UtcNow; service.Audit(user.UserId, "ReleasePublished", id); await db.SaveChangesAsync(); }
            return Results.NoContent();
        });
    }
}
public record OnboardingInput(int Version);
public record FeedbackStatusInput(string Status);
public record AnnouncementStateInput(bool Active, bool Publish);
public record AnnouncementInput(string Title, string Message, string? LinkText, string? LinkUrl, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, bool Active, bool Dismissible, bool Publish);
public record ReleaseInput(string Version, DateOnly ReleaseDate, string Title, string BodyMarkdown, bool Publish);
