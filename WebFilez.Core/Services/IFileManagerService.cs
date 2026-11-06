namespace WebFilez.Services;

/// <summary>
/// Everything the file manager UI does that fits in a JSON request. Implemented on the server by
/// <c>FileManagerService</c>, and in the browser by an HTTP client that calls the endpoints
/// <c>MapWebFilezApi()</c> exposes, so the components run unchanged in either place.
/// </summary>
/// <remarks>
/// Byte transfers (download, upload, thumbnails) are not here. They go straight from the browser
/// to the HTTP endpoints in every hosting model, because pushing file bytes through a SignalR
/// circuit or a JSON body would be slow and memory-hungry.
/// </remarks>
public interface IFileManagerService
{
    /// <summary>The roots the current caller can browse.</summary>
    Task<Result<IReadOnlyList<FileRootDto>>> GetRootsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the value.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is the outcome for the directory listing.</returns>
    Task<Result<DirectoryListingDto>> ListAsync(ListingRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches an index of names, extensions and kinds. The index is built on first use and then
    /// kept current from the same activity stream as <c>IFileActivitySink</c>. Replace
    /// <c>IFileIndex</c> to store that index somewhere else.
    /// </summary>
    Task<Result<SearchResultDto>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>The immediate subfolders of a folder, for the navigation tree.</summary>
    Task<Result<IReadOnlyList<FolderNodeDto>>> GetFoldersAsync(
        string rootId,
        string? path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the detail.
    /// </summary>
    /// <param name="rootId">The root identifier.</param>
    /// <param name="path">The path.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is the outcome for the file detail.</returns>
    Task<Result<FileDetailDto>> GetDetailAsync(string rootId, string path, CancellationToken cancellationToken = default);

    /// <summary>Walks a folder recursively, so it is only requested on demand.</summary>
    Task<Result<DirectorySizeDto>> GetDirectorySizeAsync(
        string rootId,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the directory.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is the outcome for the file system item.</returns>
    Task<Result<FileSystemItemDto>> CreateDirectoryAsync(
        CreateDirectoryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the file.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is the outcome for the file system item.</returns>
    Task<Result<FileSystemItemDto>> CreateFileAsync(CreateFileRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames the value.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is the outcome for the file system item.</returns>
    Task<Result<FileSystemItemDto>> RenameAsync(RenameRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the value.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is the outcome for the bulk operation result.</returns>
    Task<Result<BulkOperationResultDto>> DeleteAsync(DeleteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the transfer.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is the outcome for the bulk operation result.</returns>
    Task<Result<BulkOperationResultDto>> TransferAsync(TransferRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the text.
    /// </summary>
    /// <param name="rootId">The root identifier.</param>
    /// <param name="path">The path.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is the outcome for the text file content.</returns>
    Task<Result<TextFileContentDto>> ReadTextAsync(string rootId, string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the text.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is the outcome for the file system item.</returns>
    Task<Result<FileSystemItemDto>> SaveTextAsync(SaveTextFileRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the compress.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is the outcome for the file system item.</returns>
    Task<Result<FileSystemItemDto>> CompressAsync(CompressRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts the value.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is the outcome for the bulk operation result.</returns>
    Task<Result<BulkOperationResultDto>> ExtractAsync(ExtractRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the caller may download every item, then issues a short-lived, single-use token
    /// for <c>GET {prefix}/zip/{token}</c>. Lets a browser download a large zip with a plain link
    /// instead of buffering it in script.
    /// </summary>
    Task<Result<DownloadTicketDto>> CreateDownloadTicketAsync(
        DownloadTicketRequest request,
        CancellationToken cancellationToken = default);
}