namespace WebFilez.Services.Storage;

/// <summary>
/// Joins an insertable URL the same way for a configured base and for a provider's plain object
/// URL. Each path segment is encoded and slashes are not doubled. A directory ends with a slash.
/// </summary>
public static class FileUrl
{
    /// <summary>Root setting that names the object-key prefix. It overrides <see cref="FileRoot.Path"/>.</summary>
    public const string PrefixSetting = "Prefix";

    /// <summary>Prefix plus virtual path. A bucket, container, or share name is not part of the key.</summary>
    public static string ObjectKey(FileUrlContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.StorageKey))
        {
            return context.StorageKey.Replace('\\', Constants.PathSeparator).Trim(Constants.PathSeparator);
        }

        string raw = Setting(context.Root, PrefixSetting) ?? context.Root.Path;
        string prefix = raw.Replace('\\', Constants.PathSeparator).Trim(Constants.PathSeparator);
        string relative = VirtualKey(context.VirtualPath);

        if (prefix.Length == 0)
        {
            return relative;
        }

        return relative.Length == 0 ? prefix : $"{prefix}/{relative}";
    }

    /// <summary>The virtual path with no leading slash and an empty root.</summary>
    public static string VirtualKey(string virtualPath) =>
        string.Join(Constants.PathSeparator, VirtualPath.GetSegments(VirtualPath.Normalize(virtualPath)));

    /// <summary>Joins a base and a key with one slash, encoding each segment.</summary>
    public static string Combine(string baseUrl, string key, bool directory)
    {
        string root = baseUrl.Trim().TrimEnd(Constants.PathSeparator);
        string encoded = Encode(key);
        string url = encoded.Length == 0 ? root : $"{root}/{encoded}";

        if (directory && !url.EndsWith(Constants.PathSeparator))
        {
            url += Constants.PathSeparator;
        }

        return url;
    }

    private static string Encode(string key) =>
        string.Join(
            Constants.PathSeparator,
            key.Replace('\\', Constants.PathSeparator)
                .Split(Constants.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));

    private static string? Setting(FileRoot root, string key) =>
        root.Settings.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
