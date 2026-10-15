namespace WebFilez.Services.Storage;

/// <summary>Picks the <see cref="IStorageProvider"/> named by a root.</summary>
public interface IStorageProviderSelector
{
    IStorageProvider Get(FileRoot root);
}

public sealed class StorageProviderSelector(IEnumerable<IStorageProvider> providers) : IStorageProviderSelector
{
    public IStorageProvider Get(FileRoot root)
    {
        string key = StorageProviders.IsFileSystem(root.Provider) ? StorageProviders.FileSystem : root.Provider.Trim();

        var match = providers.FirstOrDefault(provider => provider.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        string registered = providers.Any()
            ? string.Join(", ", providers.Select(provider => provider.Key).Distinct(StringComparer.OrdinalIgnoreCase))
            : "none";

        throw new InvalidOperationException(
            $"No storage provider is registered for '{key}'. Registered providers: {registered}. " +
            "Add the provider's package and register it, for example services.AddWebFilezAzureBlob(), services.AddWebFilezAmazonS3(), or services.AddWebFilezGoogleCloud().");
    }
}