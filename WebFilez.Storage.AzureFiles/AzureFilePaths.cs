using WebFilez.Services;

namespace WebFilez.Storage.AzureFiles;

/// <summary>Turns a root and a virtual path into a path inside an Azure file share. No network calls.</summary>
internal static class AzureFilePaths
{
    public const string ConnectionStringKey = "ConnectionString";
    public const string ShareKey = "Share";
    public const string PrefixKey = "Prefix";

    public static string Prefix(FileRoot root)
    {
        string raw = root.Settings.TryGetValue(PrefixKey, out string? configured) && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : root.Path;

        return raw.Replace('\\', '/').Trim('/');
    }

    public static string FilePath(string prefix, string virtualPath)
    {
        string relative = string.Join('/', VirtualPath.GetSegments(virtualPath));
        return prefix.Length == 0 ? relative : relative.Length == 0 ? prefix : $"{prefix}/{relative}";
    }

    public static string Parent(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    public static string Name(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }
}