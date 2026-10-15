using WebFilez.Services;

namespace WebFilez.Storage.AzureBlob;

/// <summary>Turns a root and a virtual path into a blob name. No network calls.</summary>
internal static class AzureBlobPaths
{
    public const string ConnectionStringKey = "ConnectionString";
    public const string ContainerKey = "Container";
    public const string PrefixKey = "Prefix";

    public static string Prefix(FileRoot root)
    {
        string raw = root.Settings.TryGetValue(PrefixKey, out string? configured) && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : root.Path;

        return raw.Replace('\\', '/').Trim('/');
    }

    public static string BlobName(string prefix, string virtualPath)
    {
        string relative = string.Join('/', VirtualPath.GetSegments(virtualPath));
        return prefix.Length == 0 ? relative : relative.Length == 0 ? prefix : $"{prefix}/{relative}";
    }

    /// <summary>The prefix passed to a hierarchy listing, always ending in a slash except for the container root.</summary>
    public static string ListingPrefix(string blobName) =>
        blobName.Length == 0 ? string.Empty : blobName.TrimEnd('/') + "/";

    /// <summary>
    /// Empty-folder marker. The blob client drops a trailing slash from a blob name, so an empty
    /// folder is a hidden child blob rather than a blob whose name ends in <c>/</c>.
    /// </summary>
    public const string DirectoryMarkerName = ".folder.webfilez.tmp";

    public static string DirectoryMarker(string blobName) => ListingPrefix(blobName) + DirectoryMarkerName;
}