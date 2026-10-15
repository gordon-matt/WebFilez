using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Azure;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using WebFilez.Services;
using WebFilez.Services.Storage;

namespace WebFilez.Storage.AzureFiles;

/// <summary>
/// Stores a root in an Azure file share. Directories are real: creating one makes an empty folder,
/// and deleting one removes its children first because Azure refuses to delete a directory that
/// still has anything in it. The share itself is never created or deleted.
/// </summary>
public sealed class AzureFileStorageProvider : IStorageProvider
{
    private readonly ConcurrentDictionary<string, ShareServiceClient> clients = new(StringComparer.Ordinal);

    public string Key => StorageProviders.AzureFile;

    public StringComparison NameComparison => StringComparison.OrdinalIgnoreCase;

    public bool TryMap(FileRoot root, string virtualPath, out StorageLocation location)
    {
        foreach (string segment in VirtualPath.GetSegments(virtualPath))
        {
            if (StorageNames.IsRejected(root, segment))
            {
                location = null!;
                return false;
            }
        }

        string prefix = AzureFilePaths.Prefix(root);
        string native = AzureFilePaths.FilePath(prefix, virtualPath);
        location = new StorageLocation(root, virtualPath, native, prefix);
        return true;
    }

    public bool AreSame(StorageLocation left, StorageLocation right) =>
        string.Equals(left.NativePath.TrimEnd('/'), right.NativePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    public bool Contains(StorageLocation container, StorageLocation candidate)
    {
        if (!string.Equals(container.Root.Id, candidate.Root.Id, StringComparison.Ordinal))
        {
            return false;
        }

        if (AreSame(container, candidate))
        {
            return true;
        }

        string prefix = container.NativePath.TrimEnd('/');
        string path = candidate.NativePath.TrimEnd('/');
        return prefix.Length == 0 || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask<StorageEntry?> GetAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        var share = Share(location.Root);
        string path = location.NativePath.Trim('/');

        try
        {
            if (path.Length == 0)
            {
                var root = share.GetRootDirectoryClient();
                return await root.ExistsAsync(cancellationToken)
                    ? DirectoryEntry(location.Root.DisplayName, DateTime.UtcNow)
                    : null;
            }

            var file = FileClient(share, path);
            if (await file.ExistsAsync(cancellationToken))
            {
                var properties = await file.GetPropertiesAsync(cancellationToken: cancellationToken);
                return FileEntry(AzureFilePaths.Name(path), properties.Value);
            }

            var directory = DirectoryClient(share, path);
            if (!await directory.ExistsAsync(cancellationToken))
            {
                return null;
            }

            var directoryProperties = await directory.GetPropertiesAsync(cancellationToken: cancellationToken);
            string name = path == AzureFilePaths.Prefix(location.Root)
                ? location.Root.DisplayName
                : AzureFilePaths.Name(path);
            return DirectoryEntry(name, Timestamp(directoryProperties.Value.SmbProperties, directoryProperties.Value.LastModified));
        }
        catch (RequestFailedException ex)
        {
            throw new StorageException("That item could not be read.", ex);
        }
    }

    public async IAsyncEnumerable<StorageEntry> ListAsync(
        StorageLocation directory,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var share = Share(directory.Root);
        var folder = DirectoryClient(share, directory.NativePath.Trim('/'));
        var options = new ShareDirectoryGetFilesAndDirectoriesOptions
        {
            Traits = ShareFileTraits.Timestamps | ShareFileTraits.Attributes,
        };

        await foreach (var item in folder.GetFilesAndDirectoriesAsync(options, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool hidden = item.Name.StartsWith('.')
                || (item.FileAttributes is NtfsFileAttributes attributes && attributes.HasFlag(NtfsFileAttributes.Hidden));
            var created = item.Properties?.CreatedOn?.UtcDateTime
                ?? item.Properties?.LastWrittenOn?.UtcDateTime
                ?? DateTime.UtcNow;
            var modified = item.Properties?.LastWrittenOn?.UtcDateTime ?? created;

            yield return item.IsDirectory
                ? new StorageEntry(item.Name, true, 0, created, modified, hidden, false)
                : new StorageEntry(item.Name, false, item.FileSize ?? 0, created, modified, hidden, false);
        }
    }

    public async Task CreateDirectoryAsync(StorageLocation location, CancellationToken cancellationToken = default) => await EnsureDirectoryAsync(Share(location.Root), location.NativePath.Trim('/'), cancellationToken);

    public async Task CreateEmptyFileAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        string path = location.NativePath.Trim('/');
        var share = Share(location.Root);
        await EnsureDirectoryAsync(share, AzureFilePaths.Parent(path), cancellationToken);

        var file = FileClient(share, path);
        if (await file.ExistsAsync(cancellationToken) || await DirectoryClient(share, path).ExistsAsync(cancellationToken))
        {
            throw new StorageException($"'{AzureFilePaths.Name(path)}' already exists.");
        }

        try
        {
            await file.CreateAsync(0, new ShareFileCreateOptions
            {
                HttpHeaders = new ShareFileHttpHeaders { ContentType = ContentType(path) },
            }, conditions: null, cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            throw new StorageException("That file could not be created.", ex);
        }
    }

    public async Task<Stream> OpenReadAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        try
        {
            return await FileClient(Share(location.Root), location.NativePath.Trim('/')).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            throw new StorageException("That file could not be opened.", ex);
        }
    }

    public Task WriteAsync(StorageLocation location, Stream content, bool overwrite, CancellationToken cancellationToken = default) =>
        UploadAsync(Share(location.Root), location.NativePath.Trim('/'), content, overwrite, cancellationToken);

    public async Task CommitLocalFileAsync(
        StorageLocation location,
        string localFile,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        await using var content = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        await WriteAsync(location, content, overwrite, cancellationToken);
        File.Delete(localFile);
    }

    public async Task DeleteFileAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        try
        {
            await FileClient(Share(location.Root), location.NativePath.Trim('/')).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            throw new StorageException("That file could not be deleted.", ex);
        }
    }

    public async Task DeleteDirectoryAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        string path = location.NativePath.Trim('/');
        if (path.Length == 0)
        {
            throw new StorageException("The file share itself cannot be deleted.");
        }

        try
        {
            await DeleteTreeAsync(DirectoryClient(Share(location.Root), path), deleteRoot: true, cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            throw new StorageException("That folder could not be deleted.", ex);
        }
    }

    public Task MoveAsync(StorageLocation source, StorageLocation destination, bool overwrite, CancellationToken cancellationToken = default) =>
        TransferAsync(source, destination, overwrite, deleteSource: true, cancellationToken);

    public Task CopyAsync(StorageLocation source, StorageLocation destination, bool overwrite, CancellationToken cancellationToken = default) =>
        TransferAsync(source, destination, overwrite, deleteSource: false, cancellationToken);

    private async Task TransferAsync(
        StorageLocation source,
        StorageLocation destination,
        bool overwrite,
        bool deleteSource,
        CancellationToken cancellationToken)
    {
        var sourceShare = Share(source.Root);
        var destinationShare = Share(destination.Root);
        string from = source.NativePath.Trim('/');
        string to = destination.NativePath.Trim('/');
        bool directory = await IsDirectoryAsync(sourceShare, from, cancellationToken);

        if (SameShare(source.Root, destination.Root) && deleteSource)
        {
            await RenameAsync(sourceShare, from, to, directory, overwrite, cancellationToken);
            return;
        }

        if (directory)
        {
            await CopyDirectoryAsync(sourceShare, from, destinationShare, to, overwrite, cancellationToken);
            if (deleteSource && from.Length > 0)
            {
                await DeleteTreeAsync(DirectoryClient(sourceShare, from), deleteRoot: true, cancellationToken);
            }
        }
        else
        {
            await CopyFileAsync(sourceShare, from, destinationShare, to, overwrite, cancellationToken);
            if (deleteSource)
            {
                await FileClient(sourceShare, from).DeleteIfExistsAsync(cancellationToken: cancellationToken);
            }
        }
    }

    private async Task RenameAsync(
        ShareClient share,
        string from,
        string to,
        bool directory,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        await EnsureDirectoryAsync(share, AzureFilePaths.Parent(to), cancellationToken);

        try
        {
            if (directory)
            {
                var destination = DirectoryClient(share, to);
                if (await destination.ExistsAsync(cancellationToken))
                {
                    if (!overwrite)
                    {
                        throw new StorageException($"'{AzureFilePaths.Name(to)}' already exists.");
                    }

                    await DeleteTreeAsync(destination, deleteRoot: true, cancellationToken);
                }

                await DirectoryClient(share, from).RenameAsync(to, cancellationToken: cancellationToken);
                return;
            }

            if (!overwrite && await FileClient(share, to).ExistsAsync(cancellationToken))
            {
                throw new StorageException($"'{AzureFilePaths.Name(to)}' already exists.");
            }

            await FileClient(share, from).RenameAsync(
                to,
                new ShareFileRenameOptions { ReplaceIfExists = overwrite },
                cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            throw new StorageException("That item could not be moved.", ex);
        }
    }

    private async Task CopyDirectoryAsync(
        ShareClient sourceShare,
        string from,
        ShareClient destinationShare,
        string to,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        await EnsureDirectoryAsync(destinationShare, to, cancellationToken);

        await foreach (var item in DirectoryClient(sourceShare, from).GetFilesAndDirectoriesAsync(cancellationToken: cancellationToken))
        {
            string sourcePath = from.Length == 0 ? item.Name : $"{from}/{item.Name}";
            string destinationPath = to.Length == 0 ? item.Name : $"{to}/{item.Name}";
            if (item.IsDirectory)
            {
                await CopyDirectoryAsync(sourceShare, sourcePath, destinationShare, destinationPath, overwrite, cancellationToken);
            }
            else
            {
                await CopyFileAsync(sourceShare, sourcePath, destinationShare, destinationPath, overwrite, cancellationToken);
            }
        }
    }

    private async Task CopyFileAsync(
        ShareClient sourceShare,
        string from,
        ShareClient destinationShare,
        string to,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var destination = FileClient(destinationShare, to);
        if (await destination.ExistsAsync(cancellationToken))
        {
            if (!overwrite)
            {
                throw new StorageException($"'{AzureFilePaths.Name(to)}' already exists.");
            }

            await destination.DeleteAsync(cancellationToken: cancellationToken);
        }

        await EnsureDirectoryAsync(destinationShare, AzureFilePaths.Parent(to), cancellationToken);

        try
        {
            var copy = await destination.StartCopyAsync(FileClient(sourceShare, from).Uri, cancellationToken: cancellationToken);
            await WaitForCopyAsync(destination, copy.Value.CopyStatus, cancellationToken);
        }
        catch (RequestFailedException)
        {
            await using var content = await FileClient(sourceShare, from).OpenReadAsync(cancellationToken: cancellationToken);
            await UploadAsync(destinationShare, to, content, overwrite: true, cancellationToken);
        }
    }

    private static async Task WaitForCopyAsync(ShareFileClient destination, CopyStatus status, CancellationToken cancellationToken)
    {
        while (status == CopyStatus.Pending)
        {
            await Task.Delay(200, cancellationToken);
            var properties = await destination.GetPropertiesAsync(cancellationToken: cancellationToken);
            status = properties.Value.CopyStatus;
        }

        if (status != CopyStatus.Success)
        {
            throw new StorageException($"The copy finished with status {status}.");
        }
    }

    private async Task UploadAsync(ShareClient share, string path, Stream content, bool overwrite, CancellationToken cancellationToken)
    {
        await EnsureDirectoryAsync(share, AzureFilePaths.Parent(path), cancellationToken);
        var file = FileClient(share, path);

        if (await file.ExistsAsync(cancellationToken))
        {
            if (!overwrite)
            {
                throw new StorageException($"'{AzureFilePaths.Name(path)}' already exists.");
            }

            await file.DeleteAsync(cancellationToken: cancellationToken);
        }

        Stream? owned = null;
        string? temporary = null;
        try
        {
            if (!content.CanSeek)
            {
                temporary = Path.Combine(Path.GetTempPath(), $"webfilez-{Guid.NewGuid():N}.upload");
                await using (var local = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await content.CopyToAsync(local, cancellationToken);
                }

                content = owned = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read);
            }

            await file.UploadAsync(content, cancellationToken: cancellationToken);
            await file.SetHttpHeadersAsync(
                newSize: null,
                httpHeaders: new ShareFileHttpHeaders { ContentType = ContentType(path) },
                smbProperties: null,
                filePermission: null,
                cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            throw new StorageException($"'{AzureFilePaths.Name(path)}' already exists.", ex);
        }
        catch (RequestFailedException ex)
        {
            throw new StorageException("That file could not be written.", ex);
        }
        finally
        {
            if (owned is not null)
            {
                await owned.DisposeAsync();
            }

            if (temporary is not null)
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task<bool> IsDirectoryAsync(ShareClient share, string path, CancellationToken cancellationToken) => path.Length == 0 || (await DirectoryClient(share, path).ExistsAsync(cancellationToken)
            ? true
            : await FileClient(share, path).ExistsAsync(cancellationToken)
            ? false
            : throw new StorageException($"'{AzureFilePaths.Name(path)}' was not found."));

    private static async Task DeleteTreeAsync(ShareDirectoryClient directory, bool deleteRoot, CancellationToken cancellationToken)
    {
        await foreach (var item in directory.GetFilesAndDirectoriesAsync(cancellationToken: cancellationToken))
        {
            if (item.IsDirectory)
            {
                await DeleteTreeAsync(directory.GetSubdirectoryClient(item.Name), deleteRoot: true, cancellationToken);
            }
            else
            {
                await directory.GetFileClient(item.Name).DeleteIfExistsAsync(cancellationToken: cancellationToken);
            }
        }

        if (deleteRoot && directory.Path.Length > 0)
        {
            await directory.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
    }

    private static async Task EnsureDirectoryAsync(ShareClient share, string path, CancellationToken cancellationToken)
    {
        if (path.Length == 0)
        {
            return;
        }

        var current = share.GetRootDirectoryClient();
        foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.GetSubdirectoryClient(segment);
            try
            {
                await current.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            }
            catch (RequestFailedException ex)
            {
                throw new StorageException("That folder could not be created.", ex);
            }
        }
    }

    private ShareClient Share(FileRoot root)
    {
        if (!root.Settings.TryGetValue(AzureFilePaths.ConnectionStringKey, out string? connection) || string.IsNullOrWhiteSpace(connection))
        {
            throw new StorageException($"The '{root.DisplayName}' root needs a Settings.ConnectionString for Azure Files.");
        }

        if (!root.Settings.TryGetValue(AzureFilePaths.ShareKey, out string? share) || string.IsNullOrWhiteSpace(share))
        {
            throw new StorageException($"The '{root.DisplayName}' root needs a Settings.Share for Azure Files.");
        }

        var service = clients.GetOrAdd(connection, static value => new ShareServiceClient(value));
        return service.GetShareClient(share);
    }

    private static bool SameShare(FileRoot left, FileRoot right) =>
        string.Equals(Setting(left, AzureFilePaths.ConnectionStringKey), Setting(right, AzureFilePaths.ConnectionStringKey), StringComparison.Ordinal)
        && string.Equals(Setting(left, AzureFilePaths.ShareKey), Setting(right, AzureFilePaths.ShareKey), StringComparison.OrdinalIgnoreCase);

    private static string Setting(FileRoot root, string key) =>
        root.Settings.TryGetValue(key, out string? value) ? value : string.Empty;

    private static ShareDirectoryClient DirectoryClient(ShareClient share, string path) =>
        path.Length == 0 ? share.GetRootDirectoryClient() : share.GetDirectoryClient(path);

    private static ShareFileClient FileClient(ShareClient share, string path)
    {
        string parent = AzureFilePaths.Parent(path);
        var directory = parent.Length == 0 ? share.GetRootDirectoryClient() : share.GetDirectoryClient(parent);
        return directory.GetFileClient(AzureFilePaths.Name(path));
    }

    private static StorageEntry DirectoryEntry(string name, DateTime modified) =>
        new(name, true, 0, modified, modified, name.StartsWith('.'), false);

    private static StorageEntry FileEntry(string name, ShareFileProperties properties)
    {
        var modified = Timestamp(properties.SmbProperties, properties.LastModified);
        var created = properties.SmbProperties.FileCreatedOn?.UtcDateTime ?? modified;
        bool hidden = name.StartsWith('.')
            || (properties.SmbProperties.FileAttributes is NtfsFileAttributes attributes && attributes.HasFlag(NtfsFileAttributes.Hidden));
        return new StorageEntry(name, false, properties.ContentLength, created, modified, hidden, false);
    }

    private static DateTime Timestamp(FileSmbProperties smb, DateTimeOffset lastModified) =>
        smb.FileLastWrittenOn?.UtcDateTime ?? lastModified.UtcDateTime;

    private static string ContentType(string path) => FileKinds.GetMimeType(AzureFilePaths.Name(path));
}