using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Storage.v1.Data;
using Google.Cloud.Storage.V1;
using WebFilez;
using WebFilez.Services;
using WebFilez.Services.Storage;
using GcsObject = Google.Apis.Storage.v1.Data.Object;

namespace WebFilez.Storage.GoogleCloud;

/// <summary>
/// Stores a root in a Google Cloud Storage bucket. Folders are prefixes. An empty folder is a
/// zero-length hidden marker object inside that prefix, named
/// <see cref="GoogleCloudPaths.DirectoryMarkerName"/>.
/// </summary>
public sealed class GoogleCloudStorageProvider : IStorageProvider
{
    private readonly ConcurrentDictionary<string, StorageClient> clients = new(StringComparer.Ordinal);

    public string Key => StorageProviders.GoogleCloud;

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

        string prefix = GoogleCloudPaths.Prefix(root);
        string native = GoogleCloudPaths.ObjectName(prefix, virtualPath);
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
        if (location.NativePath.Length == 0 || location.NativePath == GoogleCloudPaths.Prefix(location.Root))
        {
            return DirectoryEntry(location.Root.DisplayName, DateTime.UtcNow);
        }

        var client = Client(location.Root);
        string bucket = Bucket(location.Root);

        if (await TryGetObjectAsync(client, bucket, location.NativePath, cancellationToken) is { } existing)
        {
            return FileEntry(Path.GetFileName(location.NativePath), existing);
        }

        string prefix = GoogleCloudPaths.ListingPrefix(location.NativePath);
        await foreach (var _ in ListObjectsAsync(client, bucket, prefix, delimiter: null, cancellationToken))
        {
            return DirectoryEntry(Path.GetFileName(location.NativePath.TrimEnd('/')), DateTime.UtcNow);
        }

        return null;
    }

    public async IAsyncEnumerable<StorageEntry> ListAsync(
        StorageLocation directory,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = Client(directory.Root);
        string bucket = Bucket(directory.Root);
        string prefix = GoogleCloudPaths.ListingPrefix(directory.NativePath);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var page in ListPagesAsync(client, bucket, prefix, delimiter: "/", cancellationToken))
        {
            foreach (string childPrefix in page.Prefixes ?? [])
            {
                string name = childPrefix.Length > prefix.Length ? childPrefix[prefix.Length..].TrimEnd('/') : string.Empty;
                if (name.Length > 0 && !name.Contains('/') && seen.Add(name))
                {
                    yield return DirectoryEntry(name, DateTime.UtcNow);
                }
            }

            foreach (var item in page.Items ?? [])
            {
                if (!TryChildName(prefix, item.Name, out string name) || !seen.Add(name))
                {
                    continue;
                }

                yield return new StorageEntry(
                    name,
                    false,
                    Size(item),
                    Timestamp(item, created: true),
                    Timestamp(item, created: false),
                    name.StartsWith('.'),
                    false);
            }
        }
    }

    public async Task CreateDirectoryAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        var client = Client(location.Root);
        string bucket = Bucket(location.Root);
        if (location.NativePath.Length > 0 && await TryGetObjectAsync(client, bucket, location.NativePath, cancellationToken) is not null)
        {
            throw new StorageException($"'{Path.GetFileName(location.NativePath)}' already exists.");
        }

        await using var empty = new MemoryStream();
        await UploadAsync(client, bucket, GoogleCloudPaths.DirectoryMarker(location.NativePath), empty, overwrite: true, "application/x-directory", cancellationToken);
    }

    public async Task CreateEmptyFileAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        var client = Client(location.Root);
        string bucket = Bucket(location.Root);
        if (await TryGetObjectAsync(client, bucket, location.NativePath, cancellationToken) is not null)
        {
            throw new StorageException($"'{Path.GetFileName(location.NativePath)}' already exists.");
        }

        await using var empty = new MemoryStream();
        await UploadAsync(client, bucket, location.NativePath, empty, overwrite: false, ContentType(location.NativePath), cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"webfilez-{Guid.NewGuid():N}{Constants.TempFileSuffix}");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await Client(location.Root).DownloadObjectAsync(Bucket(location.Root), location.NativePath, file, cancellationToken: cancellationToken);
            }

            return new FileStream(temporary, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose,
            });
        }
        catch (GoogleApiException ex)
        {
            TryDelete(temporary);
            throw new StorageException("That file could not be opened.", ex);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public Task WriteAsync(StorageLocation location, Stream content, bool overwrite, CancellationToken cancellationToken = default) =>
        UploadAsync(Client(location.Root), Bucket(location.Root), location.NativePath, content, overwrite, ContentType(location.NativePath), cancellationToken);

    public async Task CommitLocalFileAsync(
        StorageLocation location,
        string localFile,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        await using (var content = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
        {
            await WriteAsync(location, content, overwrite, cancellationToken);
        }

        File.Delete(localFile);
    }

    public async Task DeleteFileAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        try
        {
            await Client(location.Root).DeleteObjectAsync(Bucket(location.Root), location.NativePath, cancellationToken: cancellationToken);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
        }
        catch (GoogleApiException ex)
        {
            throw new StorageException("That file could not be deleted.", ex);
        }
    }

    public async Task DeleteDirectoryAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        var client = Client(location.Root);
        string bucket = Bucket(location.Root);
        string prefix = GoogleCloudPaths.ListingPrefix(location.NativePath);

        try
        {
            await foreach (var item in ListObjectsAsync(client, bucket, prefix, delimiter: null, cancellationToken))
            {
                await client.DeleteObjectAsync(bucket, item.Name, cancellationToken: cancellationToken);
            }
        }
        catch (GoogleApiException ex)
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
        var sourceClient = Client(source.Root);
        string sourceBucket = Bucket(source.Root);
        bool directory = await IsDirectoryAsync(sourceClient, sourceBucket, source.NativePath, cancellationToken);

        if (!directory)
        {
            await CopyObjectAsync(source, destination, overwrite, cancellationToken);
            if (deleteSource)
            {
                await sourceClient.DeleteObjectAsync(sourceBucket, source.NativePath, cancellationToken: cancellationToken);
            }

            return;
        }

        string fromPrefix = GoogleCloudPaths.ListingPrefix(source.NativePath);
        string toPrefix = GoogleCloudPaths.ListingPrefix(destination.NativePath);
        var copied = new List<string>();

        await foreach (var item in ListObjectsAsync(sourceClient, sourceBucket, fromPrefix, delimiter: null, cancellationToken))
        {
            string targetName = toPrefix + item.Name[fromPrefix.Length..];
            var target = new StorageLocation(destination.Root, destination.VirtualPath, targetName, destination.RootNativePath);
            var from = new StorageLocation(source.Root, source.VirtualPath, item.Name, source.RootNativePath);
            await CopyObjectAsync(from, target, overwrite, cancellationToken);
            copied.Add(item.Name);
        }

        if (copied.Count == 0)
        {
            await CreateDirectoryAsync(destination, cancellationToken);
        }

        if (deleteSource)
        {
            foreach (string name in copied)
            {
                await sourceClient.DeleteObjectAsync(sourceBucket, name, cancellationToken: cancellationToken);
            }
        }
    }

    private async Task CopyObjectAsync(
        StorageLocation source,
        StorageLocation destination,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        string sourceBucket = Bucket(source.Root);
        string destinationBucket = Bucket(destination.Root);
        if (ClientKey(source.Root) == ClientKey(destination.Root)
            && string.Equals(sourceBucket, destinationBucket, StringComparison.Ordinal)
            && string.Equals(source.NativePath, destination.NativePath, StringComparison.Ordinal))
        {
            return;
        }

        var destinationClient = Client(destination.Root);
        if (!overwrite && await TryGetObjectAsync(destinationClient, destinationBucket, destination.NativePath, cancellationToken) is not null)
        {
            throw new StorageException($"'{Path.GetFileName(destination.NativePath.TrimEnd('/'))}' already exists.");
        }

        if (ClientKey(source.Root) == ClientKey(destination.Root))
        {
            try
            {
                await destinationClient.CopyObjectAsync(sourceBucket, source.NativePath, destinationBucket, destination.NativePath, cancellationToken: cancellationToken);
                return;
            }
            catch (GoogleApiException)
            {
                // The emulator may not implement server-side copy. Stream the bytes instead.
            }
        }

        await using var content = await OpenReadAsync(source, cancellationToken);
        await UploadAsync(destinationClient, destinationBucket, destination.NativePath, content, overwrite, ContentType(destination.NativePath), cancellationToken);
    }

    private async Task<bool> IsDirectoryAsync(StorageClient client, string bucket, string nativePath, CancellationToken cancellationToken)
    {
        if (nativePath.Length == 0)
        {
            return true;
        }

        if (await TryGetObjectAsync(client, bucket, nativePath, cancellationToken) is not null)
        {
            return false;
        }

        await foreach (var _ in ListObjectsAsync(client, bucket, GoogleCloudPaths.ListingPrefix(nativePath), delimiter: null, cancellationToken))
        {
            return true;
        }

        return false;
    }

    private static async Task UploadAsync(
        StorageClient client,
        string bucket,
        string name,
        Stream content,
        bool overwrite,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (content.CanSeek)
        {
            await UploadSeekableAsync(client, bucket, name, content, overwrite, contentType, cancellationToken);
            return;
        }

        string temporary = Path.Combine(Path.GetTempPath(), $"webfilez-{Guid.NewGuid():N}{Constants.TempFileSuffix}");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            await using var readable = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            await UploadSeekableAsync(client, bucket, name, readable, overwrite, contentType, cancellationToken);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static async Task UploadSeekableAsync(
        StorageClient client,
        string bucket,
        string name,
        Stream content,
        bool overwrite,
        string contentType,
        CancellationToken cancellationToken)
    {
        var options = overwrite ? null : new UploadObjectOptions { IfGenerationMatch = 0 };

        try
        {
            await client.UploadObjectAsync(bucket, name, contentType, content, options, cancellationToken);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            throw new StorageException($"'{Path.GetFileName(name.TrimEnd('/'))}' already exists.", ex);
        }
        catch (GoogleApiException ex)
        {
            throw new StorageException("That file could not be written.", ex);
        }
    }

    private static async Task<GcsObject?> TryGetObjectAsync(StorageClient client, string bucket, string name, CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetObjectAsync(bucket, name, cancellationToken: cancellationToken);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (GoogleApiException ex)
        {
            throw new StorageException("That file could not be opened.", ex);
        }
    }

    private static async IAsyncEnumerable<GcsObject> ListObjectsAsync(
        StorageClient client,
        string bucket,
        string prefix,
        string? delimiter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var page in ListPagesAsync(client, bucket, prefix, delimiter, cancellationToken))
        {
            foreach (var item in page.Items ?? [])
            {
                yield return item;
            }
        }
    }

    private static async IAsyncEnumerable<Objects> ListPagesAsync(
        StorageClient client,
        string bucket,
        string prefix,
        string? delimiter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? token = null;
        do
        {
            Objects page;
            try
            {
                var request = client.Service.Objects.List(bucket);
                request.Prefix = prefix;
                request.Delimiter = delimiter;
                request.PageToken = token;
                page = await request.ExecuteAsync(cancellationToken);
            }
            catch (GoogleApiException ex)
            {
                throw new StorageException("That folder could not be listed.", ex);
            }

            yield return page;
            token = page.NextPageToken;
        }
        while (!string.IsNullOrEmpty(token));
    }

    private static bool TryChildName(string prefix, string? objectName, out string name)
    {
        name = objectName is not null && objectName.Length > prefix.Length ? objectName[prefix.Length..] : string.Empty;
        if (name.Length == 0 || name.EndsWith('/') || name.Contains('/') || name.EndsWith(Constants.TempFileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            name = string.Empty;
            return false;
        }

        return true;
    }

    private StorageClient Client(FileRoot root) => clients.GetOrAdd(ClientKey(root), _ => CreateClient(root));

    private static StorageClient CreateClient(FileRoot root)
    {
        var builder = new StorageClientBuilder();
        string? serviceUrl = Setting(root, GoogleCloudPaths.ServiceUrlKey);
        if (serviceUrl is not null)
        {
            builder.BaseUri = GoogleCloudPaths.ApiBaseUri(serviceUrl);
        }

        string? json = Setting(root, GoogleCloudPaths.CredentialJsonKey);
        string? file = Setting(root, GoogleCloudPaths.CredentialFileKey);
        if (json is not null)
        {
            builder.GoogleCredential = CredentialFactory.FromJson<ServiceAccountCredential>(json).ToGoogleCredential();
        }
        else if (file is not null)
        {
            builder.GoogleCredential = CredentialFactory.FromFile<ServiceAccountCredential>(file).ToGoogleCredential();
        }
        else if (serviceUrl is not null)
        {
            builder.UnauthenticatedAccess = true;
        }

        try
        {
            return builder.Build();
        }
        catch (Exception ex) when (ex is not StorageException)
        {
            throw new StorageException($"The '{root.DisplayName}' root could not sign in to Google Cloud Storage.", ex);
        }
    }

    private static string ClientKey(FileRoot root) =>
        string.Join('\n', Setting(root, GoogleCloudPaths.ServiceUrlKey), Setting(root, GoogleCloudPaths.CredentialFileKey), Setting(root, GoogleCloudPaths.CredentialJsonKey));

    private static string Bucket(FileRoot root) => Required(root, GoogleCloudPaths.BucketKey);

    private static string Required(FileRoot root, string key)
    {
        string? value = Setting(root, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new StorageException($"The '{root.DisplayName}' root needs Settings.{key} for Google Cloud Storage.");
        }

        return value;
    }

    private static string? Setting(FileRoot root, string key) =>
        root.Settings.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static StorageEntry DirectoryEntry(string name, DateTime modified) =>
        new(name, true, 0, modified, modified, name.StartsWith('.'), false);

    private static StorageEntry FileEntry(string name, GcsObject item) =>
        new(
            name,
            false,
            Size(item),
            Timestamp(item, created: true),
            Timestamp(item, created: false),
            name.StartsWith('.'),
            false);

    private static long Size(GcsObject item) =>
        item.Size is ulong value && value <= long.MaxValue ? (long)value : 0;

    /// <summary>
    /// fake-gcs-server sometimes returns timestamps the current client cannot parse. A missing
    /// clock is better than failing the whole read.
    /// </summary>
    private static DateTime Timestamp(GcsObject item, bool created)
    {
        try
        {
            var preferred = created ? item.TimeCreatedDateTimeOffset : item.UpdatedDateTimeOffset;
            var fallback = created ? item.UpdatedDateTimeOffset : item.TimeCreatedDateTimeOffset;
            if ((preferred ?? fallback) is { } value)
            {
                return value.UtcDateTime;
            }
        }
        catch (FormatException)
        {
        }

        return DateTime.UtcNow;
    }

    private static string ContentType(string objectName) =>
        WebFilez.FileKinds.GetMimeType(Path.GetFileName(objectName));

    private static void TryDelete(string path)
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
        }
    }
}
