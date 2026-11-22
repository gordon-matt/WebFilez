namespace WebFilez.Services.Storage;

/// <summary>Picks the <see cref="IStorageProvider"/> named by a root.</summary>
public interface IStorageProviderSelector
{
    /// <summary>The provider for <paramref name="root"/>. An empty provider is the file system.</summary>
    /// <param name="root">The root.</param>
    /// <returns>The matching provider.</returns>
    /// <exception cref="InvalidOperationException">No registered provider has that key.</exception>
    IStorageProvider Get(FileRoot root);
}

/// <summary>Selects a registered provider by the root's provider key, ignoring case.</summary>
public sealed class StorageProviderSelector(IEnumerable<IStorageProvider> providers) : IStorageProviderSelector
{
    /// <inheritdoc />
    public IStorageProvider Get(FileRoot root)
    {
        string key = FileSystemStorageProvider.IsFileSystem(root.Provider)
            ? FileSystemStorageProvider.ProviderKey
            : root.Provider.Trim();

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
            "Add the provider's package and register it.");
    }
}
