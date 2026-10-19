using WebFilez.Services.Storage;

namespace WebFilez.Services.FileSystem;

/// <summary>A virtual path that has been mapped onto the disk and permission-checked.</summary>
public sealed record ResolvedPath(
    FileRoot Root,
    string VirtualPath,
    string PhysicalPath,
    string RootPhysicalPath,
    FilePermission Permissions,
    IStorageProvider Storage)
{
    public StorageLocation Location => new(Root, VirtualPath, PhysicalPath, RootPhysicalPath);
}

/// <summary>
/// The single gateway from an untrusted virtual path to a physical one. Every service method
/// goes through <see cref="RequireAsync"/> before touching the disk.
/// </summary>
public sealed class PathResolver(
    IFileRootProvider rootProvider,
    IFileAccessPolicy accessPolicy,
    IStorageProviderSelector storage,
    FileScope scope)
{
    /// <summary>The folder this caller cannot leave. Empty unless a file manager set a base path.</summary>
    public FileScope Scope => scope;
    public Task<FileRoot?> FindRootAsync(string? rootId, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(rootId)
            ? Task.FromResult<FileRoot?>(null)
            : rootProvider.FindAsync(rootId, cancellationToken);

    /// <summary>
    /// Resolves <paramref name="path"/> inside a root and checks the caller holds every flag in
    /// <paramref name="required"/>. A path the caller can neither browse nor read is reported as
    /// not found rather than forbidden, so the error doesn't confirm that it exists.
    /// </summary>
    public async Task<Result<ResolvedPath>> RequireAsync(
        string? rootId,
        string? path,
        FilePermission required,
        CancellationToken cancellationToken = default)
    {
        var root = await FindRootAsync(rootId, cancellationToken);
        return root is null
            ? Result<ResolvedPath>.NotFound("That folder is not available.")
            : await RequireAsync(root, path, required, cancellationToken);
    }

    public async Task<Result<ResolvedPath>> RequireAsync(
        FileRoot root,
        string? path,
        FilePermission required,
        CancellationToken cancellationToken = default)
    {
        string virtualPath = VirtualPath.Normalize(path);
        string notFound = $"'{virtualPath}' was not found.";

        // Same answer as a missing folder, so the refusal doesn't confirm that a parent exists.
        if (!scope.Allows(root.Id, virtualPath))
        {
            return Result<ResolvedPath>.NotFound(notFound);
        }

        IStorageProvider provider;
        try
        {
            provider = storage.Get(root);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ResolvedPath>.Error(ex.Message);
        }

        if (!provider.TryMap(root, virtualPath, out var location))
        {
            return Result<ResolvedPath>.NotFound(notFound);
        }

        var permissions = await GetPermissionsAsync(root, virtualPath, cancellationToken);

        return !permissions.HasAny(FilePermission.Browse | FilePermission.Read)
            ? Result<ResolvedPath>.NotFound(notFound)
            : !permissions.Has(required)
            ? Result<ResolvedPath>.Forbidden($"You do not have permission to {Describe(required)} here.")
            : Result<ResolvedPath>.Success(
            new ResolvedPath(root, virtualPath, location.NativePath, location.RootNativePath, permissions, provider));
    }

    /// <summary>The caller's permissions at a path, already clamped to what the root allows.</summary>
    public async ValueTask<FilePermission> GetPermissionsAsync(
        FileRoot root,
        string virtualPath,
        CancellationToken cancellationToken = default)
    {
        if (!scope.Allows(root.Id, virtualPath))
        {
            return FilePermission.None;
        }

        return await accessPolicy.GetPermissionsAsync(root, virtualPath, cancellationToken) & root.EffectivePermissions;
    }

    /// <summary>
    /// Maps a normalized virtual path to a physical path under the root, refusing anything a
    /// listing would hide: excluded or hidden segments, in-progress temp files, characters the
    /// OS would interpret (such as NTFS alternate data streams), and links when the root does
    /// not follow them. Names a listing never shows can't be reached by typing a URL either.
    /// </summary>
    internal static bool TryMapToDisk(
        FileRoot root,
        string virtualPath,
        out string physicalPath,
        out string rootPhysicalPath)
    {
        rootPhysicalPath = Path.GetFullPath(root.Path);
        physicalPath = rootPhysicalPath;

        string[] segments = VirtualPath.GetSegments(virtualPath);

        foreach (string segment in segments)
        {
            if (StorageNames.IsRejected(root, segment))
            {
                return false;
            }
        }

        string candidate = Path.GetFullPath(Path.Combine([rootPhysicalPath, .. segments]));
        if (!PhysicalPaths.IsWithin(candidate, rootPhysicalPath))
        {
            return false;
        }

        // Walk the existing part of the path. A link anywhere along it could lead outside the
        // root; a hidden-attribute folder along it would otherwise be reachable by URL.
        string current = rootPhysicalPath;
        foreach (string segment in segments)
        {
            current = Path.Combine(current, segment);

            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (!info.Exists)
            {
                break;
            }

            if ((!root.FollowSymlinks && info.LinkTarget is not null)
                || (!root.ShowHiddenFiles && info.Attributes.HasFlag(FileAttributes.Hidden)))
            {
                return false;
            }
        }

        physicalPath = candidate;
        return true;
    }

    private static string Describe(FilePermission required) => required switch
    {
        _ when required.HasFlag(FilePermission.Delete) && required.HasFlag(FilePermission.Move) => "move items",
        _ when required.HasFlag(FilePermission.Extract) => "extract archives",
        _ when required.HasFlag(FilePermission.Compress) => "create archives",
        _ when required.HasFlag(FilePermission.Modify) => "change files",
        _ when required.HasFlag(FilePermission.Create) => "add files",
        _ when required.HasFlag(FilePermission.Rename) => "rename items",
        _ when required.HasFlag(FilePermission.Delete) => "delete items",
        _ when required.HasFlag(FilePermission.Download) => "download files",
        _ when required.HasFlag(FilePermission.Read) => "open files",
        _ => "do that",
    };
}