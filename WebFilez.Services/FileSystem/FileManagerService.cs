using Microsoft.Extensions.Options;

namespace WebFilez.Services.FileSystem;

/// <summary>
/// The server-side file manager. Two rules hold throughout: every path arrives untrusted and is
/// mapped to disk only through <see cref="PathResolver"/>, and no operation starts before its
/// permission has been checked.
/// </summary>
public sealed partial class FileManagerService(
    ILogger<FileManagerService> logger,
    IFileRootProvider rootProvider,
    PathResolver resolver,
    FileNamePolicy fileNamePolicy,
    IFileActivitySink activitySink,
    DownloadTicketService downloadTickets,
    IOptionsMonitor<WebFilezOptions> options) : IFileManagerService
{
    public async Task<Result<IReadOnlyList<FileRootDto>>> GetRootsAsync(CancellationToken cancellationToken = default)
    {
        var roots = await rootProvider.GetRootsAsync(cancellationToken);
        var visible = new List<FileRootDto>(roots.Count);

        foreach (var root in roots)
        {
            var permissions = await resolver.GetPermissionsAsync(root, Constants.RootPath, cancellationToken);
            if (!permissions.HasAny(FilePermission.Browse | FilePermission.Read))
            {
                continue;
            }

            visible.Add(new FileRootDto(
                root.Id,
                root.DisplayName,
                permissions,
                root.IsReadOnly,
                !string.IsNullOrWhiteSpace(root.PublicUrl),
                root.ShowHiddenFiles));
        }

        return Result<IReadOnlyList<FileRootDto>>.Success(visible);
    }

    public async Task<Result<DirectoryListingDto>> ListAsync(
        ListingRequest request,
        CancellationToken cancellationToken = default)
    {
        var resolved = await resolver.RequireAsync(request.RootId, request.Path, FilePermission.Browse, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.To<DirectoryListingDto>();
        }

        var target = resolved.Value;

        if (!Directory.Exists(target.PhysicalPath))
        {
            return File.Exists(target.PhysicalPath)
                ? Result<DirectoryListingDto>.Invalid(new ValidationError(nameof(request.Path), "That path is a file, not a folder."))
                : Result<DirectoryListingDto>.NotFound($"'{target.VirtualPath}' was not found.");
        }

        var excludes = ExcludeMatcher.For(target.Root);
        var items = new List<FileSystemItemDto>();

        try
        {
            foreach (var entry in new DirectoryInfo(target.PhysicalPath).EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!FileItems.IsVisible(entry, target.Root, request.ShowHidden, excludes))
                {
                    continue;
                }

                string childPath = VirtualPath.Combine(target.VirtualPath, entry.Name);

                // A child the caller can neither browse nor read is left out entirely rather
                // than shown locked: listing the names of files you can't open is itself a leak.
                var childPermissions = await resolver.GetPermissionsAsync(target.Root, childPath, cancellationToken);
                if (!childPermissions.HasAny(FilePermission.Browse | FilePermission.Read))
                {
                    continue;
                }

                items.Add(FileItems.ToItem(entry, target.Root, childPath, childPermissions));
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Denied by the operating system while listing {Physical}", target.PhysicalPath);
            return Result<DirectoryListingDto>.Error("The server does not have permission to read that folder.");
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "I/O failure listing {Physical}", target.PhysicalPath);
            return Result<DirectoryListingDto>.Error("That folder could not be read.");
        }

        var filtered = items
            .Where(i => i.IsDirectory || FileKinds.Matches(i.Kind, request.Kind))
            .Where(i => string.IsNullOrWhiteSpace(request.Filter)
                || i.Name.Contains(request.Filter.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        int pageNumber = Math.Max(1, request.PageNumber);
        int pageSize = Math.Clamp(request.PageSize, 1, 10_000);

        var page = Sort(filtered, request.SortBy, request.Descending)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Result<DirectoryListingDto>.Success(new DirectoryListingDto(
            target.Root.Id,
            target.Root.DisplayName,
            target.VirtualPath,
            page,
            BuildBreadcrumbs(target.Root.DisplayName, target.VirtualPath),
            target.Permissions,
            filtered.Count,
            pageNumber,
            pageSize,
            filtered.Sum(i => i.SizeBytes),
            FileItems.BuildPublicUrl(target.Root, target.VirtualPath, directory: true)));
    }

    public async Task<Result<IReadOnlyList<FolderNodeDto>>> GetFoldersAsync(
        string rootId,
        string? path,
        CancellationToken cancellationToken = default)
    {
        var resolved = await resolver.RequireAsync(rootId, path, FilePermission.Browse, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.To<IReadOnlyList<FolderNodeDto>>();
        }

        var target = resolved.Value;
        if (!Directory.Exists(target.PhysicalPath))
        {
            return Result<IReadOnlyList<FolderNodeDto>>.NotFound($"'{target.VirtualPath}' was not found.");
        }

        var excludes = ExcludeMatcher.For(target.Root);
        var folders = new List<FolderNodeDto>();

        try
        {
            foreach (var directory in new DirectoryInfo(target.PhysicalPath).EnumerateDirectories())
            {
                if (!FileItems.IsVisible(directory, target.Root, showHidden: false, excludes))
                {
                    continue;
                }

                string childPath = VirtualPath.Combine(target.VirtualPath, directory.Name);
                var permissions = await resolver.GetPermissionsAsync(target.Root, childPath, cancellationToken);
                if (!permissions.Has(FilePermission.Browse))
                {
                    continue;
                }

                folders.Add(new FolderNodeDto(directory.Name, childPath, HasVisibleSubfolder(directory, target.Root, excludes)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not list folders in {Physical}", target.PhysicalPath);
            return Result<IReadOnlyList<FolderNodeDto>>.Error("That folder could not be read.");
        }

        folders.Sort((a, b) => NaturalComparer.Instance.Compare(a.Name, b.Name));
        return Result<IReadOnlyList<FolderNodeDto>>.Success(folders);
    }

    public async Task<Result<FileDetailDto>> GetDetailAsync(
        string rootId,
        string path,
        CancellationToken cancellationToken = default)
    {
        var resolved = await resolver.RequireAsync(rootId, path, FilePermission.None, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.To<FileDetailDto>();
        }

        var target = resolved.Value;
        bool isDirectory = Directory.Exists(target.PhysicalPath);

        if (!isDirectory && !File.Exists(target.PhysicalPath))
        {
            return Result<FileDetailDto>.NotFound($"'{target.VirtualPath}' was not found.");
        }

        FileSystemInfo info = isDirectory ? new DirectoryInfo(target.PhysicalPath) : new FileInfo(target.PhysicalPath);
        var kind = FileKinds.Classify(info.Name, isDirectory);

        (int? width, int? height) = kind == FileKind.Image && target.Permissions.Has(FilePermission.Read)
            ? await ImageProbe.TryGetDimensionsAsync(target.PhysicalPath, logger, cancellationToken)
            : (null, null);

        return Result<FileDetailDto>.Success(new FileDetailDto(
            target.Root.Id,
            target.VirtualPath,
            target.VirtualPath == Constants.RootPath ? target.Root.DisplayName : info.Name,
            isDirectory,
            info is FileInfo file ? file.Length : 0,
            info.LastWriteTimeUtc,
            info.CreationTimeUtc,
            kind,
            isDirectory ? "inode/directory" : FileKinds.GetMimeType(info.Name),
            target.Permissions,
            width,
            height,
            FileItems.BuildPublicUrl(target.Root, target.VirtualPath, isDirectory)));
    }

    public async Task<Result<DirectorySizeDto>> GetDirectorySizeAsync(
        string rootId,
        string path,
        CancellationToken cancellationToken = default)
    {
        var resolved = await resolver.RequireAsync(rootId, path, FilePermission.Browse, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.To<DirectorySizeDto>();
        }

        var target = resolved.Value;
        if (!Directory.Exists(target.PhysicalPath))
        {
            return Result<DirectorySizeDto>.NotFound($"'{target.VirtualPath}' was not found.");
        }

        long bytes = 0;
        int files = 0;
        var directories = new HashSet<string>(PhysicalPaths.Comparer);

        await foreach (var (file, _) in FileItems.EnumerateFilesAsync(resolver, target, FilePermission.None, cancellationToken))
        {
            bytes += file.Length;
            files++;

            if (file.DirectoryName is { } parent && !PhysicalPaths.AreEqual(parent, target.PhysicalPath))
            {
                directories.Add(parent);
            }
        }

        return Result<DirectorySizeDto>.Success(new DirectorySizeDto(target.VirtualPath, bytes, files, directories.Count));
    }

    private static bool HasVisibleSubfolder(DirectoryInfo directory, FileRoot root, ExcludeMatcher excludes)
    {
        try
        {
            return directory.EnumerateDirectories().Any(d => FileItems.IsVisible(d, root, showHidden: false, excludes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static List<BreadcrumbDto> BuildBreadcrumbs(string rootName, string virtualPath)
    {
        var crumbs = new List<BreadcrumbDto> { new(rootName, Constants.RootPath) };

        string accumulated = string.Empty;
        foreach (string segment in VirtualPath.GetSegments(virtualPath))
        {
            accumulated = $"{accumulated}/{segment}";
            crumbs.Add(new BreadcrumbDto(segment, accumulated));
        }

        return crumbs;
    }

    private static IEnumerable<FileSystemItemDto> Sort(
        IEnumerable<FileSystemItemDto> items,
        ListingSortField sortBy,
        bool descending)
    {
        // Folders lead regardless of the sort field, as in every desktop file manager.
        var ordered = items.OrderBy(i => i.IsDirectory ? 0 : 1);

        return sortBy switch
        {
            ListingSortField.Size => descending
                ? ordered.ThenByDescending(i => i.SizeBytes)
                : ordered.ThenBy(i => i.SizeBytes),

            ListingSortField.Modified => descending
                ? ordered.ThenByDescending(i => i.ModifiedAt)
                : ordered.ThenBy(i => i.ModifiedAt),

            ListingSortField.Kind => descending
                ? ordered.ThenByDescending(i => i.Kind).ThenBy(i => i.Name, NaturalComparer.Instance)
                : ordered.ThenBy(i => i.Kind).ThenBy(i => i.Name, NaturalComparer.Instance),

            _ => descending
                ? ordered.ThenByDescending(i => i.Name, NaturalComparer.Instance)
                : ordered.ThenBy(i => i.Name, NaturalComparer.Instance),
        };
    }

    private async Task RecordAsync(FileActivity activity, CancellationToken cancellationToken)
    {
        try
        {
            await activitySink.RecordAsync(activity, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Activity sink failed for {Kind} of {Path}", activity.Kind, activity.Path);
        }
    }

    private static Result<T> RejectRoot<T>(string field, string verb) =>
        Result<T>.Invalid(new ValidationError(field, $"The top-level folder can't be {verb}."));
}
