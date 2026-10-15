using WebFilez.Services.Storage;

namespace WebFilez.Services.FileSystem;

public sealed partial class FileManagerService
{
    public async Task<Result<FileSystemItemDto>> CreateDirectoryAsync(
        CreateDirectoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = fileNamePolicy.Apply(request.Name, isDirectory: true, nameof(request.Name));
        if (!name.IsSuccess)
        {
            return name.To<FileSystemItemDto>();
        }

        // Create is checked on the parent: the new folder doesn't exist yet, so there is nothing
        // at its own path for a policy to judge.
        var parent = await resolver.RequireAsync(request.RootId, request.Path, FilePermission.Create, cancellationToken);
        if (!parent.IsSuccess)
        {
            return parent.To<FileSystemItemDto>();
        }

        var created = await ResolveNewChildAsync(parent.Value, name.Value, cancellationToken);
        if (!created.IsSuccess)
        {
            return created.To<FileSystemItemDto>();
        }

        try
        {
            await parent.Value.Storage.CreateDirectoryAsync(created.Value.Location, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not create directory {Path}", created.Value.VirtualPath);
            return Result<FileSystemItemDto>.Error($"'{name.Value}' could not be created.");
        }

        await RecordAsync(new FileActivity(FileActivityKind.CreateFolder, request.RootId, created.Value.VirtualPath), cancellationToken);

        var createdEntry = await parent.Value.Storage.GetAsync(created.Value.Location, cancellationToken);
        return Result<FileSystemItemDto>.Success(FileItems.ToItem(
            createdEntry ?? new StorageEntry(name.Value, true, 0, DateTime.UtcNow, DateTime.UtcNow, false, false),
            parent.Value.Root,
            created.Value.VirtualPath,
            created.Value.Permissions));
    }

    public async Task<Result<FileSystemItemDto>> CreateFileAsync(
        CreateFileRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = fileNamePolicy.Apply(request.Name, isDirectory: false, nameof(request.Name));
        if (!name.IsSuccess)
        {
            return name.To<FileSystemItemDto>();
        }

        var parent = await resolver.RequireAsync(request.RootId, request.Path, FilePermission.Create, cancellationToken);
        if (!parent.IsSuccess)
        {
            return parent.To<FileSystemItemDto>();
        }

        var created = await ResolveNewChildAsync(parent.Value, name.Value, cancellationToken);
        if (!created.IsSuccess)
        {
            return created.To<FileSystemItemDto>();
        }

        try
        {
            await parent.Value.Storage.CreateEmptyFileAsync(created.Value.Location, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not create file {Path}", created.Value.VirtualPath);
            return Result<FileSystemItemDto>.Error($"'{name.Value}' could not be created.");
        }

        await RecordAsync(new FileActivity(FileActivityKind.CreateFile, request.RootId, created.Value.VirtualPath), cancellationToken);

        var createdEntry = await parent.Value.Storage.GetAsync(created.Value.Location, cancellationToken);
        return Result<FileSystemItemDto>.Success(FileItems.ToItem(
            createdEntry ?? new StorageEntry(name.Value, false, 0, DateTime.UtcNow, DateTime.UtcNow, false, false),
            parent.Value.Root,
            created.Value.VirtualPath,
            created.Value.Permissions));
    }

    public async Task<Result<FileSystemItemDto>> RenameAsync(
        RenameRequest request,
        CancellationToken cancellationToken = default)
    {
        var resolved = await resolver.RequireAsync(request.RootId, request.Path, FilePermission.Rename, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.To<FileSystemItemDto>();
        }

        var target = resolved.Value;
        if (target.VirtualPath == Constants.RootPath)
        {
            return RejectRoot<FileSystemItemDto>(nameof(request.Path), "renamed");
        }

        var existing = await target.Storage.GetAsync(target.Location, cancellationToken);
        if (existing is null)
        {
            return Result<FileSystemItemDto>.NotFound($"'{target.VirtualPath}' was not found.");
        }

        var name = fileNamePolicy.Apply(request.NewName, existing.IsDirectory, nameof(request.NewName));
        if (!name.IsSuccess)
        {
            return name.To<FileSystemItemDto>();
        }

        string parentVirtual = VirtualPath.GetParent(target.VirtualPath) ?? Constants.RootPath;
        string newVirtualPath = VirtualPath.Combine(parentVirtual, name.Value);

        // The new name has to be reachable afterwards, or a rename to ".secret" or to an
        // excluded name would make the item vanish from the UI and become unmanageable.
        if (!target.Storage.TryMap(target.Root, newVirtualPath, out var destination))
        {
            return Result<FileSystemItemDto>.Invalid(new ValidationError(nameof(request.NewName), "That name can't be used here."));
        }

        // A case-only rename on Windows and macOS looks like a collision against itself.
        bool isCaseOnlyChange = target.Storage.AreSame(target.Location, destination);

        if (!isCaseOnlyChange && await target.Storage.GetAsync(destination, cancellationToken) is not null)
        {
            return Result<FileSystemItemDto>.Conflict($"'{name.Value}' already exists here.");
        }

        try
        {
            await target.Storage.MoveAsync(target.Location, destination, overwrite: false, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not rename {Path} to {NewName}", target.VirtualPath, name.Value);
            return Result<FileSystemItemDto>.Error($"'{VirtualPath.GetName(target.VirtualPath)}' could not be renamed.");
        }

        await RecordAsync(
            new FileActivity(FileActivityKind.Rename, request.RootId, target.VirtualPath, newVirtualPath, request.RootId),
            cancellationToken);

        var renamed = await target.Storage.GetAsync(destination, cancellationToken)
            ?? new StorageEntry(name.Value, existing.IsDirectory, existing.SizeBytes, existing.CreatedAt, DateTime.UtcNow, false, false);
        var permissions = await resolver.GetPermissionsAsync(target.Root, newVirtualPath, cancellationToken);

        return Result<FileSystemItemDto>.Success(FileItems.ToItem(renamed, target.Root, newVirtualPath, permissions));
    }

    public async Task<Result<BulkOperationResultDto>> DeleteAsync(
        DeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Paths.Count == 0)
        {
            return Result<BulkOperationResultDto>.Invalid(new ValidationError(nameof(request.Paths), "Select at least one item."));
        }

        var results = new List<FileOperationResultDto>(request.Paths.Count);

        foreach (string path in request.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolved = await resolver.RequireAsync(request.RootId, path, FilePermission.Delete, cancellationToken);
            if (!resolved.IsSuccess)
            {
                results.Add(new FileOperationResultDto(path, false, resolved.GetDisplayMessage()));
                continue;
            }

            var target = resolved.Value;
            if (target.VirtualPath == Constants.RootPath)
            {
                results.Add(new FileOperationResultDto(path, false, "The top-level folder can't be deleted."));
                continue;
            }

            try
            {
                var existing = await target.Storage.GetAsync(target.Location, cancellationToken);
                long? size = null;

                if (existing is { IsDirectory: true })
                {
                    await target.Storage.DeleteDirectoryAsync(target.Location, cancellationToken);
                }
                else if (existing is not null)
                {
                    size = existing.SizeBytes;
                    await target.Storage.DeleteFileAsync(target.Location, cancellationToken);
                }
                else
                {
                    results.Add(new FileOperationResultDto(target.VirtualPath, false, "No longer exists."));
                    continue;
                }

                results.Add(new FileOperationResultDto(target.VirtualPath, true, null));
                await RecordAsync(
                    new FileActivity(FileActivityKind.Delete, request.RootId, target.VirtualPath, SizeBytes: size),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Could not delete {Physical}", target.PhysicalPath);
                results.Add(new FileOperationResultDto(target.VirtualPath, false, "Could not be deleted."));
            }
        }

        return Result<BulkOperationResultDto>.Success(BulkOperationResultDto.From(results));
    }

    public async Task<Result<BulkOperationResultDto>> TransferAsync(
        TransferRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Paths.Count == 0)
        {
            return Result<BulkOperationResultDto>.Invalid(new ValidationError(nameof(request.Paths), "Select at least one item."));
        }

        // A copy only needs to read the origin; a move removes it afterwards.
        var originRequirement = request.IsCopy ? FilePermission.Read : FilePermission.Move | FilePermission.Delete;

        var destination = await resolver.RequireAsync(
            request.DestinationRootId,
            request.DestinationPath,
            request.Overwrite ? FilePermission.Create | FilePermission.Modify : FilePermission.Create,
            cancellationToken);

        if (!destination.IsSuccess)
        {
            return destination.To<BulkOperationResultDto>();
        }

        if (await destination.Value.Storage.GetAsync(destination.Value.Location, cancellationToken) is not { IsDirectory: true })
        {
            return Result<BulkOperationResultDto>.NotFound($"The destination '{destination.Value.VirtualPath}' was not found.");
        }

        var results = new List<FileOperationResultDto>(request.Paths.Count);

        foreach (string path in request.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var origin = await resolver.RequireAsync(request.RootId, path, originRequirement, cancellationToken);
            if (!origin.IsSuccess)
            {
                results.Add(new FileOperationResultDto(path, false, origin.GetDisplayMessage()));
                continue;
            }

            results.Add(await TransferOneAsync(origin.Value, destination.Value, request, cancellationToken));
        }

        return Result<BulkOperationResultDto>.Success(BulkOperationResultDto.From(results));
    }

    private async Task<FileOperationResultDto> TransferOneAsync(
        ResolvedPath origin,
        ResolvedPath destination,
        TransferRequest request,
        CancellationToken cancellationToken)
    {
        if (origin.VirtualPath == Constants.RootPath)
        {
            return new FileOperationResultDto(origin.VirtualPath, false, "The top-level folder can't be moved or copied.");
        }

        var existing = await origin.Storage.GetAsync(origin.Location, cancellationToken);
        if (existing is null)
        {
            return new FileOperationResultDto(origin.VirtualPath, false, "No longer exists.");
        }

        string name = existing.Name;

        // A blocked file that predates the extension rules may still sit in a root; it must not
        // be copyable somewhere new, such as a root that is served publicly.
        if (!existing.IsDirectory && !fileNamePolicy.Apply(name, isDirectory: false).IsSuccess)
        {
            return new FileOperationResultDto(origin.VirtualPath, false, "That type of file is not allowed here.");
        }

        string destinationVirtual = VirtualPath.Combine(destination.VirtualPath, name);
        if (!destination.Storage.TryMap(destination.Root, destinationVirtual, out var target))
        {
            return new FileOperationResultDto(origin.VirtualPath, false, "That name can't be used in the destination.");
        }

        // Copying or moving a folder into its own subtree would recurse until the disk filled.
        // Easy to trigger by dragging, so it is rejected before any bytes move.
        bool sameRoot = origin.Root.Id.Equals(destination.Root.Id, StringComparison.OrdinalIgnoreCase);
        if (existing.IsDirectory && sameRoot && origin.Storage.Contains(origin.Location, target) && !origin.Storage.AreSame(origin.Location, target))
        {
            return new FileOperationResultDto(origin.VirtualPath, false, "A folder can't be moved or copied inside itself.");
        }

        bool isSameLocation = sameRoot && origin.Storage.AreSame(origin.Location, target);
        if (isSameLocation && !request.IsCopy)
        {
            return new FileOperationResultDto(origin.VirtualPath, true, null);
        }

        // Copying onto itself duplicates under a free name, the way a desktop file manager does.
        if (isSameLocation)
        {
            name = await FindFreeNameAsync(destination, name, existing.IsDirectory, cancellationToken);
            destinationVirtual = VirtualPath.Combine(destination.VirtualPath, name);
            if (!destination.Storage.TryMap(destination.Root, destinationVirtual, out target))
            {
                return new FileOperationResultDto(origin.VirtualPath, false, "That name can't be used in the destination.");
            }
        }
        else if (await destination.Storage.GetAsync(target, cancellationToken) is not null && !request.Overwrite)
        {
            return new FileOperationResultDto(origin.VirtualPath, false, $"'{name}' already exists in the destination.");
        }

        try
        {
            await TransferBytesAsync(origin, destination.Storage, target, existing.IsDirectory, request.IsCopy, request.Overwrite, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not {Operation} {From} to {To}", request.IsCopy ? "copy" : "move", origin.VirtualPath, destinationVirtual);
            return new FileOperationResultDto(origin.VirtualPath, false, request.IsCopy ? "Could not be copied." : "Could not be moved.");
        }

        await RecordAsync(
            new FileActivity(
                request.IsCopy ? FileActivityKind.Copy : FileActivityKind.Move,
                origin.Root.Id,
                origin.VirtualPath,
                destinationVirtual,
                destination.Root.Id),
            cancellationToken);

        return new FileOperationResultDto(origin.VirtualPath, true, null);
    }

    /// <summary>
    /// Copies or moves between two locations. The same provider does it directly, including a
    /// filesystem move that falls back to copy-and-delete across drives. Different providers
    /// stream the bytes, which is how a disk folder is copied into a blob container.
    /// </summary>
    private async Task TransferBytesAsync(
        ResolvedPath origin,
        IStorageProvider destinationStorage,
        StorageLocation destination,
        bool isDirectory,
        bool isCopy,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        if (origin.Storage.Key.Equals(destinationStorage.Key, StringComparison.OrdinalIgnoreCase))
        {
            if (isCopy)
            {
                await origin.Storage.CopyAsync(origin.Location, destination, overwrite, cancellationToken);
            }
            else
            {
                await origin.Storage.MoveAsync(origin.Location, destination, overwrite, cancellationToken);
            }

            return;
        }

        await CopyAcrossProvidersAsync(origin, destinationStorage, destination, isDirectory, overwrite, cancellationToken);
        if (!isCopy)
        {
            if (isDirectory)
            {
                await origin.Storage.DeleteDirectoryAsync(origin.Location, cancellationToken);
            }
            else
            {
                await origin.Storage.DeleteFileAsync(origin.Location, cancellationToken);
            }
        }
    }

    private async Task CopyAcrossProvidersAsync(
        ResolvedPath origin,
        IStorageProvider destinationStorage,
        StorageLocation destination,
        bool isDirectory,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        if (!isDirectory)
        {
            await using var content = await origin.Storage.OpenReadAsync(origin.Location, cancellationToken);
            await destinationStorage.WriteAsync(destination, content, overwrite, cancellationToken);
            return;
        }

        await destinationStorage.CreateDirectoryAsync(destination, cancellationToken);
        await foreach (var child in origin.Storage.ListAsync(origin.Location, cancellationToken))
        {
            if (child.Name.EndsWith(Constants.TempFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!origin.Root.FollowSymlinks && child.IsSymbolicLink)
            {
                continue;
            }

            string childVirtual = VirtualPath.Combine(origin.VirtualPath, child.Name);
            string destVirtual = VirtualPath.Combine(destination.VirtualPath, child.Name);
            if (!origin.Storage.TryMap(origin.Root, childVirtual, out var childLocation)
                || !destinationStorage.TryMap(destination.Root, destVirtual, out var destChild))
            {
                continue;
            }

            var childResolved = new ResolvedPath(
                origin.Root,
                childVirtual,
                childLocation.NativePath,
                childLocation.RootNativePath,
                origin.Permissions,
                origin.Storage);

            await CopyAcrossProvidersAsync(childResolved, destinationStorage, destChild, child.IsDirectory, overwrite, cancellationToken);
        }
    }

    private async Task<string> FindFreeNameAsync(
        ResolvedPath directory,
        string name,
        bool isDirectory,
        CancellationToken cancellationToken)
    {
        if (!directory.Storage.TryMap(directory.Root, VirtualPath.Combine(directory.VirtualPath, name), out var first)
            || await directory.Storage.GetAsync(first, cancellationToken) is null)
        {
            return name;
        }

        string stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        string extension = isDirectory ? string.Empty : Path.GetExtension(name);

        for (int i = 2; i < 10_000; i++)
        {
            string candidate = $"{stem} ({i}){extension}";
            if (!directory.Storage.TryMap(directory.Root, VirtualPath.Combine(directory.VirtualPath, candidate), out var location))
            {
                continue;
            }

            if (await directory.Storage.GetAsync(location, cancellationToken) is null)
            {
                return candidate;
            }
        }

        return $"{stem} ({Guid.NewGuid():N}){extension}";
    }

    /// <summary>
    /// Resolves a not-yet-existing child of <paramref name="parent"/>, refusing names that would
    /// be hidden or excluded once created and names that are already taken.
    /// </summary>
    private async Task<Result<ResolvedPath>> ResolveNewChildAsync(
        ResolvedPath parent,
        string name,
        CancellationToken cancellationToken)
    {
        if (await parent.Storage.GetAsync(parent.Location, cancellationToken) is not { IsDirectory: true })
        {
            return Result<ResolvedPath>.NotFound($"'{parent.VirtualPath}' was not found.");
        }

        string childVirtual = VirtualPath.Combine(parent.VirtualPath, name);
        if (!parent.Storage.TryMap(parent.Root, childVirtual, out var location))
        {
            return Result<ResolvedPath>.Invalid(new ValidationError("Name", "That name can't be used here."));
        }

        if (await parent.Storage.GetAsync(location, cancellationToken) is not null)
        {
            return Result<ResolvedPath>.Conflict($"'{name}' already exists here.");
        }

        var permissions = await resolver.GetPermissionsAsync(parent.Root, childVirtual, cancellationToken);
        return Result<ResolvedPath>.Success(new ResolvedPath(
            parent.Root, childVirtual, location.NativePath, location.RootNativePath, permissions, parent.Storage));
    }
}