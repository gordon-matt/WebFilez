using System.Collections.Frozen;
using Microsoft.Extensions.Options;

namespace WebFilez.Services.Storage;

/// <summary>
/// The insertable URL for a file. A configured public base wins. Otherwise a cloud provider's
/// plain object URL is used, and a file-system root keeps its <see cref="FileRoot.PublicUrl"/>.
/// Never a presigned, SAS, or other expiring URL.
/// </summary>
public sealed class DefaultFileUrlResolver(
    IOptionsMonitor<WebFilezOptions> options,
    ILogger<DefaultFileUrlResolver> logger) : IFileUrlResolver
{
    /// <summary>Root setting that overrides the account host for this root. The bucket name is not added again.</summary>
    public const string PublicBaseUrlSetting = "PublicBaseUrl";

    private const string AzureBlob = "azure-blob";
    private const string AzureFile = "azure-file";
    private const string AmazonS3 = "amazon-s3";
    private const string GoogleCloud = "google-cloud";

    private int expiringOptInWarned;

    public ValueTask<string?> ResolveAsync(FileUrlContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WarnOnceIfExpiringOptInIsIgnored(context);
        return ValueTask.FromResult(Resolve(context));
    }

    private string? Resolve(FileUrlContext context)
    {
        var (baseUrl, legacyRoot) = ConfiguredBase(context);
        if (baseUrl is not null)
        {
            // PublicUrl is the address of the root itself, so only the virtual path is appended.
            // PublicBaseUrl replaces the account host, so the object key (prefix included, bucket
            // excluded) is appended. The file system has no object key: the virtual path is the URL.
            string key = legacyRoot || FileSystemStorageProvider.IsFileSystem(context.ProviderKey)
                ? VirtualKey(context.VirtualPath)
                : ObjectKey(context);

            return Combine(baseUrl, key, context.IsDirectory);
        }

        return RawProviderUrl(context);
    }

    /// <summary>
    /// <see cref="FileRoot.PublicUrl"/> stays the legacy "this URL is the root" address.
    /// <see cref="FileRoot.PublicBaseUrl"/>, the <c>PublicBaseUrl</c> setting, and
    /// <see cref="WebFilezOptions.PublicBaseUrl"/> are CDN bases and use the object key.
    /// </summary>
    private (string? Url, bool LegacyRoot) ConfiguredBase(FileUrlContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Root.PublicBaseUrl))
        {
            return (context.Root.PublicBaseUrl, false);
        }

        if (Setting(context.Root, PublicBaseUrlSetting) is { } fromSettings)
        {
            return (fromSettings, false);
        }

        if (!string.IsNullOrWhiteSpace(context.Root.PublicUrl))
        {
            return (context.Root.PublicUrl, true);
        }

        string? global = options.CurrentValue.PublicBaseUrl;
        return string.IsNullOrWhiteSpace(global) ? (null, false) : (global, false);
    }

    private static string? RawProviderUrl(FileUrlContext context)
    {
        if (FileSystemStorageProvider.IsFileSystem(context.ProviderKey))
        {
            return null;
        }

        if (context.ProviderKey.Equals(AzureBlob, StringComparison.OrdinalIgnoreCase))
        {
            return Azure(context, files: false);
        }

        if (context.ProviderKey.Equals(AzureFile, StringComparison.OrdinalIgnoreCase))
        {
            return Azure(context, files: true);
        }

        if (context.ProviderKey.Equals(AmazonS3, StringComparison.OrdinalIgnoreCase))
        {
            return Amazon(context);
        }

        if (context.ProviderKey.Equals(GoogleCloud, StringComparison.OrdinalIgnoreCase))
        {
            return Google(context);
        }

        return null;
    }

    private static string? Azure(FileUrlContext context, bool files)
    {
        string? container = Setting(context.Root, files ? "Share" : "Container");
        string? endpoint = AzureEndpoint(context.Root, files);
        if (container is null || endpoint is null)
        {
            return null;
        }

        return Combine($"{endpoint}/{Uri.EscapeDataString(container)}", ObjectKey(context), context.IsDirectory);
    }

    private static string? Amazon(FileUrlContext context)
    {
        string? bucket = Setting(context.Root, "Bucket");
        if (bucket is null)
        {
            return null;
        }

        string? serviceUrl = Setting(context.Root, "ServiceUrl");
        string origin = serviceUrl is not null
            ? $"{serviceUrl.TrimEnd(Constants.PathSeparator)}/{Uri.EscapeDataString(bucket)}"
            : $"https://{bucket}.s3.{Setting(context.Root, "Region") ?? "us-east-1"}.amazonaws.com";

        return Combine(origin, ObjectKey(context), context.IsDirectory);
    }

    private static string? Google(FileUrlContext context)
    {
        string? bucket = Setting(context.Root, "Bucket");
        if (bucket is null)
        {
            return null;
        }

        string? serviceUrl = Setting(context.Root, "ServiceUrl");
        string origin = serviceUrl is null
            ? "https://storage.googleapis.com"
            : serviceUrl.TrimEnd(Constants.PathSeparator);

        return Combine($"{origin}/{Uri.EscapeDataString(bucket)}", ObjectKey(context), context.IsDirectory);
    }

    /// <summary>
    /// Blob or file endpoint from the connection string. <c>UseDevelopmentStorage=true</c> is the
    /// Azurite blob endpoint. Azurite does not serve file shares, so those stay unresolved.
    /// </summary>
    private static string? AzureEndpoint(FileRoot root, bool files)
    {
        if (Setting(root, "ConnectionString") is not { } connection)
        {
            return null;
        }

        var map = ParseConnectionString(connection);
        string endpointKey = files ? "FileEndpoint" : "BlobEndpoint";
        if (map.TryGetValue(endpointKey, out string? endpoint) && !string.IsNullOrWhiteSpace(endpoint))
        {
            return endpoint.Trim().TrimEnd(Constants.PathSeparator);
        }

        if (map.TryGetValue("UseDevelopmentStorage", out string? development)
            && development.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return files ? null : "http://127.0.0.1:10000/devstoreaccount1";
        }

        if (!map.TryGetValue("AccountName", out string? account) || string.IsNullOrWhiteSpace(account))
        {
            return null;
        }

        string scheme = map.TryGetValue("DefaultEndpointsProtocol", out string? protocol)
            && protocol.Equals("http", StringComparison.OrdinalIgnoreCase)
            ? "http"
            : "https";
        string suffix = map.TryGetValue("EndpointSuffix", out string? endpointSuffix) && !string.IsNullOrWhiteSpace(endpointSuffix)
            ? endpointSuffix.Trim()
            : "core.windows.net";
        string service = files ? "file" : "blob";

        return $"{scheme}://{account.Trim()}.{service}.{suffix}";
    }

    private static FrozenDictionary<string, string> ParseConnectionString(string connection)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in connection.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int split = part.IndexOf('=');
            if (split <= 0)
            {
                continue;
            }

            pairs[part[..split].Trim()] = part[(split + 1)..].Trim();
        }

        return pairs.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Prefix plus virtual path. The bucket, container, or share name is not part of the key.</summary>
    private static string ObjectKey(FileUrlContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.StorageKey))
        {
            return context.StorageKey.Replace('\\', Constants.PathSeparator).Trim(Constants.PathSeparator);
        }

        string raw = Setting(context.Root, "Prefix") ?? context.Root.Path;
        string prefix = raw.Replace('\\', Constants.PathSeparator).Trim(Constants.PathSeparator);
        string relative = VirtualKey(context.VirtualPath);

        if (prefix.Length == 0)
        {
            return relative;
        }

        return relative.Length == 0 ? prefix : $"{prefix}/{relative}";
    }

    private static string VirtualKey(string virtualPath) =>
        string.Join(Constants.PathSeparator, VirtualPath.GetSegments(VirtualPath.Normalize(virtualPath)));

    /// <summary>Joins a base and a key with one slash, encoding each segment and never a second slash.</summary>
    private static string Combine(string baseUrl, string key, bool directory)
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

    private void WarnOnceIfExpiringOptInIsIgnored(FileUrlContext context)
    {
        if (!context.AllowExpiringUrls || Interlocked.Exchange(ref expiringOptInWarned, 1) != 0)
        {
            return;
        }

        logger.LogWarning(
            "AllowExpiringUrls is set, but the built-in URL resolver returns a stable public URL. " +
            "Register a custom IFileUrlResolver to return signed URLs. Signed URLs expire and break content that stored them.");
    }
}
