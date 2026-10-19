using Microsoft.Extensions.Options;
using WebFilez.Services.Search;
using WebFilez.Services.Storage;

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
    IFileIndex index,
    DownloadTicketService downloadTickets,
    IFileUrlResolver urlResolver,
    IOptionsMonitor<WebFilezOptions> options) : IFileManagerService
{
    public async Task<Result<IReadOnlyList<FileRootDto>>> GetRootsAsync(CancellationToken cancellationToken = default)
    {
        var roots = await rootProvider.GetRootsAsync(cancellationToken);
        var visible = new List<FileRootDto>(roots.Count);

        foreach (var root in roots)
        {
            if (resolver.Scope.BasePath is not null
                && resolver.Scope.RootId is not null
                && !resolver.Scope.RootId.Equals(root.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Permissions at the confined folder, not the real root: the real root is above the
            // caller and would otherwise look like it has no access at all.
            string gate = resolver.Scope.BasePath is not null ? resolver.Scope.BasePath : Constants.RootPath;
            var permissions = await resolver.GetPermissionsAsync(root, gate, cancellationToken);
            if (!permissions.HasAny(FilePermission.Browse | FilePermission.Read))
            {
                continue;
            }

            visible.Add(new FileRootDto(
                root.Id,
                root.DisplayName,
                permissions,
                root.IsReadOnly,
                await ResolvePublicUrlAsync(root, Constants.RootPath, isDirectory: true, cancellationToken) is not null,
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
        var current = await target.Storage.GetAsync(target.Location, cancellationToken);

        if (current is not { IsDirectory: true })
        {
            return current is not null
                ? Result<DirectoryListingDto>.Invalid(new ValidationError(nameof(request.Path), "That path is a file, not a folder."))
                : Result<DirectoryListingDto>.NotFound($"'{target.VirtualPath}' was not found.");
        }

        var excludes = ExcludeMatcher.For(target.Root);
        var items = new List<FileSystemItemDto>();

        try
        {
            await foreach (var entry in target.Storage.ListAsync(target.Location, cancellationToken))
            {
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

                items.Add(await ToItemAsync(entry, target.Root, childPath, childPermissions, cancellationToken));
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
            BuildBreadcrumbs(target.Root.DisplayName, target.VirtualPath, resolver.Scope),
            target.Permissions,
            filtered.Count,
            pageNumber,
            pageSize,
            filtered.Sum(i => i.SizeBytes),
            await ResolvePublicUrlAsync(target.Root, target.VirtualPath, isDirectory: true, cancellationToken)));
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
        if (await target.Storage.GetAsync(target.Location, cancellationToken) is not { IsDirectory: true })
        {
            return Result<IReadOnlyList<FolderNodeDto>>.NotFound($"'{target.VirtualPath}' was not found.");
        }

        var excludes = ExcludeMatcher.For(target.Root);
        var folders = new List<FolderNodeDto>();

        try
        {
            await foreach (var directory in target.Storage.ListAsync(target.Location, cancellationToken))
            {
                if (!directory.IsDirectory || !FileItems.IsVisible(directory, target.Root, showHidden: false, excludes))
                {
                    continue;
                }

                string childPath = VirtualPath.Combine(target.VirtualPath, directory.Name);
                var permissions = await resolver.GetPermissionsAsync(target.Root, childPath, cancellationToken);
                if (!permissions.Has(FilePermission.Browse))
                {
                    continue;
                }

                folders.Add(new FolderNodeDto(
                    directory.Name,
                    childPath,
                    await HasVisibleSubfolderAsync(target, childPath, excludes, cancellationToken)));
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
        var info = await target.Storage.GetAsync(target.Location, cancellationToken);
        if (info is null)
        {
            return Result<FileDetailDto>.NotFound($"'{target.VirtualPath}' was not found.");
        }

        var kind = FileKinds.Classify(info.Name, info.IsDirectory);
        (int? width, int? height) = (null, null);

        if (kind == FileKind.Image && target.Permissions.Has(FilePermission.Read))
        {
            await using var content = await target.Storage.OpenReadAsync(target.Location, cancellationToken);
            (width, height) = await ImageProbe.TryGetDimensionsAsync(content, info.Name, logger, cancellationToken);
        }

        return Result<FileDetailDto>.Success(new FileDetailDto(
            target.Root.Id,
            target.VirtualPath,
            target.VirtualPath == Constants.RootPath ? target.Root.DisplayName : info.Name,
            info.IsDirectory,
            info.SizeBytes,
            info.ModifiedAt,
            info.CreatedAt,
            kind,
            info.IsDirectory ? "inode/directory" : FileKinds.GetMimeType(info.Name),
            target.Permissions,
            width,
            height,
            await ResolvePublicUrlAsync(target.Root, target.VirtualPath, info.IsDirectory, cancellationToken)));
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
        if (await target.Storage.GetAsync(target.Location, cancellationToken) is not { IsDirectory: true })
        {
            return Result<DirectorySizeDto>.NotFound($"'{target.VirtualPath}' was not found.");
        }

        long bytes = 0;
        int files = 0;
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await foreach (var (file, virtualPath) in FileItems.EnumerateFilesAsync(resolver, target, FilePermission.None, cancellationToken))
        {
            bytes += file.SizeBytes;
            files++;

            if (VirtualPath.GetParent(virtualPath) is { } parent
                && !parent.Equals(target.VirtualPath, StringComparison.OrdinalIgnoreCase))
            {
                directories.Add(parent);
            }
        }

        return Result<DirectorySizeDto>.Success(new DirectorySizeDto(target.VirtualPath, bytes, files, directories.Count));
    }

    private static async Task<bool> HasVisibleSubfolderAsync(
        ResolvedPath parent,
        string childVirtual,
        ExcludeMatcher excludes,
        CancellationToken cancellationToken)
    {
        if (!parent.Storage.TryMap(parent.Root, childVirtual, out var child))
        {
            return false;
        }

        try
        {
            await foreach (var entry in parent.Storage.ListAsync(child, cancellationToken))
            {
                if (entry.IsDirectory && FileItems.IsVisible(entry, parent.Root, showHidden: false, excludes))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static List<BreadcrumbDto> BuildBreadcrumbs(string rootName, string virtualPath, FileScope scope)
    {
        var crumbs = new List<BreadcrumbDto> { new(rootName, Constants.RootPath) };

        string accumulated = string.Empty;
        foreach (string segment in VirtualPath.GetSegments(virtualPath))
        {
            accumulated = $"{accumulated}/{segment}";
            crumbs.Add(new BreadcrumbDto(segment, accumulated));
        }

        if (scope.BasePath is not { } floor)
        {
            return crumbs;
        }

        // The confined folder is this listing's root, so crumbs above it are not offered as links.
        var visible = crumbs.Where(crumb => VirtualPath.IsSameOrUnder(crumb.Path, floor)).ToList();
        return visible.Count == 0 ? crumbs : visible;
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

    private ValueTask<FileSystemItemDto> ToItemAsync(
        StorageEntry entry,
        FileRoot root,
        string virtualPath,
        FilePermission permissions,
        CancellationToken cancellationToken) =>
        FileItems.ToItemAsync(
            entry,
            root,
            virtualPath,
            permissions,
            urlResolver,
            options.CurrentValue.AllowExpiringUrls,
            cancellationToken);

    private ValueTask<string?> ResolvePublicUrlAsync(
        FileRoot root,
        string virtualPath,
        bool isDirectory,
        CancellationToken cancellationToken) =>
        urlResolver.ResolveAsync(
            FileUrlContext.For(root, virtualPath, isDirectory, options.CurrentValue.AllowExpiringUrls),
            cancellationToken);

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

        try
        {
            index.Apply(activity);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Search index failed for {Kind} of {Path}", activity.Kind, activity.Path);
        }
    }

    private static Result<T> RejectRoot<T>(string field, string verb) =>
        Result<T>.Invalid(new ValidationError(field, $"The top-level folder can't be {verb}."));
}