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

    Task<Result<DirectoryListingDto>> ListAsync(ListingRequest request, CancellationToken cancellationToken = default);

    /// <summary>The immediate subfolders of a folder, for the navigation tree.</summary>
    Task<Result<IReadOnlyList<FolderNodeDto>>> GetFoldersAsync(
        string rootId,
        string? path,
        CancellationToken cancellationToken = default);

    Task<Result<FileDetailDto>> GetDetailAsync(string rootId, string path, CancellationToken cancellationToken = default);

    /// <summary>Walks a folder recursively, so it is only requested on demand.</summary>
    Task<Result<DirectorySizeDto>> GetDirectorySizeAsync(
        string rootId,
        string path,
        CancellationToken cancellationToken = default);

    Task<Result<FileSystemItemDto>> CreateDirectoryAsync(
        CreateDirectoryRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<FileSystemItemDto>> CreateFileAsync(CreateFileRequest request, CancellationToken cancellationToken = default);

    Task<Result<FileSystemItemDto>> RenameAsync(RenameRequest request, CancellationToken cancellationToken = default);

    Task<Result<BulkOperationResultDto>> DeleteAsync(DeleteRequest request, CancellationToken cancellationToken = default);

    Task<Result<BulkOperationResultDto>> TransferAsync(TransferRequest request, CancellationToken cancellationToken = default);

    Task<Result<TextFileContentDto>> ReadTextAsync(string rootId, string path, CancellationToken cancellationToken = default);

    Task<Result<FileSystemItemDto>> SaveTextAsync(SaveTextFileRequest request, CancellationToken cancellationToken = default);

    Task<Result<FileSystemItemDto>> CompressAsync(CompressRequest request, CancellationToken cancellationToken = default);

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