namespace WebFilez.Services.Storage;

/// <summary>Picks the <see cref="IStorageProvider"/> named by a root.</summary>
public interface IStorageProviderSelector
{
    /// <summary>
    /// Gets the value.
    /// </summary>
    /// <param name="root">The root.</param>
    /// <returns>The i storage provider.</returns>
    IStorageProvider Get(FileRoot root);
}

/// <summary>
/// Represents the storage provider selector.
/// </summary>
public sealed class StorageProviderSelector(IEnumerable<IStorageProvider> providers) : IStorageProviderSelector
{
    /// <summary>
    /// Gets the value.
    /// </summary>
    /// <param name="root">The root.</param>
    /// <returns>The i storage provider.</returns>
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