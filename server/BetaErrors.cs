namespace StitchHelper;

public static class BetaErrors
{
    public static string Code(int status) => status switch { 400 => "VALIDATION_FAILED", 401 => "SESSION_EXPIRED", 403 => "ACCESS_REQUIRED", 404 => "NOT_FOUND", 409 => "CONFLICT", 413 => "LIMIT_EXCEEDED", 428 => "LEGAL_ACCEPTANCE_REQUIRED", 503 => "UNAVAILABLE", _ => "UNEXPECTED_FAILURE" };
    public static string Message(int status) => status switch
    {
        400 => "Check the entered details and try again.", 401 => "Your session expired. Sign in again, then retry.",
        403 => "Access is required. Contact the person who invited you to the beta.", 404 => "This item could not be found. Return to your projects.",
        409 => "This item changed. Reload its saved state before editing again.", 413 => "The upload or storage limit was reached. Remove the attachment or choose a smaller file.",
        428 => "Review and accept the updated legal documents to continue.", _ => "The request could not be completed. Keep this tab open and retry."
    };
    public static async Task Write(HttpContext context, int status, string? message = null)
    {
        var options = context.RequestServices.GetRequiredService<BetaOptions>();
        var code = Code(status); var referenceId = Guid.NewGuid().ToString("N"); string? errorToken = null;
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var user = context.RequestServices.GetRequiredService<ICurrentUserContext>();
            errorToken = context.RequestServices.GetRequiredService<BetaService>().ProtectError(user.UserId, code, referenceId);
        }
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("BetaErrors")
            .LogWarning("Request failed {ErrorCode} {ReferenceId} {AppVersion}", code, referenceId, options.AppVersion);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error = message ?? Message(status), code, referenceId, errorToken, appVersion = options.AppVersion });
    }
}
