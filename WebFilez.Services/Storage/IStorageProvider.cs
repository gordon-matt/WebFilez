namespace WebFilez.Services.Storage;

/// <summary>
/// Where a root's files actually live. The file system provider is built in; a host adds others,
/// such as Azure Blob Storage, by registering another implementation. Register it before or after
/// <c>AddWebFilez</c>. The root's <see cref="FileRoot.Provider"/> selects one by <see cref="Key"/>.
/// </summary>
public interface IStorageProvider
{
    /// <summary>Stable key matched to <see cref="FileRoot.Provider"/>, for example <c>filesystem</c>.</summary>
    string Key { get; }

    /// <summary>How this provider compares names. Object names are case-sensitive; Windows disks and Azure Files are not.</summary>
    StringComparison NameComparison { get; }

    /// <summary>
    /// Maps a normalized virtual path into the root, or returns false when a segment is illegal
    /// for this provider. Does not require the object to exist.
    /// </summary>
    bool TryMap(FileRoot root, string virtualPath, out StorageLocation location);

    bool AreSame(StorageLocation left, StorageLocation right);

    /// <summary>Whether <paramref name="candidate"/> is <paramref name="container"/> or sits inside it.</summary>
    bool Contains(StorageLocation container, StorageLocation candidate);

    ValueTask<StorageEntry?> GetAsync(StorageLocation location, CancellationToken cancellationToken = default);

    /// <summary>Immediate children. The caller filters hidden, excluded and linked names.</summary>
    IAsyncEnumerable<StorageEntry> ListAsync(StorageLocation directory, CancellationToken cancellationToken = default);

    Task CreateDirectoryAsync(StorageLocation location, CancellationToken cancellationToken = default);

    Task CreateEmptyFileAsync(StorageLocation location, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(StorageLocation location, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces a file with the contents of <paramref name="content"/>.</summary>
    Task WriteAsync(StorageLocation location, Stream content, bool overwrite, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a local file into the store and deletes it. The file system renames it into place;
    /// a remote provider uploads it.
    /// </summary>
    Task CommitLocalFileAsync(StorageLocation location, string localFile, bool overwrite, CancellationToken cancellationToken = default);

    Task DeleteFileAsync(StorageLocation location, CancellationToken cancellationToken = default);

    Task DeleteDirectoryAsync(StorageLocation location, CancellationToken cancellationToken = default);

    Task MoveAsync(StorageLocation source, StorageLocation destination, bool overwrite, CancellationToken cancellationToken = default);

    Task CopyAsync(StorageLocation source, StorageLocation destination, bool overwrite, CancellationToken cancellationToken = default);
}