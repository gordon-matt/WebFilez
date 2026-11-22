namespace WebFilez.Models;

/// <summary>A top-level folder the file manager exposes, such as "Uploads" or "Media".</summary>
/// <param name="Id">The root id.</param>
/// <param name="Name">The name shown in the UI.</param>
/// <param name="Permissions">What the caller may do in this root.</param>
/// <param name="IsReadOnly">Whether the root refuses every change.</param>
/// <param name="HasPublicUrl">Whether files here have an insertable address.</param>
/// <param name="CanShowHiddenFiles">Whether hidden items exist for the caller to reveal at all.</param>
/// <param name="SupportsUndo">Whether this root's storage can reverse a change. Cloud roots leave this off.</param>
public sealed record FileRootDto(
    string Id,
    string Name,
    FilePermission Permissions,
    bool IsReadOnly,
    bool HasPublicUrl,
    bool CanShowHiddenFiles,
    bool SupportsUndo);

/// <summary>One file or folder in a listing.</summary>
/// <param name="Name">The file or folder name.</param>
/// <param name="Path">The virtual path inside the root.</param>
/// <param name="IsDirectory">Whether this is a folder.</param>
/// <param name="SizeBytes">The size in bytes. A folder's own entry is not the size of its contents.</param>
/// <param name="ModifiedAt">When it was last changed, in UTC.</param>
/// <param name="Kind">The kind used for the icon and the type filter.</param>
/// <param name="Extension">The extension, including the dot, or <see langword="null"/> for a folder.</param>
/// <param name="IsHidden">Whether the name is hidden. Shown only when the caller asked for hidden files.</param>
/// <param name="Permissions">What the caller may do to this item.</param>
/// <param name="HasThumbnail">Whether a thumbnail can be requested.</param>
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

/// <summary>One step from the root to the open folder.</summary>
/// <param name="Name">The folder name. The first crumb is the root's display name.</param>
/// <param name="Path">The virtual path of that folder.</param>
public sealed record BreadcrumbDto(string Name, string Path);

/// <summary>One page of a folder.</summary>
/// <param name="RootId">The root.</param>
/// <param name="RootName">The root's display name.</param>
/// <param name="Path">The folder that was listed.</param>
/// <param name="Items">The page. Folders stay in the list for every kind except the folders filter.</param>
/// <param name="Breadcrumbs">The path from the root, including this folder.</param>
/// <param name="Permissions">What the caller may do in this folder.</param>
/// <param name="TotalCount">How many items matched, before paging.</param>
/// <param name="PageNumber">The page, starting at 1.</param>
/// <param name="PageSize">How many items this page holds at most.</param>
/// <param name="TotalSizeBytes">The sum of the matched items' own sizes, not a recursive folder walk.</param>
/// <param name="PublicUrl">The folder's insertable address, or <see langword="null"/>.</param>
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

/// <summary>Which folder to list, and how to filter, sort, and page it.</summary>
/// <param name="RootId">The root.</param>
/// <param name="Path">The folder. <see langword="null"/> is the root.</param>
/// <param name="Filter">Text matched anywhere in the name, ignoring case. Blank matches every name.</param>
/// <param name="Kind">The type filter. The default is all.</param>
/// <param name="SortBy">The sort column. The default is name.</param>
/// <param name="Descending">Whether the sort runs high to low.</param>
/// <param name="ShowHidden">Whether hidden names are included. The root must allow them.</param>
/// <param name="PageNumber">The page, starting at 1. A lower value becomes 1.</param>
/// <param name="PageSize">How many items to return. Clamped from 1 to 10,000. The default is 1,000.</param>
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

/// <summary>The info panel for one file or folder.</summary>
/// <param name="RootId">The root.</param>
/// <param name="Path">The virtual path.</param>
/// <param name="Name">The name. The root uses its display name.</param>
/// <param name="IsDirectory">Whether this is a folder.</param>
/// <param name="SizeBytes">The size in bytes.</param>
/// <param name="ModifiedAt">When it was last changed, in UTC.</param>
/// <param name="CreatedAt">When it was created, in UTC.</param>
/// <param name="Kind">The kind.</param>
/// <param name="MimeType">The media type. A folder is <c>inode/directory</c>.</param>
/// <param name="Permissions">What the caller may do here.</param>
/// <param name="Width">The image width in pixels, or <see langword="null"/> when it was not read.</param>
/// <param name="Height">The image height in pixels, or <see langword="null"/> when it was not read.</param>
/// <param name="PublicUrl">The insertable address, or <see langword="null"/>.</param>
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

/// <summary>The size of a folder and everything under it.</summary>
/// <param name="Path">The folder that was measured.</param>
/// <param name="SizeBytes">The sum of the file sizes underneath.</param>
/// <param name="FileCount">How many files were counted.</param>
/// <param name="DirectoryCount">How many folders sit underneath, not counting this folder.</param>
public sealed record DirectorySizeDto(string Path, long SizeBytes, int FileCount, int DirectoryCount);

/// <summary>Creates a folder inside another folder.</summary>
/// <param name="RootId">The root.</param>
/// <param name="Path">The parent folder. The root is <c>/</c>.</param>
/// <param name="Name">The new folder name. It is cleaned before it is stored.</param>
public sealed record CreateDirectoryRequest(string RootId, string Path, string Name);

/// <summary>Creates an empty file, typically to open straight away in the editor.</summary>
public sealed record CreateFileRequest(string RootId, string Path, string Name);

/// <summary>Renames one file or folder in place.</summary>
/// <param name="RootId">The root.</param>
/// <param name="Path">The item to rename.</param>
/// <param name="NewName">The new name. It is cleaned before it is stored.</param>
public sealed record RenameRequest(string RootId, string Path, string NewName);

/// <summary>Deletes every listed path. One failure does not stop the rest.</summary>
/// <param name="RootId">The root.</param>
/// <param name="Paths">The items. An empty list is invalid.</param>
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
    bool Overwrite = false,
    bool RenameOnConflict = false);

/// <summary>What happened to one item in a bulk operation.</summary>
/// <param name="Path">The path the operation was asked to change.</param>
/// <param name="Succeeded">Whether this item was changed.</param>
/// <param name="Error">Why it failed, or <see langword="null"/> when it succeeded.</param>
/// <param name="ResultPath">
/// Where a successful copy or move landed. The same as <paramref name="Path"/> when a move
/// changed nothing. Absent when the operation did not write.
/// </param>
/// <param name="NameConflict">True when the destination already has this name and nothing was written.</param>
public sealed record FileOperationResultDto(
    string Path,
    bool Succeeded,
    string? Error,
    string? ResultPath = null,
    bool NameConflict = false);

/// <summary>
/// Outcome of an operation over several items. Partial success is normal: one locked file
/// shouldn't stop the other twenty from being deleted.
/// </summary>
public sealed record BulkOperationResultDto(
    IReadOnlyList<FileOperationResultDto> Results,
    int SucceededCount,
    int FailedCount)
{
    /// <summary>Whether every item succeeded.</summary>
    public bool IsCompleteSuccess => FailedCount == 0;

    /// <summary>Counts the successes and failures in <paramref name="results"/>.</summary>
    /// <param name="results">One outcome per item.</param>
    /// <returns>The combined result.</returns>
    public static BulkOperationResultDto From(IReadOnlyList<FileOperationResultDto> results)
    {
        int succeeded = results.Count(r => r.Succeeded);
        return new BulkOperationResultDto(results, succeeded, results.Count - succeeded);
    }
}

/// <summary>A file opened as UTF-8 text.</summary>
/// <param name="Path">The file.</param>
/// <param name="Name">The file name.</param>
/// <param name="Content">The text.</param>
/// <param name="SizeBytes">The size before it was decoded.</param>
/// <param name="ModifiedAt">When it was last changed, in UTC. Send this back to detect a concurrent edit.</param>
/// <param name="CanSave">Whether the caller may write it back.</param>
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

/// <summary>Writes an archive of the selected items into a folder.</summary>
/// <param name="RootId">The root.</param>
/// <param name="Paths">The items to include. An empty list is invalid.</param>
/// <param name="Path">The folder that receives the archive.</param>
/// <param name="ArchiveName">The file name. <see langword="null"/> derives one from the selection.</param>
/// <param name="Format">Zip, tar, or gzipped tar. The default is zip.</param>
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

/// <summary>A one-time link that streams a zip. The token lasts five minutes.</summary>
/// <param name="Token">The token to put in the zip URL.</param>
/// <param name="FileName">The name the browser should save.</param>
public sealed record DownloadTicketDto(string Token, string FileName);

/// <summary>A file that was stored by an upload.</summary>
/// <param name="Item">The stored file.</param>
/// <param name="WasResized">Whether a large image was shrunk before it was stored.</param>
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

/// <summary>The matches for one search.</summary>
/// <param name="Query">The text that was searched.</param>
/// <param name="RootId">The root that was searched, or <see langword="null"/> when every visible root was searched.</param>
/// <param name="Path">The folder the search was limited to, or <see langword="null"/>.</param>
/// <param name="Hits">The matches, already sorted and capped.</param>
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
