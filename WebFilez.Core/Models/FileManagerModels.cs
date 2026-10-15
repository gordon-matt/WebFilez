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
/// Where the file or folder can be fetched without going through the file manager, when the root
/// has a <c>PublicUrl</c>. Folder addresses end with a slash. This is what picker mode hands back.
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

public sealed record BreadcrumbDto(string Name, string Path);

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

public sealed record DirectorySizeDto(string Path, long SizeBytes, int FileCount, int DirectoryCount);

public sealed record CreateDirectoryRequest(string RootId, string Path, string Name);

/// <summary>Creates an empty file, typically to open straight away in the editor.</summary>
public sealed record CreateFileRequest(string RootId, string Path, string Name);

public sealed record RenameRequest(string RootId, string Path, string NewName);

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
    public bool IsCompleteSuccess => FailedCount == 0;

    public static BulkOperationResultDto From(IReadOnlyList<FileOperationResultDto> results)
    {
        int succeeded = results.Count(r => r.Succeeded);
        return new BulkOperationResultDto(results, succeeded, results.Count - succeeded);
    }
}

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

public sealed record DownloadTicketDto(string Token, string FileName);

public sealed record UploadResultDto(FileSystemItemDto Item, bool WasResized);