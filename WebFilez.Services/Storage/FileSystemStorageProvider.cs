using System.Runtime.CompilerServices;

namespace WebFilez.Services.Storage;

/// <summary>
/// Stores a root in a directory on disk. This is the default provider: a root with no
/// <see cref="FileRoot.Provider"/> uses it.
/// </summary>
public sealed class FileSystemStorageProvider : IStorageProvider
{
    /// <summary>Value of <see cref="FileRoot.Provider"/> that selects this provider. An empty value does too.</summary>
    public const string ProviderKey = "filesystem";

    /// <summary>True when <paramref name="provider"/> is empty or <see cref="ProviderKey"/>.</summary>
    public static bool IsFileSystem(string? provider) =>
        string.IsNullOrWhiteSpace(provider) || provider.Equals(ProviderKey, StringComparison.OrdinalIgnoreCase);

    public string Key => ProviderKey;

    public StringComparison NameComparison => PhysicalPaths.Comparison;

    public bool TryMap(FileRoot root, string virtualPath, out StorageLocation location)
    {
        if (!PathResolver.TryMapToDisk(root, virtualPath, out string physical, out string rootPhysical))
        {
            location = null!;
            return false;
        }

        location = new StorageLocation(root, virtualPath, physical, rootPhysical);
        return true;
    }

    public bool AreSame(StorageLocation left, StorageLocation right) =>
        PhysicalPaths.AreEqual(left.NativePath, right.NativePath);

    public bool Contains(StorageLocation container, StorageLocation candidate) =>
        PhysicalPaths.IsWithin(candidate.NativePath, container.NativePath);

    public ValueTask<StorageEntry?> GetAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Directory.Exists(location.NativePath)
            ? ValueTask.FromResult<StorageEntry?>(ToEntry(new DirectoryInfo(location.NativePath)))
            : File.Exists(location.NativePath)
            ? ValueTask.FromResult<StorageEntry?>(ToEntry(new FileInfo(location.NativePath)))
            : ValueTask.FromResult<StorageEntry?>(null);
    }

    public async IAsyncEnumerable<StorageEntry> ListAsync(
        StorageLocation directory,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var entry in new DirectoryInfo(directory.NativePath).EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return ToEntry(entry);
        }

        await Task.CompletedTask;
    }

    public Task CreateDirectoryAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(location.NativePath);
        return Task.CompletedTask;
    }

    public async Task CreateEmptyFileAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(location.NativePath)!);
        await using var stream = new FileStream(location.NativePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.FlushAsync(cancellationToken);
    }

    public Task<Stream> OpenReadAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(FileItems.OpenSequentialRead(location.NativePath));
    }

    public async Task WriteAsync(
        StorageLocation location,
        Stream content,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        // Written beside the destination and swapped in, so a failure leaves the previous file intact.
        string temporary = location.NativePath + Constants.TempFileSuffix;
        Directory.CreateDirectory(Path.GetDirectoryName(location.NativePath)!);

        try
        {
            await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            File.Move(temporary, location.NativePath, overwrite);
        }
        catch
        {
            TryDeleteFile(temporary);
            throw;
        }
    }

    public Task CommitLocalFileAsync(
        StorageLocation location,
        string localFile,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(location.NativePath)!);

        try
        {
            File.Move(localFile, location.NativePath, overwrite);
        }
        catch (IOException) when (!File.Exists(location.NativePath) || overwrite)
        {
            File.Copy(localFile, location.NativePath, overwrite);
            File.Delete(localFile);
        }

        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(location.NativePath);
        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Recursive delete removes links rather than following them, so a link inside the folder
        // can't take files outside the root with it.
        Directory.Delete(location.NativePath, recursive: true);
        return Task.CompletedTask;
    }

    public async Task MoveAsync(
        StorageLocation source,
        StorageLocation destination,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        var entry = await GetAsync(source, cancellationToken)
            ?? throw new StorageException("That item no longer exists.");

        try
        {
            if (entry.IsDirectory)
            {
                if (overwrite && Directory.Exists(destination.NativePath))
                {
                    Directory.Delete(destination.NativePath, recursive: true);
                }

                Directory.Move(source.NativePath, destination.NativePath);
            }
            else
            {
                File.Move(source.NativePath, destination.NativePath, overwrite);
            }
        }
        catch (IOException) when (!Directory.Exists(destination.NativePath) && !File.Exists(destination.NativePath))
        {
            // Move fails across volumes. Copy, then delete the source.
            await CopyAsync(source, destination, overwrite, cancellationToken);
            if (entry.IsDirectory)
            {
                Directory.Delete(source.NativePath, recursive: true);
            }
            else
            {
                File.Delete(source.NativePath);
            }
        }
    }

    public async Task CopyAsync(
        StorageLocation source,
        StorageLocation destination,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        var entry = await GetAsync(source, cancellationToken)
            ?? throw new StorageException("That item no longer exists.");

        if (entry.IsDirectory)
        {
            CopyDirectory(source.NativePath, destination.NativePath, overwrite, source.Root.FollowSymlinks);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination.NativePath)!);
        File.Copy(source.NativePath, destination.NativePath, overwrite);
    }

    private static void CopyDirectory(string from, string to, bool overwrite, bool followSymlinks)
    {
        Directory.CreateDirectory(to);

        foreach (var entry in new DirectoryInfo(from).EnumerateFileSystemInfos())
        {
            // A link would pull in whatever it points at, which may be outside the root.
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

    private static StorageEntry ToEntry(FileSystemInfo entry)
    {
        bool isDirectory = entry is DirectoryInfo;
        return new StorageEntry(
            entry.Name,
            isDirectory,
            entry is FileInfo file ? file.Length : 0,
            entry.CreationTimeUtc,
            entry.LastWriteTimeUtc,
            entry.Name.StartsWith('.') || entry.Attributes.HasFlag(FileAttributes.Hidden),
            entry.LinkTarget is not null);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // The original failure is the one that matters.
        }
    }
}