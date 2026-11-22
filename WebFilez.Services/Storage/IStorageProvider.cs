namespace WebFilez.Services.Storage;

/// <summary>
/// Where a root's files actually live. The file system provider is built in. Any other store is a
/// separate package that implements this interface and publishes its own <see cref="Key"/>.
/// Register it before or after <c>AddWebFilez</c>. The root's <see cref="FileRoot.Provider"/>
/// selects one by that key.
/// </summary>
public interface IStorageProvider
{
    /// <summary>Stable key matched to <see cref="FileRoot.Provider"/>. Defined by the provider's package.</summary>
    string Key { get; }

    /// <summary>How this provider compares names. Object names are case-sensitive; Windows disks and Azure Files are not.</summary>
    StringComparison NameComparison { get; }

    /// <summary>
    /// Whether undo and redo can reverse a change here. A disk rename is one operation. Object
    /// stores copy every object and then delete the originals, so a failed undo can leave the
    /// folder under both names.
    /// </summary>
    bool SupportsUndo { get; }

    /// <summary>
    /// Maps a normalized virtual path into the root, or returns false when a segment is illegal
    /// for this provider. Does not require the object to exist.
    /// </summary>
    bool TryMap(FileRoot root, string virtualPath, out StorageLocation location);

    /// <summary>Whether two locations name the same object. A case-only difference is the same location on a case-insensitive store.</summary>
    /// <param name="left">One location.</param>
    /// <param name="right">The other location.</param>
    /// <returns><see langword="true"/> when they are the same object.</returns>
    bool AreSame(StorageLocation left, StorageLocation right);

    /// <summary>Whether <paramref name="candidate"/> is <paramref name="container"/> or sits inside it.</summary>
    bool Contains(StorageLocation container, StorageLocation candidate);

    /// <summary>The file or folder at <paramref name="location"/>, or <see langword="null"/> when it is missing.</summary>
    /// <param name="location">A mapped location.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The entry, or <see langword="null"/>.</returns>
    ValueTask<StorageEntry?> GetAsync(StorageLocation location, CancellationToken cancellationToken = default);

    /// <summary>Immediate children. The caller filters hidden, excluded and linked names.</summary>
    IAsyncEnumerable<StorageEntry> ListAsync(StorageLocation directory, CancellationToken cancellationToken = default);

    /// <summary>Creates the directory. A store without real directories uses a hidden marker so the empty folder can be listed.</summary>
    /// <param name="location">Where the directory should be.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes once the directory exists.</returns>
    Task CreateDirectoryAsync(StorageLocation location, CancellationToken cancellationToken = default);

    /// <summary>Creates an empty file at <paramref name="location"/>.</summary>
    /// <param name="location">Where the file should be.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes once the file exists.</returns>
    Task CreateEmptyFileAsync(StorageLocation location, CancellationToken cancellationToken = default);

    /// <summary>Opens the file for reading. The caller disposes the stream.</summary>
    /// <param name="location">The file.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>A readable stream.</returns>
    Task<Stream> OpenReadAsync(StorageLocation location, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces a file with the contents of <paramref name="content"/>.</summary>
    /// <param name="location">The file to write.</param>
    /// <param name="content">The bytes. The caller keeps ownership of the stream.</param>
    /// <param name="overwrite">Whether an existing file may be replaced.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the file is stored.</returns>
    Task WriteAsync(StorageLocation location, Stream content, bool overwrite, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a local file into the store and deletes it. The file system renames it into place;
    /// a remote provider uploads it.
    /// </summary>
    /// <param name="location">Where the file should land.</param>
    /// <param name="localFile">The full path of the local file.</param>
    /// <param name="overwrite">Whether an existing file may be replaced.</param>
    /// <param name="cancellationToken">Cancels the commit.</param>
    /// <returns>A task that completes once the local file has been stored and removed.</returns>
    Task CommitLocalFileAsync(StorageLocation location, string localFile, bool overwrite, CancellationToken cancellationToken = default);

    /// <summary>Deletes one file.</summary>
    /// <param name="location">The file.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>A task that completes once the file is gone.</returns>
    Task DeleteFileAsync(StorageLocation location, CancellationToken cancellationToken = default);

    /// <summary>Deletes a directory and what it contains.</summary>
    /// <param name="location">The directory.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>A task that completes once the directory is gone.</returns>
    Task DeleteDirectoryAsync(StorageLocation location, CancellationToken cancellationToken = default);

    /// <summary>Moves a file or directory onto <paramref name="destination"/>.</summary>
    /// <param name="source">The item to move.</param>
    /// <param name="destination">Where it should land.</param>
    /// <param name="overwrite">Whether an existing destination may be replaced.</param>
    /// <param name="cancellationToken">Cancels the move.</param>
    /// <returns>A task that completes once the item is at the destination.</returns>
    Task MoveAsync(StorageLocation source, StorageLocation destination, bool overwrite, CancellationToken cancellationToken = default);

    /// <summary>Copies a file or directory onto <paramref name="destination"/>.</summary>
    /// <param name="source">The item to copy.</param>
    /// <param name="destination">Where the copy should land.</param>
    /// <param name="overwrite">Whether an existing destination may be replaced.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>A task that completes once the copy exists.</returns>
    Task CopyAsync(StorageLocation source, StorageLocation destination, bool overwrite, CancellationToken cancellationToken = default);
}