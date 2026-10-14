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
    Task<IReadOnlyList<FileRoot>> GetRootsAsync(CancellationToken cancellationToken = default);

    async Task<FileRoot?> FindAsync(string rootId, CancellationToken cancellationToken = default)
    {
        var roots = await GetRootsAsync(cancellationToken);
        return roots.FirstOrDefault(r => string.Equals(r.Id, rootId, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class OptionsFileRootProvider(IOptionsMonitor<WebFilezOptions> options) : IFileRootProvider
{
    public Task<IReadOnlyList<FileRoot>> GetRootsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FileRoot>>(options.CurrentValue.Roots);
}
