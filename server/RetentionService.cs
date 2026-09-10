using Microsoft.EntityFrameworkCore;

namespace StitchHelper;

public sealed class RetentionService(StitchDbContext db, IPatternAssetStore sources, IBackupArtifactStore backups, ILogger<RetentionService> logger)
{
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan PurgeTarget = TimeSpan.FromHours(24);

    public void DeleteAccount(Guid owner)
    {
        using var tx = db.Database.BeginTransaction();
        ContentLifetime.Lock(db, owner);
        ContentLifetime.InvalidateBackups(db, owner);
        db.Projects.Where(p => p.UserId == owner).ExecuteDelete();
        ContentLifetime.RemoveUnreferenced(db, owner);
        db.Users.Where(u => u.Id == owner).ExecuteDelete(); // Identity, inventory and preferences cascade.
        tx.Commit(); db.ChangeTracker.Clear();
    }

    public async Task Maintain(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var owner in await db.Users.Select(u => u.Id).ToListAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                ContentLifetime.Lock(db, owner);
                if (ContentLifetime.RemoveUnreferenced(db, owner)) ContentLifetime.InvalidateBackups(db, owner);
                foreach (var backup in await db.Backups.Where(b => b.UserId == owner &&
                    (b.Kind == "daily" ? b.CreatedAt <= now.AddDays(-7) : b.CreatedAt <= now.AddDays(-30))).ToListAsync(ct))
                {
                    ContentLifetime.Queue(db, "backup", backup.StorageKey);
                    db.Backups.Remove(backup);
                }
                await db.BackupJobs.Where(b => b.UserId == owner && b.CompletedAt < now.AddDays(-35)).ExecuteDeleteAsync(ct);
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            }
            catch (UserError ex) when (ex.Status == 401) { await tx.RollbackAsync(ct); } // Account was deleted after enumeration.
            finally { db.ChangeTracker.Clear(); }
        }
        await Cleanup(ct);
        TemporaryFiles.Cleanup();
    }

    public async Task Cleanup(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        // Bounded batches; SKIP LOCKED allows multiple workers without double-deleting a queue item.
        for (var i = 0; i < 500; i++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var item = await db.PendingObjectDeletions.FromSqlInterpolated($"SELECT * FROM \"PendingObjectDeletions\" WHERE \"NotBefore\" <= {now} ORDER BY \"NotBefore\" LIMIT 1 FOR UPDATE SKIP LOCKED").AsTracking().SingleOrDefaultAsync(ct);
            if (item is null) break;
            try
            {
                var referenced = item.StoreKind == "source"
                    ? await db.Assets.AnyAsync(a => a.StorageKey == item.StorageKey, ct)
                    : await db.Backups.AnyAsync(b => b.StorageKey == item.StorageKey, ct);
                if (!referenced)
                {
                    IPrivateObjectStore store = item.StoreKind switch { "source" => sources, "backup" => backups, _ => throw new InvalidDataException("Unknown deletion store.") };
                    await store.Delete(item.StorageKey, ct);
                }
                db.PendingObjectDeletions.Remove(item);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                item.Attempts++; item.NotBefore = now + RetryInterval;
                logger.LogWarning("Object cleanup failed ({StoreKind}, attempt {Attempts}, {FailureType}); retry scheduled", item.StoreKind, item.Attempts, ex.GetType().Name);
                if (now - item.QueuedAt >= PurgeTarget) logger.LogError("Object deletion is overdue; operator action required ({StoreKind})", item.StoreKind);
            }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); db.ChangeTracker.Clear();
        }
    }

    public async Task TryCleanupAfterDeletion(CancellationToken ct)
    {
        try { await Cleanup(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { } // Committed deletion must not depend on the client staying connected.
        catch (Exception ex) { logger.LogWarning("Post-delete cleanup deferred ({FailureType}); the durable worker will retry", ex.GetType().Name); }
    }
}

public sealed class RetentionWorker(IServiceScopeFactory scopes, ILogger<RetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<RetentionService>().Maintain(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Retention maintenance failed ({FailureType}); retrying in five minutes", ex.GetType().Name); }
            await Task.Delay(RetentionService.RetryInterval, stoppingToken);
        }
    }
}
