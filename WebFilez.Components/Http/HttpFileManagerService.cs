namespace WebFilez.Components.Http;

/// <summary><see cref="IFileManagerService"/> over the WebFilez HTTP API, for WebAssembly hosts.</summary>
internal sealed class HttpFileManagerService(WebFilezApiClient api) : IFileManagerService
{
    private static WebFilezJsonContext Json => WebFilezJsonContext.Default;

    public Task<Result<IReadOnlyList<FileRootDto>>> GetRootsAsync(CancellationToken cancellationToken = default) =>
        api.GetAsync(ApiRoutes.Roots, Json.IReadOnlyListFileRootDto, cancellationToken);

    public Task<Result<DirectoryListingDto>> ListAsync(ListingRequest request, CancellationToken cancellationToken = default)
    {
        string url = Query(ApiRoutes.List, ("root", request.RootId), ("path", request.Path))
            + $"&filter={Escape(request.Filter)}&kind={request.Kind}&sort={request.SortBy}"
            + $"&desc={Bool(request.Descending)}&hidden={Bool(request.ShowHidden)}"
            + $"&page={request.PageNumber}&size={request.PageSize}";

        return api.GetAsync(url, Json.DirectoryListingDto, cancellationToken);
    }

    public Task<Result<IReadOnlyList<FolderNodeDto>>> GetFoldersAsync(string rootId, string? path, CancellationToken cancellationToken = default) =>
        api.GetAsync(Query(ApiRoutes.Folders, ("root", rootId), ("path", path)), Json.IReadOnlyListFolderNodeDto, cancellationToken);

    public Task<Result<FileDetailDto>> GetDetailAsync(string rootId, string path, CancellationToken cancellationToken = default) =>
        api.GetAsync(Query(ApiRoutes.Detail, ("root", rootId), ("path", path)), Json.FileDetailDto, cancellationToken);

    public Task<Result<DirectorySizeDto>> GetDirectorySizeAsync(string rootId, string path, CancellationToken cancellationToken = default) =>
        api.GetAsync(Query(ApiRoutes.Size, ("root", rootId), ("path", path)), Json.DirectorySizeDto, cancellationToken);

    public Task<Result<FileSystemItemDto>> CreateDirectoryAsync(CreateDirectoryRequest request, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Post, ApiRoutes.Directories, request, Json.CreateDirectoryRequest, Json.FileSystemItemDto, cancellationToken);

    public Task<Result<FileSystemItemDto>> CreateFileAsync(CreateFileRequest request, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Post, ApiRoutes.Files, request, Json.CreateFileRequest, Json.FileSystemItemDto, cancellationToken);

    public Task<Result<FileSystemItemDto>> RenameAsync(RenameRequest request, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Post, ApiRoutes.Rename, request, Json.RenameRequest, Json.FileSystemItemDto, cancellationToken);

    public Task<Result<BulkOperationResultDto>> DeleteAsync(DeleteRequest request, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Post, ApiRoutes.Delete, request, Json.DeleteRequest, Json.BulkOperationResultDto, cancellationToken);

    public Task<Result<BulkOperationResultDto>> TransferAsync(TransferRequest request, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Post, ApiRoutes.Transfer, request, Json.TransferRequest, Json.BulkOperationResultDto, cancellationToken);

    public Task<Result<TextFileContentDto>> ReadTextAsync(string rootId, string path, CancellationToken cancellationToken = default) =>
        api.GetAsync(Query(ApiRoutes.Text, ("root", rootId), ("path", path)), Json.TextFileContentDto, cancellationToken);

    public Task<Result<FileSystemItemDto>> SaveTextAsync(SaveTextFileRequest request, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Put, ApiRoutes.Text, request, Json.SaveTextFileRequest, Json.FileSystemItemDto, cancellationToken);

    public Task<Result<FileSystemItemDto>> CompressAsync(CompressRequest request, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Post, ApiRoutes.Compress, request, Json.CompressRequest, Json.FileSystemItemDto, cancellationToken);

    public Task<Result<BulkOperationResultDto>> ExtractAsync(ExtractRequest request, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Post, ApiRoutes.Extract, request, Json.ExtractRequest, Json.BulkOperationResultDto, cancellationToken);

    public Task<Result<DownloadTicketDto>> CreateDownloadTicketAsync(DownloadTicketRequest request, CancellationToken cancellationToken = default) =>
        api.SendAsync(HttpMethod.Post, ApiRoutes.Tickets, request, Json.DownloadTicketRequest, Json.DownloadTicketDto, cancellationToken);

    private static string Query(string route, params (string Name, string? Value)[] values) =>
        route + "?" + string.Join('&', values.Select(v => $"{v.Name}={Escape(v.Value)}"));

    private static string Escape(string? value) => Uri.EscapeDataString(value ?? string.Empty);

    private static string Bool(bool value) => value ? "true" : "false";
}