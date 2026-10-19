using WebFilez;
using WebFilez.Services;
using WebFilez.Services.Storage;

namespace WebFilez.Storage.AmazonS3;

/// <summary>
/// Plain object URL. A region uses <c>https://{bucket}.s3.{region}.amazonaws.com/{key}</c>.
/// <c>ServiceUrl</c> is path-style, <c>{serviceUrl}/{bucket}/{key}</c>. Never a presigned URL.
/// </summary>
public sealed class AmazonS3PublicUrl : IStoragePublicUrl
{
    public string ProviderKey => AmazonS3StorageProvider.ProviderKey;

    public string? GetUrl(FileUrlContext context)
    {
        string? bucket = Setting(context.Root, AmazonS3Paths.BucketKey);
        if (bucket is null)
        {
            return null;
        }

        string? serviceUrl = Setting(context.Root, AmazonS3Paths.ServiceUrlKey);
        string origin = serviceUrl is not null
            ? $"{serviceUrl.TrimEnd(Constants.PathSeparator)}/{Uri.EscapeDataString(bucket)}"
            : $"https://{bucket}.s3.{Setting(context.Root, AmazonS3Paths.RegionKey) ?? "us-east-1"}.amazonaws.com";

        return FileUrl.Combine(origin, FileUrl.ObjectKey(context), context.IsDirectory);
    }

    private static string? Setting(FileRoot root, string key) =>
        root.Settings.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
