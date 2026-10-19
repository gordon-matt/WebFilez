using WebFilez;
using WebFilez.Services;
using WebFilez.Services.Storage;

namespace WebFilez.Storage.AzureFiles;

/// <summary>
/// Plain file URL, <c>https://{account}.file.core.windows.net/{share}/{key}</c>. Azurite does not
/// serve file shares, so <c>UseDevelopmentStorage=true</c> stays unresolved. Never a SAS URL.
/// </summary>
public sealed class AzureFilePublicUrl : IStoragePublicUrl
{
    public string ProviderKey => AzureFileStorageProvider.ProviderKey;

    public string? GetUrl(FileUrlContext context)
    {
        string? endpoint = Endpoint(context.Root);
        string? share = Setting(context.Root, AzureFilePaths.ShareKey);
        if (endpoint is null || share is null)
        {
            return null;
        }

        return FileUrl.Combine(
            $"{endpoint}/{Uri.EscapeDataString(share)}",
            FileUrl.ObjectKey(context),
            context.IsDirectory);
    }

    private static string? Endpoint(FileRoot root)
    {
        if (Setting(root, AzureFilePaths.ConnectionStringKey) is not { } connection)
        {
            return null;
        }

        var map = ParseConnectionString(connection);
        if (map.TryGetValue("FileEndpoint", out string? endpoint) && !string.IsNullOrWhiteSpace(endpoint))
        {
            return endpoint.Trim().TrimEnd(Constants.PathSeparator);
        }

        if (map.TryGetValue("UseDevelopmentStorage", out string? development)
            && development.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return null;
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

        return $"{scheme}://{account.Trim()}.file.{suffix}";
    }

    private static Dictionary<string, string> ParseConnectionString(string connection)
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

        return pairs;
    }

    private static string? Setting(FileRoot root, string key) =>
        root.Settings.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
