using Microsoft.EntityFrameworkCore;

namespace StitchHelper;

public class FeedbackSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Guid UserId { get; set; }
    public Guid SubmissionKey { get; set; }
    public string PayloadHash { get; set; } = "";
    public string Category { get; set; } = "Other";
    public string Message { get; set; } = "";
    public string Status { get; set; } = "Unread";
    public bool DiagnosticsIncluded { get; set; }
    public int DiagnosticsDisclosureVersion { get; set; }
    public string Route { get; set; } = "library";
    public string AppVersion { get; set; } = "";
    public string? BrowserMetadata { get; set; }
    public string? ScreenMetadata { get; set; }
    public string? ProjectId { get; set; }
    public string? PatternRevisionId { get; set; }
    public string? ImportRunId { get; set; }
    public string? ImportMetadata { get; set; }
    public string? ErrorCode { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset? PatternAttachmentConsentAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CompletedByAdminUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class FeedbackAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FeedbackSubmissionId { get; set; } = "";
    public string Kind { get; set; } = "Screenshot";
    public string? AssetId { get; set; }
    // Bounded screenshots commit atomically with the report and cascade on account deletion.
    public byte[]? Screenshot { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class UserOnboardingState
{
    public Guid UserId { get; set; }
    public int CompletedOnboardingVersion { get; set; }
    public DateTimeOffset CompletedOrDismissedAt { get; set; }
}
public class BetaAnnouncement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? LinkText { get; set; }
    public string? LinkUrl { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public bool Active { get; set; }
    public bool Dismissible { get; set; } = true;
}
public class AnnouncementDismissal
{
    public string AnnouncementId { get; set; } = "";
    public Guid UserId { get; set; }
    public DateTimeOffset DismissedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class BetaRelease
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Version { get; set; } = "";
    public DateOnly ReleaseDate { get; set; }
    public string Title { get; set; } = "";
    public string BodyMarkdown { get; set; } = "";
    public DateTimeOffset? PublishedAt { get; set; }
}
public class BetaAuditEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Guid AdminUserId { get; set; }
    public string Action { get; set; } = "";
    public string TargetId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public static class BetaSchema
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<FeedbackSubmission>().HasIndex(x => new { x.UserId, x.SubmissionKey }).IsUnique();
        b.Entity<FeedbackSubmission>().HasIndex(x => new { x.Status, x.CreatedAt });
        b.Entity<FeedbackSubmission>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<FeedbackAttachment>().HasOne<FeedbackSubmission>().WithMany().HasForeignKey(x => x.FeedbackSubmissionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<FeedbackAttachment>().HasOne<PatternSourceAsset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<UserOnboardingState>().HasKey(x => x.UserId);
        b.Entity<UserOnboardingState>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AnnouncementDismissal>().HasKey(x => new { x.UserId, x.AnnouncementId });
        b.Entity<AnnouncementDismissal>().HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AnnouncementDismissal>().HasOne<BetaAnnouncement>().WithMany().HasForeignKey(x => x.AnnouncementId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<BetaRelease>(); b.Entity<BetaAuditEvent>();
    }
}
