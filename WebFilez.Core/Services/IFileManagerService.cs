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
    /// <summary>
    /// The roots the current caller can browse or read. A root they cannot see is left out.
    /// A confined scope that names one root leaves the others out.
    /// </summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The visible roots.</returns>
    Task<Result<IReadOnlyList<FileRootDto>>> GetRootsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists one folder. Needs browse permission. A file path is invalid, and an unknown path is not found.
    /// Hidden, excluded, and items the caller can neither browse nor read are left out. Folders stay in the list for every kind except the folders filter, which shows directories only.
    /// The name filter matches anywhere in the name, ignoring case. A page below 1 becomes 1, and the page size is clamped from 1 to 10,000.
    /// </summary>
    /// <param name="request">Which folder, and how to filter, sort, and page it.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>One page, with the filtered total. A folder the operating system refuses is an error.</returns>
    Task<Result<DirectoryListingDto>> ListAsync(ListingRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches an index of names, extensions and kinds. The index is built on first use and then
    /// kept current from the same activity stream as <c>IFileActivitySink</c>. Replace
    /// <c>IFileIndex</c> to store that index somewhere else.
    /// A blank query is invalid, and a query longer than 200 characters is invalid. A folder without a root is invalid.
    /// An unknown root is not found. A confined scope cannot search another root, and it cannot search above its folder.
    /// </summary>
    /// <param name="request">The text, the roots, and how many hits to return. The maximum is clamped from 1 to 1,000, and a non-positive value uses 200.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The hits. Truncated is set when more matches exist than the maximum, or a root's index was capped.</returns>
    Task<Result<SearchResultDto>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// The immediate subfolders of a folder, for the navigation tree. Hidden folders stay out.
    /// A child the caller cannot browse is left out. Names are sorted naturally.
    /// </summary>
    /// <param name="rootId">The root.</param>
    /// <param name="path">The folder. <see langword="null"/> is the root.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The folders, or not found when the path is not a folder.</returns>
    Task<Result<IReadOnlyList<FolderNodeDto>>> GetFoldersAsync(
        string rootId,
        string? path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One file or folder. Image width and height are filled when the caller can read the file.
    /// A directory reports the directory media type. The root itself uses the root's display name.
    /// </summary>
    /// <param name="rootId">The root.</param>
    /// <param name="path">The virtual path.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The detail, or not found.</returns>
    Task<Result<FileDetailDto>> GetDetailAsync(string rootId, string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks a folder recursively, so it is only requested on demand.
    /// The directory count is the folders underneath, not the folder itself.
    /// </summary>
    /// <param name="rootId">The root.</param>
    /// <param name="path">The folder.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <returns>The size, or not found when the path is not a folder.</returns>
    Task<Result<DirectorySizeDto>> GetDirectorySizeAsync(
        string rootId,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a folder inside a folder the caller can create in. The name is cleaned by the file-name options.
    /// A name that already exists is a conflict. A name the provider cannot map is invalid.
    /// </summary>
    /// <param name="request">The parent folder and the new name.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The new folder, or invalid, conflict, forbidden, or not found.</returns>
    Task<Result<FileSystemItemDto>> CreateDirectoryAsync(
        CreateDirectoryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an empty file, typically to open straight away in the editor.
    /// The extension must be allowed. A name that already exists is a conflict.
    /// </summary>
    /// <param name="request">The parent folder and the new name.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The new file, or invalid, conflict, forbidden, or not found.</returns>
    Task<Result<FileSystemItemDto>> CreateFileAsync(CreateFileRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a file or folder. The root cannot be renamed. A name that already exists is a conflict, except a change of case on a case-insensitive store.
    /// A name the caller could not open afterwards is invalid.
    /// </summary>
    /// <param name="request">The item and its new name.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The item at its new path, or invalid, conflict, forbidden, or not found.</returns>
    Task<Result<FileSystemItemDto>> RenameAsync(RenameRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes each path. An empty list is invalid. One failure does not stop the rest, and the result says which ones failed.
    /// The root cannot be deleted. An item that is already gone is a failed item, and the call still succeeds.
    /// </summary>
    /// <param name="request">The root and the paths.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>One result per path.</returns>
    Task<Result<BulkOperationResultDto>> DeleteAsync(DeleteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies or moves items into a folder, which may be in another root. Copying an item onto itself duplicates it under a free name.
    /// Moving an item onto itself succeeds and changes nothing. A folder cannot be placed inside itself.
    /// When the destination name exists and neither overwrite nor rename-on-conflict is set, that item reports a name conflict and nothing is written.
    /// A copy needs read permission. A move needs move and delete. The destination needs create, and modify as well when overwriting.
    /// Different providers stream the bytes. The same provider uses its own copy or move.
    /// </summary>
    /// <param name="request">The items, the destination, and how to treat a name that already exists.</param>
    /// <param name="cancellationToken">Cancels the transfer.</param>
    /// <returns>One result per item. A missing destination folder fails the whole call.</returns>
    Task<Result<BulkOperationResultDto>> TransferAsync(TransferRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a file as UTF-8 text. A file larger than the editor limit is invalid.
    /// A file that contains a NUL byte is treated as binary and is invalid. The caller needs read permission.
    /// </summary>
    /// <param name="rootId">The root.</param>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The text and whether the caller may save it, or invalid or not found.</returns>
    Task<Result<TextFileContentDto>> ReadTextAsync(string rootId, string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes UTF-8 text over an existing file. The caller needs modify permission.
    /// Text larger than the editor limit is invalid. When the file's modified time differs from the time it was opened by more than a millisecond, the save is a conflict.
    /// </summary>
    /// <param name="request">The file, the text, and the timestamp it had when it was opened.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The saved file, or invalid, conflict, forbidden, or not found.</returns>
    Task<Result<FileSystemItemDto>> SaveTextAsync(SaveTextFileRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a zip, tar, or gzipped tar of the selected items into a folder the caller can create in.
    /// The caller also needs compress permission there, and read permission on every source. An empty selection is invalid.
    /// A missing archive name is derived from the selection.
    /// </summary>
    /// <param name="request">The items, the folder that receives the archive, and the format.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The new archive, or invalid, forbidden, or not found.</returns>
    Task<Result<FileSystemItemDto>> CompressAsync(CompressRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts an archive into its own folder, or into <c>DestinationPath</c> when one is given.
    /// An entry that would land outside the destination is skipped. Extraction stops when the configured byte or entry ceiling is passed.
    /// The caller needs read permission on the archive, and create and extract permission on the destination. Overwrite also needs modify.
    /// </summary>
    /// <param name="request">The archive and, optionally, where to put the files.</param>
    /// <param name="cancellationToken">Cancels the extract.</param>
    /// <returns>One result per entry that was considered. A ceiling stops the rest.</returns>
    Task<Result<BulkOperationResultDto>> ExtractAsync(ExtractRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the caller may download every item, then issues a short-lived, single-use token
    /// for <c>GET {prefix}/zip/{token}</c>. The token lasts five minutes and can be used once.
    /// An empty selection is invalid. One path the caller cannot download fails the whole call.
    /// </summary>
    /// <param name="request">The root and the items.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The token and the suggested file name.</returns>
    Task<Result<DownloadTicketDto>> CreateDownloadTicketAsync(
        DownloadTicketRequest request,
        CancellationToken cancellationToken = default);
}