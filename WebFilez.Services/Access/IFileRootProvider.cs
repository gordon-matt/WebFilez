using Microsoft.Extensions.Options;

namespace WebFilez.Services.Access;

/// <summary>
/// Supplies the roots the file manager can see. The default reads
/// <see cref="WebFilezOptions.Roots"/>. Replace it to load roots from a database or to give each
/// user their own home folder.
/// </summary>
public interface IFileRootProvider
{
    /// <summary>Roots available to the current caller.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The roots. An empty list is valid.</returns>
    Task<IReadOnlyList<FileRoot>> GetRootsAsync(CancellationToken cancellationToken = default);

    /// <summary>One root, compared without case, or <see langword="null"/> when the caller has no such root.</summary>
    /// <param name="rootId">The root id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The root, or <see langword="null"/>.</returns>
    async Task<FileRoot?> FindAsync(string rootId, CancellationToken cancellationToken = default)
    {
        var roots = await GetRootsAsync(cancellationToken);
        return roots.FirstOrDefault(r => string.Equals(r.Id, rootId, StringComparison.OrdinalIgnoreCase));
    }
}

/// <inheritdoc cref="IFileRootProvider"/>
public sealed class OptionsFileRootProvider(IOptionsMonitor<WebFilezOptions> options) : IFileRootProvider
{
    /// <inheritdoc />
    public Task<IReadOnlyList<FileRoot>> GetRootsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FileRoot>>(options.CurrentValue.Roots);
}