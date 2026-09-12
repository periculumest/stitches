using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace StitchHelper;

public sealed class BetaOptions
{
    public int MessageMinLength { get; set; } = 5;
    public int MessageMaxLength { get; set; } = 5000;
    public int ScreenshotCount { get; set; } = 1;
    public int ScreenshotMaxBytes { get; set; } = 5 * 1024 * 1024;
    public int ScreenshotMaxPixels { get; set; } = 12_000_000;
    public long StorageQuotaBytes { get; set; } = 500 * 1024 * 1024;
    public string SupportContact { get; set; } = "Contact the person who invited you to the beta.";
    public string AppVersion { get; set; } = typeof(BetaOptions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.2.0";
    public const int OnboardingVersion = 2, DisclosureVersion = 1;
    public void Validate()
    {
        if (MessageMinLength < 1 || MessageMaxLength < MessageMinLength || MessageMaxLength > 20000 || ScreenshotCount is < 1 or > 3 ||
            ScreenshotMaxBytes is < 1 or > 10 * 1024 * 1024 || ScreenshotMaxPixels is < 1 or > 24_000_000 || StorageQuotaBytes < 0)
            throw new InvalidOperationException("Invalid Beta feedback limits.");
    }
}
public record FeedbackInput(Guid SubmissionKey, string Category, string Message, string Route, bool DiagnosticsIncluded,
    int DiagnosticsDisclosureVersion, string? ProjectId = null, string? SourceAssetId = null, bool AttachPattern = false,
    bool AttachScreenshots = false, int? ScreenWidth = null, int? ScreenHeight = null, string? ErrorToken = null,
    string? ClientOccurrenceId = null);
public record FeedbackContext(string ProjectId, string PatternRevisionId, string? ImportRunId, string? SourceAssetId);
public record SafeErrorContext(Guid UserId, string Code, string ReferenceId, DateTimeOffset ExpiresAt);

public sealed class BetaService(StitchDbContext db, BetaOptions options, IDataProtectionProvider protection, IConfiguration configuration, IHostEnvironment environment)
{
    private static readonly SemaphoreSlim ScreenshotDecoders = new(2);
    public const string AdminRole = "BetaAdmin";
    private IDataProtector ErrorProtector => protection.CreateProtector("beta-error-context-v1");
    public Task<bool> IsAdmin(Guid user) => db.UserRoles.AnyAsync(ur => ur.UserId == user && db.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == "BETAADMIN"));
    public async Task RequireAdmin(Guid user)
    {
        if (!await IsAdmin(user)) throw new UserError("Administrator access is required.", 403);
    }
    public async Task<bool> HasAccess(Guid user)
    {
        if (await IsAdmin(user)) return true;
        var configured = configuration.GetSection("Beta:AllowedEmails").Get<string[]>() ?? [];
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
            if (configured.Length == 0) return true;
        var email = await db.Users.Where(u => u.Id == user).Select(u => u.Email).SingleOrDefaultAsync();
        return email is not null && configured.Any(e => e.Trim().Equals(email, StringComparison.OrdinalIgnoreCase));
    }
    public async Task RequireAccess(Guid user)
    {
        if (!await HasAccess(user)) throw new UserError("Beta access is required. Contact the person who invited you.", 403);
    }
    public string ProtectError(Guid user, string code, string reference) => ErrorProtector.Protect(Json.Write(new SafeErrorContext(user, code, reference, DateTimeOffset.UtcNow.AddDays(1))));
    public static string Route(string? route) => route is "library" or "workspace" or "inventory" or "backups" or "whats-new" or "error" or "admin" ? route : "unknown";
    public async Task<FeedbackContext> Context(Guid user, string projectId)
    {
        var p = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId && p.UserId == user) ?? throw Repository.Missing();
        var pattern = await db.Patterns.AsNoTracking().SingleAsync(pat => pat.Id == p.PatternId && pat.UserId == user);
        var import = await db.Imports.AsNoTracking().Where(i => i.UserId == user && i.PatternId == pattern.Id).OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id).FirstOrDefaultAsync();
        return new(p.Id, $"{p.PatternId}:{p.DataRevision}", import?.Id, pattern.AssetId);
    }
    public async Task<FeedbackSubmission> Submit(Guid user, FeedbackInput input, IReadOnlyList<byte[]> files, string? userAgent)
    {
        if (input.SubmissionKey == Guid.Empty || input.Message is null || input.Message.Trim().Length < options.MessageMinLength || input.Message.Trim().Length > options.MessageMaxLength)
            throw new UserError($"Write between {options.MessageMinLength} and {options.MessageMaxLength} characters.");
        if (input.Category is not ("Bug" or "FeatureRequest" or "ConfusingExperience" or "Other")) throw new UserError("Choose a feedback category.");
        if (input.DiagnosticsDisclosureVersion != BetaOptions.DisclosureVersion) throw new UserError("Reload the feedback form to review the current disclosure.", 409);
        if (files.Count > options.ScreenshotCount || files.Count > 0 && !input.AttachScreenshots || input.AttachScreenshots && files.Count == 0)
            throw new UserError("Choose screenshots explicitly, or uncheck the screenshot choice to submit without them.");
        var hash = StorageKeys.Hash(System.Text.Encoding.UTF8.GetBytes(Json.Write(input) + string.Join(',', files.Select(StorageKeys.Hash))));
        await using var tx = await db.Database.BeginTransactionAsync();
        ContentLifetime.Lock(db, user);
        var previous = await db.Set<FeedbackSubmission>().SingleOrDefaultAsync(f => f.UserId == user && f.SubmissionKey == input.SubmissionKey);
        if (previous is not null)
        {
            if (previous.PayloadHash != hash) throw new UserError("This submission key already has a receipt. Start a new report for different content.", 409);
            return previous;
        }
        var context = input.ProjectId is null ? null : await Context(user, input.ProjectId);
        if (input.AttachPattern && (context?.SourceAssetId is null || context.SourceAssetId != input.SourceAssetId))
            throw new UserError("This exact source is no longer available. Uncheck the pattern attachment to send text, or reopen the report.", 409);
        var screenshots = new List<byte[]>();
        foreach (var bytes in files) screenshots.Add(SanitizeScreenshot(bytes));
        if (screenshots.Count > 0)
        {
            var sources = await db.Assets.Where(a => a.UserId == user).SumAsync(a => a.ByteSize);
            var used = await db.Set<FeedbackAttachment>().Where(a => a.Screenshot != null && db.Set<FeedbackSubmission>().Any(f => f.Id == a.FeedbackSubmissionId && f.UserId == user)).SumAsync(a => (long)a.Screenshot!.Length);
            if (sources + used + screenshots.Sum(b => (long)b.Length) > options.StorageQuotaBytes)
                throw new UserError("Your storage quota is full. Remove screenshots to send text only, or free storage and retry.", 413);
        }
        var report = new FeedbackSubmission { UserId = user, SubmissionKey = input.SubmissionKey, PayloadHash = hash,
            Category = input.Category, Message = input.Message.Trim(), Route = Route(input.Route), AppVersion = options.AppVersion,
            DiagnosticsIncluded = input.DiagnosticsIncluded, DiagnosticsDisclosureVersion = input.DiagnosticsDisclosureVersion,
            ProjectId = context?.ProjectId, PatternRevisionId = context?.PatternRevisionId, ImportRunId = context?.ImportRunId };
        // PostgreSQL stores microseconds; return the same receipt timestamp before and after a retry.
        report.CreatedAt = new DateTimeOffset(report.CreatedAt.Ticks / 10 * 10, TimeSpan.Zero);
        report.UpdatedAt = report.CreatedAt;
        if (input.DiagnosticsIncluded)
        {
            report.BrowserMetadata = userAgent is null ? null : userAgent[..Math.Min(userAgent.Length, 512)];
            if (input.ScreenWidth is > 0 and <= 32768 && input.ScreenHeight is > 0 and <= 32768) report.ScreenMetadata = $"{input.ScreenWidth}x{input.ScreenHeight}";
            if (context?.ImportRunId is not null)
            {
                var import = await db.Imports.SingleAsync(i => i.Id == context.ImportRunId && i.UserId == user);
                report.ImportMetadata = Json.Write(new { import.Status, import.CreatedAt, import.ParserVersion });
            }
            if (input.ErrorToken is not null)
            {
                SafeErrorContext safe;
                try { safe = Json.Read<SafeErrorContext>(ErrorProtector.Unprotect(input.ErrorToken)); }
                catch (Exception ex) when (ex is CryptographicException or System.Text.Json.JsonException) { throw new UserError("The error reference expired. Reopen the report or decline technical diagnostics."); }
                if (safe.UserId != user || safe.ExpiresAt < DateTimeOffset.UtcNow) throw new UserError("The error reference is unavailable for this account. Reopen the report or decline diagnostics.");
                report.ErrorCode = safe.Code; report.CorrelationId = safe.ReferenceId;
            }
            else if (input.ClientOccurrenceId?.StartsWith("client-") == true && Guid.TryParse(input.ClientOccurrenceId[7..], out var occurrence))
            { report.ErrorCode = "CLIENT_FAILURE"; report.CorrelationId = "client-" + occurrence; }
        }
        db.Add(report);
        if (input.AttachPattern)
        {
            report.PatternAttachmentConsentAt = DateTimeOffset.UtcNow;
            db.Add(new FeedbackAttachment { FeedbackSubmissionId = report.Id, Kind = "ExistingPatternReference", AssetId = context!.SourceAssetId });
        }
        foreach (var bytes in screenshots) db.Add(new FeedbackAttachment { FeedbackSubmissionId = report.Id, Screenshot = bytes });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return report;
    }
    public byte[] SanitizeScreenshot(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > options.ScreenshotMaxBytes) throw new UserError("A screenshot exceeds the configured byte limit.", 413);
        if (!ScreenshotDecoders.Wait(0)) throw new UserError("Screenshot processing is busy. Keep your draft open and retry shortly.", 503);
        try
        {
            var format = Image.DetectFormat(bytes);
            if (format.Name is not ("PNG" or "JPEG")) throw new UserError("Screenshots must be PNG or JPEG.");
            var decoder = new DecoderOptions { SkipMetadata = true, MaxFrames = 1 };
            var info = Image.Identify(decoder, bytes);
            if ((long)info.Width * info.Height > options.ScreenshotMaxPixels) throw new UserError("A screenshot exceeds the decoded pixel limit.", 413);
            using var decoded = Image.Load<Rgba32>(decoder, bytes);
            // Copy pixels into a fresh image, dropping all source and frame metadata.
            using var clean = new Image<Rgba32>(decoded.Width, decoded.Height);
            decoded.ProcessPixelRows(clean, (source, target) => { for (var y = 0; y < source.Height; y++) source.GetRowSpan(y).CopyTo(target.GetRowSpan(y)); });
            using var output = new MemoryStream(); clean.SaveAsPng(output);
            if (output.Length > options.ScreenshotMaxBytes) throw new UserError("The cleaned screenshot exceeds the byte limit. Choose a smaller image.", 413);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        { throw new UserError("This screenshot could not be decoded safely. Choose a valid PNG or JPEG, or remove it to submit text only."); }
        finally { ScreenshotDecoders.Release(); }
    }
    public async Task SetStatus(Guid admin, string id, string status)
    {
        await RequireAdmin(admin);
        if (status is not ("Unread" or "Read" or "Completed" or "Archived")) throw new UserError("Choose a valid feedback status.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"feedback:" + id}, 0))");
        var report = await db.Set<FeedbackSubmission>().SingleOrDefaultAsync(f => f.Id == id) ?? throw Repository.Missing();
        if (status == "Completed" && report.CompletedAt is null) { report.CompletedAt = DateTimeOffset.UtcNow; report.CompletedByAdminUserId = admin; }
        if (status is "Unread" or "Read") { report.CompletedAt = null; report.CompletedByAdminUserId = null; }
        report.Status = status; report.UpdatedAt = DateTimeOffset.UtcNow; Audit(admin, "Feedback" + status, id);
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public void Audit(Guid admin, string action, string target) => db.Add(new BetaAuditEvent { AdminUserId = admin, Action = action, TargetId = target });
    public async Task<BetaAnnouncement?> Announcement(Guid user)
    {
        var now = DateTimeOffset.UtcNow;
        var latest = await db.Set<BetaAnnouncement>().AsNoTracking().Where(a => a.Active && a.PublishedAt <= now && (a.StartsAt == null || a.StartsAt <= now) && (a.EndsAt == null || a.EndsAt > now)).OrderByDescending(a => a.PublishedAt).ThenByDescending(a => a.Id).FirstOrDefaultAsync();
        return latest is not null && (!latest.Dismissible || !await db.Set<AnnouncementDismissal>().AnyAsync(d => d.UserId == user && d.AnnouncementId == latest.Id)) ? latest : null;
    }
    public static bool SafeLink(string? url) => string.IsNullOrWhiteSpace(url) || url == "/whats-new" ||
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo == "" && !url.Any(char.IsControl);
}
