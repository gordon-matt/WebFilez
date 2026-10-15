using WebFilez.Services;

namespace WebFilez.Storage.AmazonS3;

/// <summary>Turns a root and a virtual path into an S3 object key. No network calls.</summary>
internal static class AmazonS3Paths
{
    public const string AccessKeyKey = "AccessKey";
    public const string SecretKeyKey = "SecretKey";
    public const string BucketKey = "Bucket";
    public const string RegionKey = "Region";
    public const string ServiceUrlKey = "ServiceUrl";
    public const string PrefixKey = "Prefix";

    /// <summary>
    /// Empty-folder marker. Object stores have no real directories, so an empty folder is a
    /// hidden child object rather than a key that ends in <c>/</c>.
    /// </summary>
    public const string DirectoryMarkerName = ".folder.webfilez.tmp";

    public static string Prefix(FileRoot root)
    {
        string raw = root.Settings.TryGetValue(PrefixKey, out string? configured) && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : root.Path;

        return raw.Replace('\\', '/').Trim('/');
    }

    public static string ObjectKey(string prefix, string virtualPath)
    {
        string relative = string.Join('/', VirtualPath.GetSegments(virtualPath));
        return prefix.Length == 0 ? relative : relative.Length == 0 ? prefix : $"{prefix}/{relative}";
    }

    /// <summary>The prefix passed to a hierarchy listing, always ending in a slash except for the bucket root.</summary>
    public static string ListingPrefix(string objectKey) =>
        objectKey.Length == 0 ? string.Empty : objectKey.TrimEnd('/') + "/";

    public static string DirectoryMarker(string objectKey) => ListingPrefix(objectKey) + DirectoryMarkerName;
}