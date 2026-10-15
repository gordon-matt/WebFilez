using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace WebFilez.CustomElements;

/// <summary>
/// Sends the browser's cookies with JSON calls to an API on another origin. The API's CORS
/// policy must then allow credentials for this page's origin.
/// </summary>
internal sealed class IncludeCredentialsHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}