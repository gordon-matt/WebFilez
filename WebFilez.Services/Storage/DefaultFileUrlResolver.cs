using Microsoft.Extensions.Options;

namespace WebFilez.Services.Storage;

/// <summary>
/// The insertable URL for a file. A configured public base wins. Otherwise the matching
/// <see cref="IStoragePublicUrl"/> supplies the plain object URL. A file-system root keeps its
/// <see cref="FileRoot.PublicUrl"/>. Never a presigned, SAS, or other expiring URL.
/// </summary>
public sealed class DefaultFileUrlResolver(
    IOptionsMonitor<WebFilezOptions> options,
    ILogger<DefaultFileUrlResolver> logger,
    IEnumerable<IStoragePublicUrl> providers) : IFileUrlResolver
{
    /// <summary>Root setting that overrides the account host for this root. The bucket name is not added again.</summary>
    public const string PublicBaseUrlSetting = "PublicBaseUrl";

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
            // PublicBaseUrl replaces the account host, so the object key is appended. The file
            // system has no object key: the virtual path is the URL.
            string key = legacyRoot || FileSystemStorageProvider.IsFileSystem(context.ProviderKey)
                ? FileUrl.VirtualKey(context.VirtualPath)
                : FileUrl.ObjectKey(context);

            return FileUrl.Combine(baseUrl, key, context.IsDirectory);
        }

        foreach (var provider in providers)
        {
            if (provider.ProviderKey.Equals(context.ProviderKey, StringComparison.OrdinalIgnoreCase))
            {
                return provider.GetUrl(context);
            }
        }

        return null;
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
