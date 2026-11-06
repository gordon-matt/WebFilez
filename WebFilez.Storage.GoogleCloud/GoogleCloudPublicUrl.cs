using WebFilez;
using WebFilez.Services;
using WebFilez.Services.Storage;

namespace WebFilez.Storage.GoogleCloud;

/// <summary>
/// Plain object URL, <c>https://storage.googleapis.com/{bucket}/{key}</c>. <c>ServiceUrl</c>
/// replaces the host, which is how fake-gcs-server is addressed. Never a signed URL.
/// </summary>
public sealed class GoogleCloudPublicUrl : IStoragePublicUrl
{
    /// <summary>
    /// Gets the provider key.
    /// </summary>
    public string ProviderKey => GoogleCloudStorageProvider.ProviderKey;

    /// <summary>
    /// Gets the URL.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>The URL.</returns>
    public string? GetUrl(FileUrlContext context)
    {
        string? bucket = Setting(context.Root, GoogleCloudPaths.BucketKey);
        if (bucket is null)
        {
            return null;
        }

        string? serviceUrl = Setting(context.Root, GoogleCloudPaths.ServiceUrlKey);
        string origin = serviceUrl is null
            ? "https://storage.googleapis.com"
            : serviceUrl.TrimEnd(Constants.PathSeparator);

        return FileUrl.Combine(
            $"{origin}/{Uri.EscapeDataString(bucket)}",
            FileUrl.ObjectKey(context),
            context.IsDirectory);
    }

    private static string? Setting(FileRoot root, string key) =>
        root.Settings.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
