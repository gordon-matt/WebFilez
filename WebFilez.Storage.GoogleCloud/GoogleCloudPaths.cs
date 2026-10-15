using WebFilez.Services;

namespace WebFilez.Storage.GoogleCloud;

/// <summary>Turns a root and a virtual path into a Cloud Storage object name. No network calls.</summary>
internal static class GoogleCloudPaths
{
    public const string BucketKey = "Bucket";
    public const string CredentialJsonKey = "CredentialJson";
    public const string CredentialFileKey = "CredentialFile";
    public const string ServiceUrlKey = "ServiceUrl";
    public const string PrefixKey = "Prefix";

    /// <summary>
    /// Empty-folder marker. Object stores have no real directories, so an empty folder is a
    /// hidden child object rather than a name that ends in <c>/</c>.
    /// </summary>
    public const string DirectoryMarkerName = ".folder.webfilez.tmp";

    public static string Prefix(FileRoot root)
    {
        string raw = root.Settings.TryGetValue(PrefixKey, out string? configured) && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : root.Path;

        return raw.Replace('\\', '/').Trim('/');
    }

    public static string ObjectName(string prefix, string virtualPath)
    {
        string relative = string.Join('/', VirtualPath.GetSegments(virtualPath));
        return prefix.Length == 0 ? relative : relative.Length == 0 ? prefix : $"{prefix}/{relative}";
    }

    /// <summary>The prefix passed to a hierarchy listing, always ending in a slash except for the bucket root.</summary>
    public static string ListingPrefix(string objectName) =>
        objectName.Length == 0 ? string.Empty : objectName.TrimEnd('/') + "/";

    public static string DirectoryMarker(string objectName) => ListingPrefix(objectName) + DirectoryMarkerName;

    /// <summary>
    /// Accepts either the emulator origin (<c>http://127.0.0.1:4443</c>) or a full
    /// <c>/storage/v1/</c> base URI.
    /// </summary>
    public static string ApiBaseUri(string serviceUrl)
    {
        string trimmed = serviceUrl.Trim().TrimEnd('/');
        return trimmed.EndsWith("/storage/v1", StringComparison.OrdinalIgnoreCase)
            ? trimmed + "/"
            : trimmed + "/storage/v1/";
    }
}
