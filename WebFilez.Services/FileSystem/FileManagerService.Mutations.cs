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
            Directory.CreateDirectory(created.Value.PhysicalPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not create directory {Physical}", created.Value.PhysicalPath);
            return Result<FileSystemItemDto>.Error($"'{name.Value}' could not be created.");
        }

        await RecordAsync(new FileActivity(FileActivityKind.CreateFolder, request.RootId, created.Value.VirtualPath), cancellationToken);

        return Result<FileSystemItemDto>.Success(FileItems.ToItem(
            new DirectoryInfo(created.Value.PhysicalPath),
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
            await using (new FileStream(created.Value.PhysicalPath, FileMode.CreateNew, FileAccess.Write))
            {
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not create file {Physical}", created.Value.PhysicalPath);
            return Result<FileSystemItemDto>.Error($"'{name.Value}' could not be created.");
        }

        await RecordAsync(new FileActivity(FileActivityKind.CreateFile, request.RootId, created.Value.VirtualPath), cancellationToken);

        return Result<FileSystemItemDto>.Success(FileItems.ToItem(
            new FileInfo(created.Value.PhysicalPath),
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

        bool isDirectory = Directory.Exists(target.PhysicalPath);
        if (!isDirectory && !File.Exists(target.PhysicalPath))
        {
            return Result<FileSystemItemDto>.NotFound($"'{target.VirtualPath}' was not found.");
        }

        var name = fileNamePolicy.Apply(request.NewName, isDirectory, nameof(request.NewName));
        if (!name.IsSuccess)
        {
            return name.To<FileSystemItemDto>();
        }

        string parentVirtual = VirtualPath.GetParent(target.VirtualPath) ?? Constants.RootPath;
        string newVirtualPath = VirtualPath.Combine(parentVirtual, name.Value);

        // The new name has to be reachable afterwards, or a rename to ".secret" or to an
        // excluded name would make the item vanish from the UI and become unmanageable.
        if (!PathResolver.TryMapToDisk(target.Root, newVirtualPath, out string destination, out _))
        {
            return Result<FileSystemItemDto>.Invalid(new ValidationError(nameof(request.NewName), "That name can't be used here."));
        }

        // A case-only rename on Windows and macOS looks like a collision against itself.
        bool isCaseOnlyChange = string.Equals(destination, target.PhysicalPath, StringComparison.OrdinalIgnoreCase);

        if (!isCaseOnlyChange && (Directory.Exists(destination) || File.Exists(destination)))
        {
            return Result<FileSystemItemDto>.Conflict($"'{name.Value}' already exists here.");
        }

        try
        {
            if (isDirectory)
            {
                Directory.Move(target.PhysicalPath, destination);
            }
            else
            {
                File.Move(target.PhysicalPath, destination, overwrite: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not rename {Physical} to {NewName}", target.PhysicalPath, name.Value);
            return Result<FileSystemItemDto>.Error($"'{VirtualPath.GetName(target.VirtualPath)}' could not be renamed.");
        }

        await RecordAsync(
            new FileActivity(FileActivityKind.Rename, request.RootId, target.VirtualPath, newVirtualPath, request.RootId),
            cancellationToken);

        FileSystemInfo renamed = isDirectory ? new DirectoryInfo(destination) : new FileInfo(destination);
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
                long? size = null;

                if (Directory.Exists(target.PhysicalPath))
                {
                    // Recursive delete removes links rather than following them, so a link
                    // inside the folder can't take files outside the root with it.
                    Directory.Delete(target.PhysicalPath, recursive: true);
                }
                else if (File.Exists(target.PhysicalPath))
                {
                    size = new FileInfo(target.PhysicalPath).Length;
                    File.Delete(target.PhysicalPath);
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

        if (!Directory.Exists(destination.Value.PhysicalPath))
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

        bool isDirectory = Directory.Exists(origin.PhysicalPath);
        if (!isDirectory && !File.Exists(origin.PhysicalPath))
        {
            return new FileOperationResultDto(origin.VirtualPath, false, "No longer exists.");
        }

        string name = Path.GetFileName(origin.PhysicalPath);

        // A blocked file that predates the extension rules may still sit in a root; it must not
        // be copyable somewhere new, such as a root that is served publicly.
        if (!isDirectory && !fileNamePolicy.Apply(name, isDirectory: false).IsSuccess)
        {
            return new FileOperationResultDto(origin.VirtualPath, false, "That type of file is not allowed here.");
        }

        string finalPath = Path.Combine(destination.PhysicalPath, name);

        // Copying or moving a folder into its own subtree would recurse until the disk filled.
        // Easy to trigger by dragging, so it is rejected before any bytes move.
        if (isDirectory && PhysicalPaths.IsWithin(finalPath, origin.PhysicalPath)
            && !PhysicalPaths.AreEqual(finalPath, origin.PhysicalPath))
        {
            return new FileOperationResultDto(origin.VirtualPath, false, "A folder can't be moved or copied inside itself.");
        }

        bool isSameLocation = PhysicalPaths.AreEqual(origin.PhysicalPath, finalPath);
        if (isSameLocation && !request.IsCopy)
        {
            return new FileOperationResultDto(origin.VirtualPath, true, null);
        }

        // Copying onto itself duplicates under a free name, the way a desktop file manager does.
        if (isSameLocation)
        {
            name = PhysicalPaths.FindFreeName(destination.PhysicalPath, name, isDirectory);
            finalPath = Path.Combine(destination.PhysicalPath, name);
        }
        else if ((Directory.Exists(finalPath) || File.Exists(finalPath)) && !request.Overwrite)
        {
            return new FileOperationResultDto(origin.VirtualPath, false, $"'{name}' already exists in the destination.");
        }

        string destinationVirtual = VirtualPath.Combine(destination.VirtualPath, name);
        if (!PathResolver.TryMapToDisk(destination.Root, destinationVirtual, out _, out _))
        {
            return new FileOperationResultDto(origin.VirtualPath, false, "That name can't be used in the destination.");
        }

        try
        {
            if (request.IsCopy)
            {
                if (isDirectory)
                {
                    FileItems.CopyDirectory(origin.PhysicalPath, finalPath, request.Overwrite, origin.Root.FollowSymlinks);
                }
                else
                {
                    File.Copy(origin.PhysicalPath, finalPath, request.Overwrite);
                }
            }
            else
            {
                MoveAcrossVolumes(origin, finalPath, isDirectory, request.Overwrite);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not {Operation} {From} to {To}", request.IsCopy ? "copy" : "move", origin.PhysicalPath, finalPath);
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
    /// <see cref="File.Move(string, string, bool)"/> and <see cref="Directory.Move"/> fail across
    /// volumes, which is normal when two roots are on different drives. Falls back to
    /// copy-then-delete so the move still succeeds.
    /// </summary>
    private static void MoveAcrossVolumes(ResolvedPath origin, string to, bool isDirectory, bool overwrite)
    {
        string from = origin.PhysicalPath;

        try
        {
            if (isDirectory)
            {
                if (overwrite && Directory.Exists(to))
                {
                    Directory.Delete(to, recursive: true);
                }

                Directory.Move(from, to);
            }
            else
            {
                File.Move(from, to, overwrite);
            }

            return;
        }
        catch (IOException) when (!Directory.Exists(to) && !File.Exists(to))
        {
            // Fall through to copy-and-delete.
        }

        if (isDirectory)
        {
            FileItems.CopyDirectory(from, to, overwrite, origin.Root.FollowSymlinks);
            Directory.Delete(from, recursive: true);
        }
        else
        {
            File.Copy(from, to, overwrite);
            File.Delete(from);
        }
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
        if (!Directory.Exists(parent.PhysicalPath))
        {
            return Result<ResolvedPath>.NotFound($"'{parent.VirtualPath}' was not found.");
        }

        string childVirtual = VirtualPath.Combine(parent.VirtualPath, name);
        if (!PathResolver.TryMapToDisk(parent.Root, childVirtual, out string physical, out string rootPhysical))
        {
            return Result<ResolvedPath>.Invalid(new ValidationError("Name", "That name can't be used here."));
        }

        if (Directory.Exists(physical) || File.Exists(physical))
        {
            return Result<ResolvedPath>.Conflict($"'{name}' already exists here.");
        }

        var permissions = await resolver.GetPermissionsAsync(parent.Root, childVirtual, cancellationToken);
        return Result<ResolvedPath>.Success(new ResolvedPath(parent.Root, childVirtual, physical, rootPhysical, permissions));
    }
}
