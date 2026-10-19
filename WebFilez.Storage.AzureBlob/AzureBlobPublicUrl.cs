using WebFilez;
using WebFilez.Services;
using WebFilez.Services.Storage;

namespace WebFilez.Storage.AzureBlob;

/// <summary>
/// Plain blob URL, <c>https://{account}.blob.core.windows.net/{container}/{key}</c>.
/// <c>UseDevelopmentStorage=true</c> uses the Azurite host. Never a SAS URL.
/// </summary>
public sealed class AzureBlobPublicUrl : IStoragePublicUrl
{
    public string ProviderKey => AzureBlobStorageProvider.ProviderKey;

    public string? GetUrl(FileUrlContext context)
    {
        string? endpoint = Endpoint(context.Root);
        string? container = Setting(context.Root, AzureBlobPaths.ContainerKey);
        if (endpoint is null || container is null)
        {
            return null;
        }

        return FileUrl.Combine(
            $"{endpoint}/{Uri.EscapeDataString(container)}",
            FileUrl.ObjectKey(context),
            context.IsDirectory);
    }

    /// <summary>
    /// Blob endpoint from the connection string. <c>UseDevelopmentStorage=true</c> is the Azurite
    /// blob endpoint.
    /// </summary>
    private static string? Endpoint(FileRoot root)
    {
        if (Setting(root, AzureBlobPaths.ConnectionStringKey) is not { } connection)
        {
            return null;
        }

        var map = ParseConnectionString(connection);
        if (map.TryGetValue("BlobEndpoint", out string? endpoint) && !string.IsNullOrWhiteSpace(endpoint))
        {
            return endpoint.Trim().TrimEnd(Constants.PathSeparator);
        }

        if (map.TryGetValue("UseDevelopmentStorage", out string? development)
            && development.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return "http://127.0.0.1:10000/devstoreaccount1";
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

        return $"{scheme}://{account.Trim()}.blob.{suffix}";
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
