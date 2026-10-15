namespace WebFilez.Services.Storage;

/// <summary>A virtual path mapped into one root by its storage provider.</summary>
/// <param name="NativePath">Provider address: a full disk path, a blob name, an object key, or a path inside a file share.</param>
/// <param name="RootNativePath">The root's own native address. Used to keep operations inside it.</param>
public sealed record StorageLocation(FileRoot Root, string VirtualPath, string NativePath, string RootNativePath);

/// <summary>One file or folder as a provider reports it. The service turns this into a DTO.</summary>
public sealed record StorageEntry(
    string Name,
    bool IsDirectory,
    long SizeBytes,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    bool IsHidden,
    bool IsSymbolicLink);

/// <summary>A storage operation failed. Thrown as an <see cref="IOException"/> so callers can treat every provider alike.</summary>
public sealed class StorageException : IOException
{
    public StorageException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}