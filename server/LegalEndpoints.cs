namespace StitchHelper;

public static class LegalEndpoints
{
    public static bool ExemptFromAcceptance(HttpRequest request) =>
        request.Path.StartsWithSegments("/api/legal") || request.Path.StartsWithSegments("/api/admin/legal") ||
        request.Path == "/api/me" || request.Path == "/api/antiforgery" ||
        request.Path == "/api/exports/current" && request.Method == "POST" || request.Path == "/api/account" && request.Method == "DELETE";

    public static void MapLegalDocuments(this WebApplication app)
    {
        var publicDocs = app.MapGroup("/api/legal/documents").AllowAnonymous();
        publicDocs.MapGet("", (LegalDocumentService service, CancellationToken ct) => service.Published(ct));
        publicDocs.MapGet("/{slug}", (string slug, LegalDocumentService service, CancellationToken ct) => service.PublishedVersion(slug, null, ct));
        publicDocs.MapGet("/{slug}/versions", (string slug, LegalDocumentService service, CancellationToken ct) => service.Versions(slug, ct));
        publicDocs.MapGet("/{slug}/versions/{version:int}", (string slug, int version, LegalDocumentService service, CancellationToken ct) => service.PublishedVersion(slug, version, ct));
        var own = app.MapGroup("/api/legal").RequireAuthorization();
        own.MapGet("/status", (ICurrentUserContext user, LegalDocumentService service, CancellationToken ct) => service.Status(user.UserId, ct));
        own.MapGet("/history", (ICurrentUserContext user, LegalDocumentService service, CancellationToken ct) => service.History(user.UserId, ct));
        own.MapPost("/accept", async (LegalAcceptInput input, ICurrentUserContext user, LegalDocumentService service, CancellationToken ct) =>
        {
            await service.Accept(user.UserId, input, ct); return await service.Status(user.UserId, ct);
        });
        var admin = app.MapGroup("/api/admin/legal").RequireAuthorization();
        admin.MapGet("/drafts", (ICurrentUserContext user, LegalDocumentService service, CancellationToken ct) => service.Drafts(user.UserId, ct));
        admin.MapPost("/drafts/{slug}", (string slug, LegalDraftInput input, ICurrentUserContext user, LegalDocumentService service, CancellationToken ct) => service.Save(user.UserId, slug, input, true, ct));
        admin.MapPut("/drafts/{slug}", (string slug, LegalDraftInput input, ICurrentUserContext user, LegalDocumentService service, CancellationToken ct) => service.Save(user.UserId, slug, input, false, ct));
        admin.MapPost("/drafts/{slug}/publish", (string slug, LegalPublishInput input, ICurrentUserContext user, LegalDocumentService service, CancellationToken ct) => service.Publish(user.UserId, slug, input, ct));
        admin.MapPost("/audit", (LegalAuditInput input, ICurrentUserContext user, LegalDocumentService service, CancellationToken ct) => service.Audit(user.UserId, input.Query, ct));
    }
}
public record LegalAuditInput(string Query);
