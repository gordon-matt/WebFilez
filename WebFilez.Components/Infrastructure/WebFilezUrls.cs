namespace WebFilez.Components.Infrastructure;

/// <summary>
/// Absolute URLs for the requests the browser makes directly to the API. Downloads, previews and
/// thumbnails use these permission-checked addresses. The URL stored in content is
/// <see cref="FileSystemItemDto.PublicUrl"/>, produced on the server, and is not built here.
/// </summary>
public sealed class WebFilezUrls(NavigationManager navigation, WebFilezApiAddress address)
{
    private readonly Uri pageUri = new(navigation.BaseUri);
    private readonly Uri baseUri = new(new Uri(navigation.BaseUri), address.BaseUrl);

    /// <summary>Whether browser downloads, uploads, and thumbnails send cookies.</summary>
    public bool IncludeCredentials => address.IncludeCredentials;

    /// <summary>When set, browser calls to the API stay inside the folder this token was issued for.</summary>
    public string? ScopeToken { get; set; }

    /// <summary>The upload endpoint, including the scope token when this instance is confined.</summary>
    public string Upload => WithScope(Combine(ApiRoutes.Upload));

    /// <summary>The download address for one file.</summary>
    /// <param name="rootId">The root.</param>
    /// <param name="path">The virtual path.</param>
    /// <param name="inline">Whether the browser should display the file instead of saving it.</param>
    /// <returns>An absolute URL.</returns>
    public string Download(string rootId, string path, bool inline = false) =>
        WithScope(Combine($"{ApiRoutes.Download}?root={Escape(rootId)}&path={Escape(path)}{(inline ? "&inline=true" : string.Empty)}"));

    /// <summary>
    /// The file's modification ticks make each version of it a distinct URL, which is what lets
    /// the endpoint send a long cache lifetime.
    /// </summary>
    public string Thumbnail(string rootId, FileSystemItemDto item, int size) =>
        WithScope(Combine($"{ApiRoutes.Thumbnail}?root={Escape(rootId)}&path={Escape(item.Path)}&size={size}&v={item.ModifiedAt.Ticks}"));

    /// <summary>The one-time zip address for a ticket.</summary>
    /// <param name="token">The ticket.</param>
    /// <returns>An absolute URL.</returns>
    public string Zip(string token) => WithScope(Combine($"{ApiRoutes.Zip}/{Escape(token)}"));

    /// <summary>
    /// A root's public URL as the page should use it. A relative one, such as
    /// <c>/media/logo.png</c>, belongs to the site serving the API, so it is made absolute when
    /// that's another origin and left relative, which is what a CMS usually stores, when not.
    /// </summary>
    public string? Public(string? url) => url is null || IsSameOrigin ? url : new Uri(baseUri, url).ToString();

    private bool IsSameOrigin => Uri.Compare(
        baseUri, pageUri, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;

    private string Combine(string relative) => new Uri(baseUri, relative).ToString();

    private string WithScope(string url) =>
        string.IsNullOrEmpty(ScopeToken)
            ? url
            : url + (url.Contains('?') ? "&" : "?") + Constants.ScopeQueryName + "=" + Escape(ScopeToken);

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
