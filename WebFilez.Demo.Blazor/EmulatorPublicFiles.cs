namespace WebFilez.Demo;

/// <summary>
/// Serves emulator objects on the demo's own origin. Azurite and fake-gcs-server are HTTP-only,
/// so an HTTPS page will not load their URLs. fake-gcs-server also does not serve
/// <c>/{bucket}/{object}</c>; its bytes are on the download API. Each root's PublicBaseUrl
/// points here: <c>/azurite/{container}</c> or <c>/gcs/{bucket}</c>.
/// </summary>
public static class EmulatorPublicFiles
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    public static void MapEmulatorPublicFiles(this WebApplication app)
    {
        app.MapGet("/azurite/{*path}", (HttpContext context, string? path, CancellationToken cancellationToken) =>
            ProxyAsync(context, Upstream("http://127.0.0.1:10000/devstoreaccount1/", path), cancellationToken));

        app.MapGet("/gcs/{bucket}/{*objectName}", (HttpContext context, string bucket, string? objectName, CancellationToken cancellationToken) =>
            ProxyAsync(context, GoogleDownload(bucket, objectName), cancellationToken));
    }

    /// <summary>
    /// The object name is one path segment, so slashes inside it are encoded. <c>alt=media</c>
    /// is what makes fake-gcs-server return the bytes.
    /// </summary>
    private static Uri? GoogleDownload(string bucket, string? objectName)
    {
        if (Unsafe(bucket) || string.IsNullOrEmpty(objectName))
        {
            return null;
        }

        return new Uri(
            "http://127.0.0.1:4443/download/storage/v1/b/" +
            Uri.EscapeDataString(bucket) +
            "/o/" +
            Uri.EscapeDataString(objectName) +
            "?alt=media");
    }

    private static Uri? Upstream(string emulator, string? path)
    {
        if (Unsafe(path))
        {
            return null;
        }

        string encoded = string.Join('/', path!.Split('/').Select(Uri.EscapeDataString));
        return new Uri(emulator + encoded);
    }

    private static bool Unsafe(string? path) =>
        string.IsNullOrEmpty(path) || path.Contains('\\') || path.Split('/').Contains("..");

    private static async Task ProxyAsync(HttpContext context, Uri? upstream, CancellationToken cancellationToken)
    {
        if (upstream is null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        try
        {
            using var response = await Http.GetAsync(upstream, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            context.Response.StatusCode = (int)response.StatusCode;
            if (response.Content.Headers.ContentType is { } type)
            {
                context.Response.ContentType = type.ToString();
            }

            if (response.Content.Headers.ContentLength is { } length)
            {
                context.Response.ContentLength = length;
            }

            await response.Content.CopyToAsync(context.Response.Body, cancellationToken);
        }
        catch (HttpRequestException)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }
}
