namespace WebFilez.Models;

/// <summary>A top-level folder the file manager exposes, such as "Uploads" or "Media".</summary>
/// <param name="CanShowHiddenFiles">Whether hidden items exist for the caller to reveal at all.</param>
public sealed record FileRootDto(
    string Id,
    string Name,
    FilePermission Permissions,
    bool IsReadOnly,
    bool HasPublicUrl,
    bool CanShowHiddenFiles);

/// <param name="PublicUrl">
/// Where the file or folder can be fetched without going through the file manager. Folder addresses
/// end with a slash. This is what picker mode hands back to store in content. Null when the file
/// has no public address. It is not a presigned URL unless the host opted into expiring URLs.
/// </param>
public sealed record FileSystemItemDto(
    string Name,
    string Path,
    bool IsDirectory,
    long SizeBytes,
    DateTime ModifiedAt,
    FileKind Kind,
    string? Extension,
    bool IsHidden,
    FilePermission Permissions,
    bool HasThumbnail,
    string? PublicUrl);

/// <summary>
/// Represents the breadcrumb DTO.
/// </summary>
/// <param name="Name">The name.</param>
/// <param name="Path">The path.</param>
public sealed record BreadcrumbDto(string Name, string Path);

/// <summary>
/// Represents the directory listing DTO.
/// </summary>
/// <param name="RootId">The root identifier.</param>
/// <param name="RootName">The root name.</param>
/// <param name="Path">The path.</param>
/// <param name="Items">The items.</param>
/// <param name="Breadcrumbs">The breadcrumbs.</param>
/// <param name="Permissions">The permissions.</param>
/// <param name="TotalCount">The total count.</param>
/// <param name="PageNumber">The page number.</param>
/// <param name="PageSize">The page size.</param>
/// <param name="TotalSizeBytes">The total size bytes.</param>
/// <param name="PublicUrl">The public URL.</param>
public sealed record DirectoryListingDto(
    string RootId,
    string RootName,
    string Path,
    IReadOnlyList<FileSystemItemDto> Items,
    IReadOnlyList<BreadcrumbDto> Breadcrumbs,
    FilePermission Permissions,
    int TotalCount,
    int PageNumber,
    int PageSize,
    long TotalSizeBytes,
    string? PublicUrl = null);

/// <summary>
/// Represents the listing request.
/// </summary>
/// <param name="RootId">The root identifier.</param>
/// <param name="Path">The path.</param>
/// <param name="Filter">The filter.</param>
/// <param name="Kind">The kind.</param>
/// <param name="SortBy">The sort by.</param>
/// <param name="Descending">The descending.</param>
/// <param name="ShowHidden">The show hidden.</param>
/// <param name="PageNumber">The page number.</param>
/// <param name="PageSize">The page size.</param>
public sealed record ListingRequest(
    string RootId,
    string? Path = null,
    string? Filter = null,
    KindFilter Kind = KindFilter.All,
    ListingSortField SortBy = ListingSortField.Name,
    bool Descending = false,
    bool ShowHidden = false,
    int PageNumber = 1,
    int PageSize = 1000);

/// <summary>A folder in the navigation tree, loaded one level at a time.</summary>
public sealed record FolderNodeDto(string Name, string Path, bool HasChildren);

/// <summary>
/// Represents the file detail DTO.
/// </summary>
/// <param name="RootId">The root identifier.</param>
/// <param name="Path">The path.</param>
/// <param name="Name">The name.</param>
/// <param name="IsDirectory">The is directory.</param>
/// <param name="SizeBytes">The size bytes.</param>
/// <param name="ModifiedAt">The modified at.</param>
/// <param name="CreatedAt">The created at.</param>
/// <param name="Kind">The kind.</param>
/// <param name="MimeType">The mime type.</param>
/// <param name="Permissions">The permissions.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
/// <param name="PublicUrl">The public URL.</param>
public sealed record FileDetailDto(
    string RootId,
    string Path,
    string Name,
    bool IsDirectory,
    long SizeBytes,
    DateTime ModifiedAt,
    DateTime CreatedAt,
    FileKind Kind,
    string MimeType,
    FilePermission Permissions,
    int? Width,
    int? Height,
    string? PublicUrl);

/// <summary>
/// Represents the directory size DTO.
/// </summary>
/// <param name="Path">The path.</param>
/// <param name="SizeBytes">The size bytes.</param>
/// <param name="FileCount">The file count.</param>
/// <param name="DirectoryCount">The directory count.</param>
public sealed record DirectorySizeDto(string Path, long SizeBytes, int FileCount, int DirectoryCount);

/// <summary>
/// Represents the create directory request.
/// </summary>
/// <param name="RootId">The root identifier.</param>
/// <param name="Path">The path.</param>
/// <param name="Name">The name.</param>
public sealed record CreateDirectoryRequest(string RootId, string Path, string Name);

/// <summary>Creates an empty file, typically to open straight away in the editor.</summary>
public sealed record CreateFileRequest(string RootId, string Path, string Name);

/// <summary>
/// Represents the rename request.
/// </summary>
/// <param name="RootId">The root identifier.</param>
/// <param name="Path">The path.</param>
/// <param name="NewName">The new name.</param>
public sealed record RenameRequest(string RootId, string Path, string NewName);

/// <summary>
/// Represents the delete request.
/// </summary>
/// <param name="RootId">The root identifier.</param>
/// <param name="Paths">The paths.</param>
public sealed record DeleteRequest(string RootId, IReadOnlyList<string> Paths);

/// <summary>
/// Copies or moves items into a folder, which may be in another root. Copying an item into the
/// folder it already lives in duplicates it under a free name, such as <c>report (2).pdf</c>.
/// </summary>
public sealed record TransferRequest(
    string RootId,
    IReadOnlyList<string> Paths,
    string DestinationRootId,
    string DestinationPath,
    bool IsCopy,
    bool Overwrite = false);

/// <summary>
/// Represents the file operation result DTO.
/// </summary>
/// <param name="Path">The path.</param>
/// <param name="Succeeded">The succeeded.</param>
/// <param name="Error">The error.</param>
public sealed record FileOperationResultDto(string Path, bool Succeeded, string? Error);

/// <summary>
/// Outcome of an operation over several items. Partial success is normal: one locked file
/// shouldn't stop the other twenty from being deleted.
/// </summary>
public sealed record BulkOperationResultDto(
    IReadOnlyList<FileOperationResultDto> Results,
    int SucceededCount,
    int FailedCount)
{
    /// <summary>
    /// Gets a value indicating whether this instance is complete success.
    /// </summary>
    public bool IsCompleteSuccess => FailedCount == 0;

    /// <summary>
    /// Creates a value from the value.
    /// </summary>
    /// <param name="results">The results.</param>
    /// <returns>The bulk operation result.</returns>
    public static BulkOperationResultDto From(IReadOnlyList<FileOperationResultDto> results)
    {
        int succeeded = results.Count(r => r.Succeeded);
        return new BulkOperationResultDto(results, succeeded, results.Count - succeeded);
    }
}

/// <summary>
/// Represents the text file content DTO.
/// </summary>
/// <param name="Path">The path.</param>
/// <param name="Name">The name.</param>
/// <param name="Content">The content.</param>
/// <param name="SizeBytes">The size bytes.</param>
/// <param name="ModifiedAt">The modified at.</param>
/// <param name="CanSave">The can save.</param>
public sealed record TextFileContentDto(
    string Path,
    string Name,
    string Content,
    long SizeBytes,
    DateTime ModifiedAt,
    bool CanSave);

/// <summary>
/// Saves editor content. <paramref name="ExpectedModifiedAt"/> is the timestamp the file had
/// when it was opened; if the file has changed since, the save is refused with a conflict rather
/// than silently overwriting someone else's edit.
/// </summary>
public sealed record SaveTextFileRequest(
    string RootId,
    string Path,
    string Content,
    DateTime? ExpectedModifiedAt = null);

/// <summary>
/// Represents the compress request.
/// </summary>
/// <param name="RootId">The root identifier.</param>
/// <param name="Paths">The paths.</param>
/// <param name="Path">The path.</param>
/// <param name="ArchiveName">The archive name.</param>
/// <param name="Format">The format.</param>
public sealed record CompressRequest(
    string RootId,
    IReadOnlyList<string> Paths,
    string Path,
    string? ArchiveName = null,
    ArchiveFormat Format = ArchiveFormat.Zip);

/// <summary>Extracts into the archive's own folder unless a destination is given.</summary>
public sealed record ExtractRequest(
    string RootId,
    string Path,
    string? DestinationPath = null,
    bool Overwrite = false);

/// <summary>Asks for a one-time link that streams a zip of several items.</summary>
public sealed record DownloadTicketRequest(string RootId, IReadOnlyList<string> Paths);

/// <summary>
/// Represents the download ticket DTO.
/// </summary>
/// <param name="Token">The token.</param>
/// <param name="FileName">The file name.</param>
public sealed record DownloadTicketDto(string Token, string FileName);

/// <summary>
/// Represents the upload result DTO.
/// </summary>
/// <param name="Item">The item.</param>
/// <param name="WasResized">The was resized.</param>
public sealed record UploadResultDto(FileSystemItemDto Item, bool WasResized);

/// <summary>
/// Finds files and folders by name, extension or kind, across one root or every root the caller
/// can browse. <paramref name="Path"/> limits the walk to that folder and everything under it,
/// and requires <paramref name="RootId"/>.
/// </summary>
public sealed record SearchRequest(
    string Query,
    string? RootId = null,
    string? Path = null,
    KindFilter Kind = KindFilter.All,
    bool ShowHidden = false,
    ListingSortField SortBy = ListingSortField.Name,
    bool Descending = false,
    int MaxResults = 200);

/// <summary>One search match. <paramref name="Location"/> is the root and parent folder, for display.</summary>
public sealed record SearchHitDto(
    string RootId,
    string RootName,
    string Path,
    string Name,
    string Location,
    bool IsDirectory,
    long SizeBytes,
    DateTime ModifiedAt,
    FileKind Kind,
    string? Extension,
    bool IsHidden,
    FilePermission Permissions,
    bool HasThumbnail,
    string? PublicUrl);

/// <param name="Truncated">True when more matches exist than <see cref="SearchRequest.MaxResults"/>, or a root's index was capped.</param>
public sealed record SearchResultDto(
    string Query,
    string? RootId,
    string? Path,
    IReadOnlyList<SearchHitDto> Hits,
    bool Truncated);

/// <summary>Outcome of an anonymous request to one insertable file URL. <paramref name="Warning"/> is set when visitors would not be able to read it.</summary>
public sealed record PublicUrlProbeResult(
    bool Enabled,
    string? RootId,
    string? Path,
    string? Url,
    int? StatusCode,
    string? Warning);