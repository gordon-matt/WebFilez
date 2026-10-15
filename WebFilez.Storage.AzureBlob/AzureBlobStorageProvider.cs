using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using WebFilez.Services;
using WebFilez.Services.Storage;

namespace WebFilez.Storage.AzureBlob;

/// <summary>
/// Stores a root in an Azure Blob container. Folders are prefixes. An empty folder is a
/// zero-length blob whose name ends in <c>/</c>, which is what makes it show up before anything
/// is put inside it.
/// </summary>
public sealed class AzureBlobStorageProvider : IStorageProvider
{
    private readonly ConcurrentDictionary<string, BlobServiceClient> clients = new(StringComparer.Ordinal);

    public string Key => StorageProviders.AzureBlob;

    public StringComparison NameComparison => StringComparison.Ordinal;

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

        string prefix = AzureBlobPaths.Prefix(root);
        string native = AzureBlobPaths.BlobName(prefix, virtualPath);
        location = new StorageLocation(root, virtualPath, native, prefix);
        return true;
    }

    public bool AreSame(StorageLocation left, StorageLocation right) =>
        string.Equals(left.NativePath.TrimEnd('/'), right.NativePath.TrimEnd('/'), StringComparison.Ordinal);

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
        return prefix.Length == 0 || path.StartsWith(prefix + "/", StringComparison.Ordinal);
    }

    public async ValueTask<StorageEntry?> GetAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        if (location.NativePath.Length == 0 || location.NativePath == AzureBlobPaths.Prefix(location.Root))
        {
            return DirectoryEntry(location.Root.DisplayName, DateTime.UtcNow);
        }

        var container = Container(location.Root);
        var file = container.GetBlobClient(location.NativePath);
        if (await file.ExistsAsync(cancellationToken))
        {
            var properties = await file.GetPropertiesAsync(cancellationToken: cancellationToken);
            return FileEntry(Path.GetFileName(location.NativePath), properties.Value);
        }

        string markerName = AzureBlobPaths.ListingPrefix(location.NativePath);
        var marker = container.GetBlobClient(markerName);
        if (await marker.ExistsAsync(cancellationToken))
        {
            var properties = await marker.GetPropertiesAsync(cancellationToken: cancellationToken);
            return DirectoryEntry(Path.GetFileName(location.NativePath.TrimEnd('/')), properties.Value.LastModified.UtcDateTime);
        }

        await foreach (var _ in container.GetBlobsAsync(prefix: markerName, cancellationToken: cancellationToken))
        {
            return DirectoryEntry(Path.GetFileName(location.NativePath.TrimEnd('/')), DateTime.UtcNow);
        }

        return null;
    }

    public async IAsyncEnumerable<StorageEntry> ListAsync(
        StorageLocation directory,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var container = Container(directory.Root);
        string prefix = AzureBlobPaths.ListingPrefix(directory.NativePath);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var item in container.GetBlobsByHierarchyAsync(delimiter: "/", prefix: prefix, cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.IsPrefix && item.Prefix is { Length: > 0 } childPrefix)
            {
                string name = childPrefix[prefix.Length..].TrimEnd('/');
                if (name.Length > 0 && seen.Add(name))
                {
                    yield return DirectoryEntry(name, DateTime.UtcNow);
                }
            }
            else if (item.Blob is { } blob)
            {
                string rest = blob.Name[prefix.Length..];
                if (rest.Length == 0)
                {
                    continue;
                }

                if (rest.EndsWith('/'))
                {
                    string name = rest.TrimEnd('/');
                    if (name.Length > 0 && !name.Contains('/') && seen.Add(name))
                    {
                        yield return DirectoryEntry(name, blob.Properties.LastModified?.UtcDateTime ?? DateTime.UtcNow);
                    }

                    continue;
                }

                if (!rest.Contains('/') && seen.Add(rest))
                {
                    yield return new StorageEntry(
                        rest,
                        false,
                        blob.Properties.ContentLength ?? 0,
                        blob.Properties.CreatedOn?.UtcDateTime ?? blob.Properties.LastModified?.UtcDateTime ?? DateTime.UtcNow,
                        blob.Properties.LastModified?.UtcDateTime ?? DateTime.UtcNow,
                        rest.StartsWith('.'),
                        false);
                }
            }
        }
    }

    public Task CreateDirectoryAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        string name = AzureBlobPaths.ListingPrefix(location.NativePath);
        var blob = Container(location.Root).GetBlobClient(name);
        return UploadAsync(blob, Stream.Null, overwrite: true, "application/x-directory", cancellationToken);
    }

    public async Task CreateEmptyFileAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        var blob = Container(location.Root).GetBlobClient(location.NativePath);
        if (await blob.ExistsAsync(cancellationToken))
        {
            throw new StorageException($"'{Path.GetFileName(location.NativePath)}' already exists.");
        }

        await using var empty = new MemoryStream();
        await UploadAsync(blob, empty, overwrite: false, ContentType(location.NativePath), cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Container(location.Root).GetBlobClient(location.NativePath).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            throw new StorageException("That file could not be opened.", ex);
        }
    }

    public Task WriteAsync(StorageLocation location, Stream content, bool overwrite, CancellationToken cancellationToken = default) =>
        UploadAsync(
            Container(location.Root).GetBlobClient(location.NativePath),
            content,
            overwrite,
            ContentType(location.NativePath),
            cancellationToken);

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
            await Container(location.Root).DeleteBlobIfExistsAsync(location.NativePath, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            throw new StorageException("That file could not be deleted.", ex);
        }
    }

    public async Task DeleteDirectoryAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        var container = Container(location.Root);
        string prefix = AzureBlobPaths.ListingPrefix(location.NativePath);

        try
        {
            await foreach (var blob in container.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
            {
                await container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
            }
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
        var sourceContainer = Container(source.Root);
        var destinationContainer = Container(destination.Root);
        bool directory = await IsDirectoryAsync(sourceContainer, source.NativePath, cancellationToken);

        if (!directory)
        {
            await CopyBlobAsync(sourceContainer, source.NativePath, destinationContainer, destination.NativePath, overwrite, cancellationToken);
            if (deleteSource)
            {
                await sourceContainer.DeleteBlobIfExistsAsync(source.NativePath, cancellationToken: cancellationToken);
            }

            return;
        }

        string fromPrefix = AzureBlobPaths.ListingPrefix(source.NativePath);
        string toPrefix = AzureBlobPaths.ListingPrefix(destination.NativePath);
        var copied = new List<string>();

        await foreach (var blob in sourceContainer.GetBlobsAsync(prefix: fromPrefix, cancellationToken: cancellationToken))
        {
            string targetName = toPrefix + blob.Name[fromPrefix.Length..];
            await CopyBlobAsync(sourceContainer, blob.Name, destinationContainer, targetName, overwrite, cancellationToken);
            copied.Add(blob.Name);
        }

        if (copied.Count == 0)
        {
            await CreateDirectoryAsync(destination, cancellationToken);
        }

        if (deleteSource)
        {
            foreach (string name in copied)
            {
                await sourceContainer.DeleteBlobIfExistsAsync(name, cancellationToken: cancellationToken);
            }
        }
    }

    private async Task CopyBlobAsync(
        BlobContainerClient source,
        string sourceName,
        BlobContainerClient destination,
        string destinationName,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var target = destination.GetBlobClient(destinationName);
        if (!overwrite && await target.ExistsAsync(cancellationToken))
        {
            throw new StorageException($"'{Path.GetFileName(destinationName.TrimEnd('/'))}' already exists.");
        }

        try
        {
            var operation = await target.StartCopyFromUriAsync(source.GetBlobClient(sourceName).Uri, cancellationToken: cancellationToken);
            await operation.WaitForCompletionAsync(cancellationToken);
        }
        catch (RequestFailedException)
        {
            await using var content = await source.GetBlobClient(sourceName).OpenReadAsync(cancellationToken: cancellationToken);
            await UploadAsync(target, content, overwrite, ContentType(destinationName), cancellationToken);
        }
    }

    private static async Task<bool> IsDirectoryAsync(BlobContainerClient container, string nativePath, CancellationToken cancellationToken)
    {
        if (nativePath.Length == 0)
        {
            return true;
        }

        if (await container.GetBlobClient(nativePath).ExistsAsync(cancellationToken))
        {
            return false;
        }

        string prefix = AzureBlobPaths.ListingPrefix(nativePath);
        await foreach (var _ in container.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            return true;
        }

        return await container.GetBlobClient(prefix).ExistsAsync(cancellationToken);
    }

    private static async Task UploadAsync(
        BlobClient blob,
        Stream content,
        bool overwrite,
        string contentType,
        CancellationToken cancellationToken)
    {
        try
        {
            await blob.UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Conditions = overwrite ? null : new BlobRequestConditions { IfNoneMatch = ETag.All },
            }, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            throw new StorageException($"'{Path.GetFileName(blob.Name.TrimEnd('/'))}' already exists.", ex);
        }
        catch (RequestFailedException ex)
        {
            throw new StorageException("That file could not be written.", ex);
        }
    }

    private BlobContainerClient Container(FileRoot root)
    {
        if (!root.Settings.TryGetValue(AzureBlobPaths.ConnectionStringKey, out string? connection) || string.IsNullOrWhiteSpace(connection))
        {
            throw new StorageException($"The '{root.DisplayName}' root needs a Settings.ConnectionString for Azure Blob Storage.");
        }

        if (!root.Settings.TryGetValue(AzureBlobPaths.ContainerKey, out string? container) || string.IsNullOrWhiteSpace(container))
        {
            throw new StorageException($"The '{root.DisplayName}' root needs a Settings.Container for Azure Blob Storage.");
        }

        var service = clients.GetOrAdd(connection, static value => new BlobServiceClient(value));
        return service.GetBlobContainerClient(container);
    }

    private static StorageEntry DirectoryEntry(string name, DateTime modified) =>
        new(name, true, 0, modified, modified, name.StartsWith('.'), false);

    private static StorageEntry FileEntry(string name, BlobProperties properties) =>
        new(
            name,
            false,
            properties.ContentLength,
            properties.CreatedOn.UtcDateTime,
            properties.LastModified.UtcDateTime,
            name.StartsWith('.'),
            false);

    private static string ContentType(string blobName) =>
        blobName.EndsWith('/') ? "application/x-directory" : FileKinds.GetMimeType(Path.GetFileName(blobName));
}