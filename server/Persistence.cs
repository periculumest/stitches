using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace StitchHelper;

public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class OwnedPattern
{
    public string Id { get; set; } = "";
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string? AssetId { get; set; }
    public string DataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class OwnedProject
{
    public string Id { get; set; } = "";
    public Guid UserId { get; set; }
    public string PatternId { get; set; } = "";
    public string DataJson { get; set; } = "{}";
    // Small project metadata/history only. Chart and stitch state have separate persistence.
    public string StateJson { get; set; } = "{}";
    public long Revision { get; set; }
    public long DataRevision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class ProjectStitchState
{
    public string ProjectId { get; set; } = "";
    public string StitchId { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class ProgressMutation
{
    public string ProjectId { get; set; } = "";
    public Guid RequestId { get; set; }
    public string PayloadHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class PatternSourceAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Guid UserId { get; set; }
    public string StorageKey { get; set; } = Guid.NewGuid().ToString("N");
    public string OriginalFileName { get; set; } = "original.pdf";
    public string MediaType { get; set; } = "application/pdf";
    public long ByteSize { get; set; }
    public string Sha256 { get; set; } = "";
}
public class CatalogItem
{
    public string Code { get; set; } = "";
    public string Json { get; set; } = "{}";
}
public class UserInventory
{
    public Guid UserId { get; set; }
    public string Code { get; set; } = "";
    public int BobbinCount { get; set; }
    public string Location { get; set; } = "";
    public long Revision { get; set; }
}
public class UserPreferences
{
    public Guid UserId { get; set; }
    public string Json { get; set; } = "{}";
    public long Revision { get; set; }
}
public class ImportRecord
{
    public string ParserVersion { get; set; } = "unknown";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Guid UserId { get; set; }
    public string PatternId { get; set; } = "";
    public string Status { get; set; } = "review";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class BackupArtifact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "daily";
    public DateOnly ScheduleDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string StorageKey { get; set; } = Guid.NewGuid().ToString("N");
    public long ByteSize { get; set; }
    public string Sha256 { get; set; } = "";
}
public class BackupJob
{
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "";
    public DateOnly ScheduleDate { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}
public class PendingObjectDeletion
{
    public string StorageKey { get; set; } = "";
    public string StoreKind { get; set; } = "backup";
    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset NotBefore { get; set; } = DateTimeOffset.UtcNow;
    public int Attempts { get; set; }
}

public class StitchDbContext(DbContextOptions<StitchDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<OwnedPattern> Patterns => Set<OwnedPattern>();
    public DbSet<OwnedProject> Projects => Set<OwnedProject>();
    public DbSet<ProjectStitchState> StitchStates => Set<ProjectStitchState>();
    public DbSet<ProgressMutation> ProgressMutations => Set<ProgressMutation>();
    public DbSet<PatternSourceAsset> Assets => Set<PatternSourceAsset>();
    public DbSet<CatalogItem> Catalog => Set<CatalogItem>();
    public DbSet<UserInventory> Inventory => Set<UserInventory>();
    public DbSet<UserPreferences> Preferences => Set<UserPreferences>();
    public DbSet<ImportRecord> Imports => Set<ImportRecord>();
    public DbSet<BackupArtifact> Backups => Set<BackupArtifact>();
    public DbSet<BackupJob> BackupJobs => Set<BackupJob>();
    public DbSet<PendingObjectDeletion> PendingObjectDeletions => Set<PendingObjectDeletion>();
    public DbSet<LegalDocument> LegalDocuments => Set<LegalDocument>();
    public DbSet<LegalVersion> LegalVersions => Set<LegalVersion>();
    public DbSet<LegalAcceptance> LegalAcceptances => Set<LegalAcceptance>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        BetaSchema.Configure(b);
        b.Entity<LegalDocument>().HasKey(d => d.Slug);
        b.Entity<LegalDocument>().Property(d => d.DraftRevision).IsConcurrencyToken();
        b.Entity<LegalVersion>().HasIndex(v => new { v.DocumentSlug, v.Version }).IsUnique();
        b.Entity<LegalVersion>().HasOne<LegalDocument>().WithMany().HasForeignKey(v => v.DocumentSlug).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LegalAcceptance>().HasKey(a => new { a.UserId, a.VersionId });
        b.Entity<LegalAcceptance>().HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<LegalAcceptance>().HasOne<LegalVersion>().WithMany().HasForeignKey(a => a.VersionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<OwnedPattern>().HasAlternateKey(x => new { x.UserId, x.Id });
        b.Entity<OwnedPattern>().Property(x => x.DataJson).HasColumnType("jsonb");
        b.Entity<OwnedPattern>().HasOne<PatternSourceAsset>().WithMany().HasForeignKey(x => new { x.UserId, x.AssetId }).HasPrincipalKey(x => new { x.UserId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<OwnedProject>().HasOne<OwnedPattern>().WithMany().HasForeignKey(x => new { x.UserId, x.PatternId }).HasPrincipalKey(x => new { x.UserId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<OwnedProject>().Property(x => x.StateJson).HasColumnType("jsonb");
        b.Entity<OwnedProject>().Property(x => x.DataJson).HasColumnType("jsonb");
        b.Entity<OwnedProject>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<ProjectStitchState>().HasKey(x => new { x.ProjectId, x.StitchId });
        b.Entity<ProjectStitchState>().HasOne<OwnedProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProgressMutation>().HasKey(x => new { x.ProjectId, x.RequestId });
        b.Entity<ProgressMutation>().HasOne<OwnedProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PatternSourceAsset>().HasAlternateKey(x => new { x.UserId, x.Id });
        b.Entity<PatternSourceAsset>().HasIndex(x => x.StorageKey).IsUnique();
        b.Entity<CatalogItem>().HasKey(x => x.Code);
        b.Entity<CatalogItem>().Property(x => x.Json).HasColumnType("jsonb");
        b.Entity<UserInventory>().HasKey(x => new { x.UserId, x.Code });
        b.Entity<UserInventory>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<UserInventory>().HasOne<CatalogItem>().WithMany().HasForeignKey(x => x.Code).OnDelete(DeleteBehavior.Restrict);
        b.Entity<UserPreferences>().HasKey(x => x.UserId);
        b.Entity<UserPreferences>().Property(x => x.Json).HasColumnType("jsonb");
        b.Entity<UserPreferences>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<ImportRecord>().HasOne<OwnedPattern>().WithMany().HasForeignKey(x => new { x.UserId, x.PatternId }).HasPrincipalKey(x => new { x.UserId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.Entity<BackupArtifact>().HasIndex(x => new { x.UserId, x.Kind }).IsUnique();
        b.Entity<BackupJob>().HasKey(x => new { x.UserId, x.Kind, x.ScheduleDate });
        b.Entity<PendingObjectDeletion>().HasKey(x => new { x.StoreKind, x.StorageKey });
        b.Entity<PendingObjectDeletion>().HasIndex(x => x.NotBefore);
        foreach (var type in new[] { typeof(OwnedPattern), typeof(OwnedProject), typeof(PatternSourceAsset), typeof(UserInventory), typeof(UserPreferences), typeof(ImportRecord), typeof(BackupArtifact), typeof(BackupJob) })
        {
            b.Entity(type).HasOne(typeof(ApplicationUser)).WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade);
            b.Entity(type).HasIndex("UserId");
        }
    }

    public void SeedCatalog()
    {
        foreach (var thread in ThreadCatalog.Load(Path.Combine(AppContext.BaseDirectory, "rgb-dmc.json")))
        {
            var json = StitchHelper.Json.Write(thread);
            Database.ExecuteSqlInterpolated($"INSERT INTO \"Catalog\" (\"Code\", \"Json\") VALUES ({thread.Code}, {json}::jsonb) ON CONFLICT (\"Code\") DO UPDATE SET \"Json\" = EXCLUDED.\"Json\"");
        }
    }
}
