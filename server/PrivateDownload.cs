namespace StitchHelper;

/// <summary>No Content-Length: stream archives through HTTP/1 ingress even when larger than 32 MiB.</summary>
public sealed class PrivateDownload(Stream content, string filename) : IResult
{
    public async Task ExecuteAsync(HttpContext context)
    {
        await using (content)
        {
            context.Response.ContentType = "application/zip";
            context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";
            context.Response.Headers.CacheControl = "no-store";
            await content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
    }
}
