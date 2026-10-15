using System.Runtime.CompilerServices;

namespace WebFilez.Services.FileSystem;

/// <summary>Builds DTOs from disk entries and decides which entries a listing shows.</summary>
internal static class FileItems
{
    public static FileSystemItemDto ToItem(
        FileSystemInfo entry,
        FileRoot root,
        string virtualPath,
        FilePermission permissions)
    {
        bool isDirectory = entry is DirectoryInfo;
        long size = entry is FileInfo file ? file.Length : 0;
        var kind = FileKinds.Classify(entry.Name, isDirectory);

        return new FileSystemItemDto(
            entry.Name,
            virtualPath,
            isDirectory,
            size,
            entry.LastWriteTimeUtc,
            kind,
            isDirectory ? null : entry.Extension,
            IsHidden(entry),
            permissions,
            HasThumbnail: !isDirectory && FileKinds.SupportsThumbnail(entry.Name) && permissions.Has(FilePermission.Read),
            PublicUrl: BuildPublicUrl(root, virtualPath, isDirectory));
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

    public static bool IsHidden(FileSystemInfo entry) =>
        entry.Name.StartsWith('.') || entry.Attributes.HasFlag(FileAttributes.Hidden);

    /// <summary>
    /// Whether an entry may appear at all. Mirrors the checks in
    /// <see cref="PathResolver.TryMapToDisk"/>, so nothing listed is unreachable and nothing
    /// reachable is unlisted.
    /// </summary>
    public static bool IsVisible(FileSystemInfo entry, FileRoot root, bool showHidden, ExcludeMatcher excludes)
    {
        if (entry.Name.EndsWith(Constants.TempFileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!root.FollowSymlinks && entry.LinkTarget is not null)
        {
            return false;
        }

        if (IsHidden(entry) && !(root.ShowHiddenFiles && showHidden))
        {
            return false;
        }

        return !excludes.IsExcluded(entry.Name);
    }

    /// <summary>
    /// Every visible file under a folder that the caller holds <paramref name="required"/> on.
    /// Excluded and unbrowsable folders are pruned rather than descended, so a zip of a folder
    /// can't be used to get at files hidden or denied inside it.
    /// </summary>
    public static async IAsyncEnumerable<(FileInfo File, string VirtualPath)> EnumerateFilesAsync(
        PathResolver resolver,
        ResolvedPath directory,
        FilePermission required,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var root = directory.Root;
        var excludes = ExcludeMatcher.For(root);
        var pending = new Stack<(DirectoryInfo Directory, string VirtualPath)>();
        pending.Push((new DirectoryInfo(directory.PhysicalPath), directory.VirtualPath));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (current, currentVirtual) = pending.Pop();

            FileSystemInfo[] entries;
            try
            {
                entries = current.GetFileSystemInfos();
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

                string childVirtual = VirtualPath.Combine(currentVirtual, entry.Name);
                var permissions = await resolver.GetPermissionsAsync(root, childVirtual, cancellationToken);

                if (entry is DirectoryInfo childDirectory)
                {
                    if (permissions.HasAny(FilePermission.Browse | FilePermission.Read))
                    {
                        pending.Push((childDirectory, childVirtual));
                    }
                }
                else if (entry is FileInfo file && permissions.Has(required))
                {
                    yield return (file, childVirtual);
                }
            }
        }
    }

    /// <summary>
    /// Copies a folder tree. Links are skipped unless the root follows them, otherwise copying a
    /// folder that contains a link would pull in whatever the link points at.
    /// </summary>
    public static void CopyDirectory(string from, string to, bool overwrite, bool followSymlinks)
    {
        Directory.CreateDirectory(to);

        foreach (var entry in new DirectoryInfo(from).EnumerateFileSystemInfos())
        {
            if (!followSymlinks && entry.LinkTarget is not null)
            {
                continue;
            }

            if (entry.Name.EndsWith(Constants.TempFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string target = Path.Combine(to, entry.Name);
            if (entry is DirectoryInfo)
            {
                CopyDirectory(entry.FullName, target, overwrite, followSymlinks);
            }
            else
            {
                File.Copy(entry.FullName, target, overwrite);
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
    public static async Task<bool> LooksBinaryAsync(string physicalPath, CancellationToken cancellationToken)
    {
        const int SampleSize = 8 * 1024;

        await using var stream = OpenSequentialRead(physicalPath);
        byte[] buffer = new byte[SampleSize];
        int read = await stream.ReadAsync(buffer.AsMemory(0, SampleSize), cancellationToken);

        return buffer.AsSpan(0, read).Contains((byte)0);
    }

    /// <summary>
    /// Opaque validator over size and modification time. Changes whenever the file does, which
    /// is all a conditional GET needs.
    /// </summary>
    public static string BuildETag(FileInfo info) =>
        $"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}";
}
