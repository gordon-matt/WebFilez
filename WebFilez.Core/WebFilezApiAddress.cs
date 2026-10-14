namespace WebFilez;

/// <summary>
/// Where the browser reaches the WebFilez HTTP API, for the requests that always go over HTTP:
/// downloads, uploads and thumbnails.
/// </summary>
/// <param name="BaseUrl">
/// Absolute, or relative to the page's base URI, with a trailing slash, such as
/// <c>api/webfilez/</c> or <c>https://files.example.com/api/webfilez/</c>.
/// </param>
/// <param name="IncludeCredentials">
/// Send cookies with cross-origin requests. Needed when the API is on another origin and uses
/// cookie authentication; that origin's CORS policy must then allow credentials.
/// </param>
public sealed record WebFilezApiAddress(string BaseUrl, bool IncludeCredentials = false);
