namespace WebFilez.Components.Infrastructure;

/// <summary>Absolute URLs for the requests the browser makes directly to the API.</summary>
public sealed class WebFilezUrls(NavigationManager navigation, WebFilezApiAddress address)
{
    private readonly Uri pageUri = new(navigation.BaseUri);
    private readonly Uri baseUri = new(new Uri(navigation.BaseUri), address.BaseUrl);

    public bool IncludeCredentials => address.IncludeCredentials;

    public string Upload => Combine(ApiRoutes.Upload);

    public string Download(string rootId, string path, bool inline = false) =>
        Combine($"{ApiRoutes.Download}?root={Escape(rootId)}&path={Escape(path)}{(inline ? "&inline=true" : string.Empty)}");

    /// <summary>
    /// The file's modification ticks make each version of it a distinct URL, which is what lets
    /// the endpoint send a long cache lifetime.
    /// </summary>
    public string Thumbnail(string rootId, FileSystemItemDto item, int size) =>
        Combine($"{ApiRoutes.Thumbnail}?root={Escape(rootId)}&path={Escape(item.Path)}&size={size}&v={item.ModifiedAt.Ticks}");

    public string Zip(string token) => Combine($"{ApiRoutes.Zip}/{Escape(token)}");

    /// <summary>
    /// A root's public URL as the page should use it. A relative one, such as
    /// <c>/media/logo.png</c>, belongs to the site serving the API, so it is made absolute when
    /// that's another origin and left relative, which is what a CMS usually stores, when not.
    /// </summary>
    public string? Public(string? url) => url is null || IsSameOrigin ? url : new Uri(baseUri, url).ToString();

    private bool IsSameOrigin => Uri.Compare(
        baseUri, pageUri, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;

    private string Combine(string relative) => new Uri(baseUri, relative).ToString();

    private static string Escape(string value) => Uri.EscapeDataString(value);
}