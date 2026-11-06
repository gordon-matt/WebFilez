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
    /// Maps a normalized virtual path into the root, or returns false when a segment is illegal
    /// for this provider. Does not require the object to exist.
    /// </summary>
    bool TryMap(FileRoot root, string virtualPath, out StorageLocation location);

    /// <summary>
    /// Determines whether this instance is same.
    /// </summary>
    /// <param name="left">The left.</param>
    /// <param name="right">The right.</param>
    /// <returns>true when the condition is met; otherwise, false.</returns>
    bool AreSame(StorageLocation left, StorageLocation right);

    /// <summary>Whether <paramref name="candidate"/> is <paramref name="container"/> or sits inside it.</summary>
    bool Contains(StorageLocation container, StorageLocation candidate);

    /// <summary>
    /// Gets the value.
    /// </summary>
    /// <param name="location">The location.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the storage entry.</returns>
    ValueTask<StorageEntry?> GetAsync(StorageLocation location, CancellationToken cancellationToken = default);

    /// <summary>Immediate children. The caller filters hidden, excluded and linked names.</summary>
    IAsyncEnumerable<StorageEntry> ListAsync(StorageLocation directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the directory.
    /// </summary>
    /// <param name="location">The location.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task CreateDirectoryAsync(StorageLocation location, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the empty file.
    /// </summary>
    /// <param name="location">The location.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task CreateEmptyFileAsync(StorageLocation location, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the read.
    /// </summary>
    /// <param name="location">The location.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the stream.</returns>
    Task<Stream> OpenReadAsync(StorageLocation location, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces a file with the contents of <paramref name="content"/>.</summary>
    Task WriteAsync(StorageLocation location, Stream content, bool overwrite, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a local file into the store and deletes it. The file system renames it into place;
    /// a remote provider uploads it.
    /// </summary>
    Task CommitLocalFileAsync(StorageLocation location, string localFile, bool overwrite, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the file.
    /// </summary>
    /// <param name="location">The location.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task DeleteFileAsync(StorageLocation location, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the directory.
    /// </summary>
    /// <param name="location">The location.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task DeleteDirectoryAsync(StorageLocation location, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the value.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="destination">The destination.</param>
    /// <param name="overwrite">The overwrite.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task MoveAsync(StorageLocation source, StorageLocation destination, bool overwrite, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies the value.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="destination">The destination.</param>
    /// <param name="overwrite">The overwrite.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task CopyAsync(StorageLocation source, StorageLocation destination, bool overwrite, CancellationToken cancellationToken = default);
}