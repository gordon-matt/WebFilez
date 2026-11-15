using WebFilez.Services.Storage;

namespace WebFilez.Services.FileSystem;

/// <summary>Finds a sibling name that is not already taken, using the <c>name (2)</c> pattern.</summary>
internal static class FreeNames
{
    public static async Task<string> FindAsync(
        IStorageProvider storage,
        FileRoot root,
        string directoryPath,
        string name,
        bool isDirectory,
        CancellationToken cancellationToken)
    {
        if (await IsFreeAsync(storage, root, directoryPath, name, cancellationToken))
        {
            return name;
        }

        string stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        string extension = isDirectory ? string.Empty : Path.GetExtension(name);

        for (int i = 2; i < 10_000; i++)
        {
            string candidate = $"{stem} ({i}){extension}";
            if (await IsFreeAsync(storage, root, directoryPath, candidate, cancellationToken))
            {
                return candidate;
            }
        }

        return $"{stem} ({Guid.NewGuid():N}){extension}";
    }

    private static async Task<bool> IsFreeAsync(
        IStorageProvider storage,
        FileRoot root,
        string directoryPath,
        string name,
        CancellationToken cancellationToken)
    {
        if (!storage.TryMap(root, VirtualPath.Combine(directoryPath, name), out var location))
        {
            return false;
        }

        return await storage.GetAsync(location, cancellationToken) is null;
    }
}
