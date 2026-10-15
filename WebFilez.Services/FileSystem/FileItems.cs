using System.Runtime.CompilerServices;
using WebFilez.Services.Storage;

namespace WebFilez.Services.FileSystem;

/// <summary>Builds DTOs from disk entries and decides which entries a listing shows.</summary>
internal static class FileItems
{
    public static FileSystemItemDto ToItem(
        StorageEntry entry,
        FileRoot root,
        string virtualPath,
        FilePermission permissions)
    {
        var kind = FileKinds.Classify(entry.Name, entry.IsDirectory);

        return new FileSystemItemDto(
            entry.Name,
            virtualPath,
            entry.IsDirectory,
            entry.SizeBytes,
            entry.ModifiedAt,
            kind,
            entry.IsDirectory ? null : Path.GetExtension(entry.Name),
            entry.IsHidden,
            permissions,
            HasThumbnail: !entry.IsDirectory && FileKinds.SupportsThumbnail(entry.Name) && permissions.Has(FilePermission.Read),
            PublicUrl: BuildPublicUrl(root, virtualPath, entry.IsDirectory));
    }

    /// <summary>
    /// Public address of a file or folder. Folder addresses end with a slash, including the
    /// root itself, so a host can tell a directory from a file without a second lookup.
    /// </summary>
    public static string? BuildPublicUrl(FileRoot root, string virtualPath, bool directory = false)
    {
        if (string.IsNullOrWhiteSpace(root.PublicUrl))
        {
            return null;
        }

        string escaped = string.Concat(VirtualPath.GetSegments(virtualPath)
            .Select(s => Constants.PathSeparator + Uri.EscapeDataString(s)));

        string url = root.PublicUrl.TrimEnd(Constants.PathSeparator) + escaped;
        return directory ? url + Constants.PathSeparator : url;
    }

    /// <summary>
    /// Whether an entry may appear at all. Mirrors the checks in
    /// <see cref="PathResolver.TryMapToDisk"/>, so nothing listed is unreachable and nothing
    /// reachable is unlisted.
    /// </summary>
    public static bool IsVisible(StorageEntry entry, FileRoot root, bool showHidden, ExcludeMatcher excludes) => !entry.Name.EndsWith(Constants.TempFileSuffix, StringComparison.OrdinalIgnoreCase) && (root.FollowSymlinks || !entry.IsSymbolicLink) && (!entry.IsHidden || (root.ShowHiddenFiles && showHidden)) && !excludes.IsExcluded(entry.Name);

    /// <summary>
    /// Every visible file under a folder that the caller holds <paramref name="required"/> on.
    /// Excluded and unbrowsable folders are pruned rather than descended, so a zip of a folder
    /// can't be used to get at files hidden or denied inside it.
    /// </summary>
    public static async IAsyncEnumerable<(StorageEntry Entry, string VirtualPath)> EnumerateFilesAsync(
        PathResolver resolver,
        ResolvedPath directory,
        FilePermission required,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var root = directory.Root;
        var excludes = ExcludeMatcher.For(root);
        var pending = new Stack<ResolvedPath>();
        pending.Push(directory);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            IReadOnlyList<StorageEntry> entries;
            try
            {
                var listed = new List<StorageEntry>();
                await foreach (var entry in current.Storage.ListAsync(current.Location, cancellationToken))
                {
                    listed.Add(entry);
                }

                entries = listed;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (!IsVisible(entry, root, showHidden: true, excludes))
                {
                    continue;
                }

                string childVirtual = VirtualPath.Combine(current.VirtualPath, entry.Name);
                var permissions = await resolver.GetPermissionsAsync(root, childVirtual, cancellationToken);

                if (!current.Storage.TryMap(root, childVirtual, out var child))
                {
                    continue;
                }

                if (entry.IsDirectory)
                {
                    if (permissions.HasAny(FilePermission.Browse | FilePermission.Read))
                    {
                        pending.Push(new ResolvedPath(
                            root, childVirtual, child.NativePath, child.RootNativePath, permissions, current.Storage));
                    }
                }
                else if (permissions.Has(required))
                {
                    yield return (entry, childVirtual);
                }
            }
        }
    }

    public static FileStream OpenSequentialRead(string physicalPath) => new(
        physicalPath,
        new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });

    /// <summary>
    /// Treats a file as binary if the first 8 KB contain a NUL byte. The same heuristic git
    /// uses, and reliable enough to keep the editor away from executables and media.
    /// </summary>
    public static async Task<bool> LooksBinaryAsync(Stream stream, CancellationToken cancellationToken)
    {
        const int SampleSize = 8 * 1024;

        byte[] buffer = new byte[SampleSize];
        int read = await stream.ReadAsync(buffer.AsMemory(0, SampleSize), cancellationToken);

        return buffer.AsSpan(0, read).Contains((byte)0);
    }

    /// <summary>
    /// Opaque validator over size and modification time. Changes whenever the file does, which
    /// is all a conditional GET needs.
    /// </summary>
    public static string BuildETag(long sizeBytes, DateTime modifiedAtUtc) =>
        $"{sizeBytes:x}-{modifiedAtUtc.Ticks:x}";
}