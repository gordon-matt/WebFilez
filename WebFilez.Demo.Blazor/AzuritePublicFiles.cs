namespace WebFilez.Demo;

/// <summary>
/// Serves Azurite blobs on the demo's own origin. Azurite is HTTP-only, and an HTTPS page
/// will not load those image URLs. The azure-blob root's PublicBaseUrl points here.
/// </summary>
public static class AzuritePublicFiles
{
    private const string Emulator = "http://127.0.0.1:10000/devstoreaccount1/";

    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri(Emulator),
        Timeout = TimeSpan.FromSeconds(30),
    };

    public static void MapAzuritePublicFiles(this WebApplication app)
    {
        app.MapGet("/azurite/{*path}", ProxyAsync);
    }

    private static async Task ProxyAsync(HttpContext context, string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\') || path.Split('/').Contains(".."))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        string encoded = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        try
        {
            using var response = await Http.GetAsync(encoded, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
