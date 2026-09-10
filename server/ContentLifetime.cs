using Microsoft.EntityFrameworkCore;

namespace StitchHelper;

// The same PostgreSQL lock coordinates deletion, creation and backup publication across instances.
public static class ContentLifetime
{
    public static void Lock(StitchDbContext db, Guid owner)
    {
        db.Database.ExecuteSqlInterpolated($"SELECT pg_advisory_xact_lock(hashtextextended({"content:" + owner}, 0))");
        if (!db.Users.Any(u => u.Id == owner)) throw new UserError("Sign in again.", 401);
    }

    public static async Task<IAsyncDisposable> Acquire(StitchDbContext db, Guid owner, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        var acquired = false;
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock(hashtextextended({"content:" + owner}, 0))", ct);
            acquired = true;
            if (!await db.Users.AnyAsync(u => u.Id == owner, ct))
                throw new UserError("Sign in again.", 401);
            return new Lease(db, owner);
        }
        catch
        {
            try { if (acquired) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock(hashtextextended({"content:" + owner}, 0))", CancellationToken.None); }
            finally { await db.Database.CloseConnectionAsync(); }
            throw;
        }
    }

    private sealed class Lease(StitchDbContext db, Guid owner) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock(hashtextextended({"content:" + owner}, 0))", CancellationToken.None); }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }

    public static void Queue(StitchDbContext db, string kind, string key, DateTimeOffset? notBefore = null)
    {
        StorageKeys.Validate(key);
        var now = DateTimeOffset.UtcNow;
        var due = notBefore ?? now;
        db.Database.ExecuteSqlInterpolated($"INSERT INTO \"PendingObjectDeletions\" (\"StoreKind\", \"StorageKey\", \"QueuedAt\", \"NotBefore\", \"Attempts\") VALUES ({kind}, {key}, {now}, {due}, 0) ON CONFLICT (\"StoreKind\", \"StorageKey\") DO UPDATE SET \"NotBefore\" = LEAST(\"PendingObjectDeletions\".\"NotBefore\", EXCLUDED.\"NotBefore\")");
    }

    public static void InvalidateBackups(StitchDbContext db, Guid owner)
    {
        foreach (var backup in db.Backups.AsNoTracking().Where(b => b.UserId == owner).ToList()) Queue(db, "backup", backup.StorageKey);
        db.Backups.Where(b => b.UserId == owner).ExecuteDelete();
        // Permit a clean replacement in the same schedule window.
        db.BackupJobs.Where(b => b.UserId == owner).ExecuteDelete();
    }

    public static bool RemoveUnreferenced(StitchDbContext db, Guid owner)
    {
        var patterns = db.Patterns.Where(p => p.UserId == owner && !db.Projects.Any(j => j.PatternId == p.Id));
        var changed = patterns.ExecuteDelete() > 0; // Import rows cascade with their pattern.
        foreach (var asset in db.Assets.AsNoTracking().Where(a => a.UserId == owner && !db.Patterns.Any(p => p.AssetId == a.Id)).ToList())
        {
            Queue(db, "source", asset.StorageKey);
            db.Assets.Where(a => a.Id == asset.Id && a.UserId == owner).ExecuteDelete();
            changed = true;
        }
        return changed;
    }
}
