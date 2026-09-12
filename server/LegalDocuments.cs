using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace StitchHelper;

public class LegalDocument
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string ChangeSummary { get; set; } = "";
    public string AcceptanceText { get; set; } = "";
    public bool RequiresAcceptance { get; set; }
    public bool RequireReacceptance { get; set; } = true;
    public long DraftRevision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class LegalVersion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DocumentSlug { get; set; } = "";
    public int Version { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string ChangeSummary { get; set; } = "";
    public string AcceptanceText { get; set; } = "";
    public bool RequiresAcceptance { get; set; }
    public int AcceptanceGeneration { get; set; }
    public string ContentSha256 { get; set; } = "";
    public DateTimeOffset PublishedAt { get; set; }
}

public class LegalAcceptance
{
    public Guid UserId { get; set; }
    public string VersionId { get; set; } = "";
    public string ContentSha256 { get; set; } = "";
    public DateTimeOffset AcceptedAt { get; set; }
}

public record LegalDraftInput(long Revision, string Title, string Body, string ChangeSummary, string AcceptanceText, bool RequiresAcceptance, bool RequireReacceptance = true);
public record LegalPublishInput(long Revision);
public record LegalAcceptanceInput(string VersionId, string ContentSha256);
public record LegalAcceptInput(List<LegalAcceptanceInput> Documents);
public record LegalVersionSummary(string Id, string DocumentSlug, int Version, string Title, string ChangeSummary, string AcceptanceText,
    bool RequiresAcceptance, int AcceptanceGeneration, string ContentSha256, DateTimeOffset PublishedAt)
{
    public static LegalVersionSummary From(LegalVersion v) => new(v.Id, v.DocumentSlug, v.Version, v.Title, v.ChangeSummary, v.AcceptanceText, v.RequiresAcceptance, v.AcceptanceGeneration, v.ContentSha256, v.PublishedAt);
}
public record LegalAcceptanceSummary(string VersionId, int Version, int AcceptanceGeneration, string ContentSha256, DateTimeOffset AcceptedAt);
public record LegalDocumentState(LegalVersionSummary Current, LegalAcceptanceSummary? LastAcceptance, bool NeedsAcceptance);
public record LegalStatus(bool IsEditor, List<LegalDocumentState> Documents);
public record LegalAcceptanceEvidence(LegalVersion Document, DateTimeOffset AcceptedAt, string ContentSha256);

public sealed class LegalDocumentService(StitchDbContext db)
{
    public const string EditorRole = "LegalEditor";
    public Task<bool> IsEditor(Guid user, CancellationToken ct = default) => db.UserRoles.AnyAsync(ur => ur.UserId == user && db.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == "LEGALEDITOR"), ct);
    private IQueryable<LegalVersion> CurrentVersions => db.LegalVersions.AsNoTracking().Where(v => !db.LegalVersions.Any(newer => newer.DocumentSlug == v.DocumentSlug && newer.Version > v.Version));
    private Task Lock(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(81764002)", ct);
    private async Task RequireEditor(Guid user, CancellationToken ct)
    {
        if (!await IsEditor(user, ct)) throw new UserError("Legal document editing requires editor access.", 403);
    }

    public async Task<List<LegalVersionSummary>> Published(CancellationToken ct = default) => (await CurrentVersions.OrderBy(v => v.DocumentSlug).ToListAsync(ct)).Select(LegalVersionSummary.From).ToList();
    public Task<LegalVersion> PublishedVersion(string slug, int? version, CancellationToken ct = default) => PublishedVersionCore(slug, version, ct);
    private async Task<LegalVersion> PublishedVersionCore(string slug, int? version, CancellationToken ct) =>
        await db.LegalVersions.AsNoTracking().Where(v => v.DocumentSlug == slug && (version == null || v.Version == version)).OrderByDescending(v => v.Version).FirstOrDefaultAsync(ct) ?? throw Repository.Missing();
    public async Task<List<LegalVersionSummary>> Versions(string slug, CancellationToken ct = default) =>
        (await db.LegalVersions.AsNoTracking().Where(v => v.DocumentSlug == slug).OrderByDescending(v => v.Version).ToListAsync(ct)).Select(LegalVersionSummary.From).ToList();
    public Task<bool> HasPending(Guid user, CancellationToken ct = default) => CurrentVersions.AnyAsync(v => v.RequiresAcceptance &&
        !db.LegalAcceptances.Any(a => a.UserId == user && db.LegalVersions.Any(accepted => accepted.Id == a.VersionId && accepted.DocumentSlug == v.DocumentSlug && accepted.AcceptanceGeneration == v.AcceptanceGeneration)), ct);

    public async Task<LegalStatus> Status(Guid user, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var current = await Published(ct);
        var accepted = await (from a in db.LegalAcceptances.AsNoTracking() join v in db.LegalVersions on a.VersionId equals v.Id
                              where a.UserId == user orderby v.Version descending
                              select new { v.DocumentSlug, Receipt = new LegalAcceptanceSummary(v.Id, v.Version, v.AcceptanceGeneration, a.ContentSha256, a.AcceptedAt) }).ToListAsync(ct);
        var result = new LegalStatus(await IsEditor(user, ct), current.Select(v => {
            var last = accepted.FirstOrDefault(a => a.DocumentSlug == v.DocumentSlug)?.Receipt;
            return new LegalDocumentState(v, last, v.RequiresAcceptance && last?.AcceptanceGeneration != v.AcceptanceGeneration);
        }).ToList());
        await tx.CommitAsync(ct); return result;
    }

    public Task<List<LegalAcceptanceEvidence>> History(Guid user, CancellationToken ct = default) =>
        (from a in db.LegalAcceptances.AsNoTracking() join v in db.LegalVersions.AsNoTracking() on a.VersionId equals v.Id
         where a.UserId == user orderby a.AcceptedAt descending, v.Version descending
         select new LegalAcceptanceEvidence(v, a.AcceptedAt, a.ContentSha256)).ToListAsync(ct);

    public async Task Accept(Guid user, LegalAcceptInput input, CancellationToken ct = default)
    {
        if (input.Documents is null || input.Documents.Count is < 1 or > 50 || input.Documents.Any(d => d is null) || input.Documents.Select(d => d.VersionId).Distinct().Count() != input.Documents.Count)
            throw new UserError("Select distinct document versions to accept.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        ContentLifetime.Lock(db, user);
        await Lock(ct); // Publication cannot change the presented versions while acceptance is committed.
        var current = await CurrentVersions.ToListAsync(ct);
        var selected = new List<LegalVersion>();
        foreach (var requested in input.Documents)
        {
            var version = current.SingleOrDefault(v => v.Id == requested.VersionId && v.ContentSha256 == requested.ContentSha256);
            if (version is null) throw new UserError("A legal document changed while you were reviewing it. Review the current version and try again.", 409);
            if (string.IsNullOrWhiteSpace(version.AcceptanceText)) throw new UserError("This document does not have an acceptance statement.");
            selected.Add(version);
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var version in selected)
            if (!await db.LegalAcceptances.AnyAsync(a => a.UserId == user && a.VersionId == version.Id, ct))
                db.LegalAcceptances.Add(new() { UserId = user, VersionId = version.Id, ContentSha256 = version.ContentSha256, AcceptedAt = now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task<List<LegalDocument>> Drafts(Guid editor, CancellationToken ct = default)
    {
        await RequireEditor(editor, ct); return await db.LegalDocuments.AsNoTracking().OrderBy(d => d.Slug).ToListAsync(ct);
    }
    public async Task<LegalDocument> Save(Guid editor, string slug, LegalDraftInput input, bool create, CancellationToken ct = default)
    {
        await RequireEditor(editor, ct);
        if (!Regex.IsMatch(slug, "^[a-z][a-z0-9-]{0,79}$") || string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 160 || input.Body is null || input.Body.Length > 250000 ||
            input.ChangeSummary is null || input.ChangeSummary.Length > 2000 || input.AcceptanceText is null || input.AcceptanceText.Length > 500)
            throw new UserError("Use a document slug, a title up to 160 characters, document text up to 250,000 characters, a short change summary and an acceptance statement up to 500 characters.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(ct);
        var draft = await db.LegalDocuments.SingleOrDefaultAsync(d => d.Slug == slug, ct);
        if (create && draft is not null) throw new UserError("A document with that identifier already exists.", 409);
        if (!create && draft is null) throw Repository.Missing();
        if (input.Revision != (draft?.DraftRevision ?? 0)) throw new UserError("Another editor changed this draft. Reload it before saving.", 409);
        if (draft is null) { draft = new() { Slug = slug }; db.LegalDocuments.Add(draft); }
        draft.Title = input.Title.Trim(); draft.Body = input.Body; draft.ChangeSummary = input.ChangeSummary.Trim(); draft.AcceptanceText = input.AcceptanceText.Trim();
        draft.RequiresAcceptance = input.RequiresAcceptance; draft.RequireReacceptance = input.RequireReacceptance;
        draft.DraftRevision++; draft.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return draft;
    }

    public async Task<LegalVersion> Publish(Guid editor, string slug, LegalPublishInput input, CancellationToken ct = default)
    {
        await RequireEditor(editor, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(ct);
        var draft = await db.LegalDocuments.SingleOrDefaultAsync(d => d.Slug == slug, ct) ?? throw Repository.Missing();
        if (draft.DraftRevision != input.Revision) throw new UserError("The draft changed. Reload and review it before publishing.", 409);
        if (string.IsNullOrWhiteSpace(draft.Body) || string.IsNullOrWhiteSpace(draft.ChangeSummary) || draft.RequiresAcceptance && string.IsNullOrWhiteSpace(draft.AcceptanceText))
            throw new UserError("Add the document text, a publication summary and, when required, an acceptance statement before publishing.");
        var previous = await db.LegalVersions.Where(v => v.DocumentSlug == slug).OrderByDescending(v => v.Version).FirstOrDefaultAsync(ct);
        var maxGeneration = await db.LegalVersions.Where(v => v.DocumentSlug == slug).Select(v => (int?)v.AcceptanceGeneration).MaxAsync(ct) ?? 0;
        var generation = draft.RequiresAcceptance ? previous?.RequiresAcceptance == true && !draft.RequireReacceptance ? previous.AcceptanceGeneration : maxGeneration + 1 : 0;
        var version = new LegalVersion { DocumentSlug = slug, Version = (previous?.Version ?? 0) + 1, Title = draft.Title, Body = draft.Body,
            ChangeSummary = draft.ChangeSummary, AcceptanceText = draft.AcceptanceText, RequiresAcceptance = draft.RequiresAcceptance, AcceptanceGeneration = generation, PublishedAt = DateTimeOffset.UtcNow };
        version.ContentSha256 = StorageKeys.Hash(Encoding.UTF8.GetBytes(Json.Write(new { version.DocumentSlug, version.Version, version.Title, version.Body, version.ChangeSummary, version.AcceptanceText, version.RequiresAcceptance, version.AcceptanceGeneration })));
        db.LegalVersions.Add(version);
        draft.DraftRevision++; // Stops duplicate publication/replays of a reviewed draft revision.
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return version;
    }

    public async Task<object> Audit(Guid editor, string query, CancellationToken ct = default)
    {
        await RequireEditor(editor, ct);
        if (string.IsNullOrWhiteSpace(query) || query.Length > 256) throw new UserError("Enter an account ID or exact email address.");
        var byId = Guid.TryParse(query.Trim(), out var userId); var email = query.Trim().ToUpperInvariant();
        var users = await db.Users.AsNoTracking().Where(u => byId ? u.Id == userId : u.NormalizedEmail == email).OrderBy(u => u.CreatedAt).Take(20).ToListAsync(ct);
        var result = new List<object>();
        foreach (var user in users) result.Add(new { user.Id, user.DisplayName, user.Email, status = await Status(user.Id, ct), history = await History(user.Id, ct) });
        return result;
    }
}
