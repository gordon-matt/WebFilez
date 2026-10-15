using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using Amazon.S3;
using Amazon.S3.Model;
using WebFilez.Services;
using WebFilez.Services.Storage;

namespace WebFilez.Storage.AmazonS3;

/// <summary>
/// Stores a root in an Amazon S3 bucket. Folders are prefixes. An empty folder is a zero-length
/// hidden marker object inside that prefix, named <see cref="AmazonS3Paths.DirectoryMarkerName"/>.
/// </summary>
public sealed class AmazonS3StorageProvider : IStorageProvider
{
    /// <summary>Value of <see cref="FileRoot.Provider"/> that selects this provider.</summary>
    public const string ProviderKey = "amazon-s3";

    private readonly ConcurrentDictionary<string, IAmazonS3> clients = new(StringComparer.Ordinal);

    public string Key => ProviderKey;

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

        string prefix = AmazonS3Paths.Prefix(root);
        string native = AmazonS3Paths.ObjectKey(prefix, virtualPath);
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
        if (location.NativePath.Length == 0 || location.NativePath == AmazonS3Paths.Prefix(location.Root))
        {
            return DirectoryEntry(location.Root.DisplayName, DateTime.UtcNow);
        }

        var client = Client(location.Root);
        string bucket = Bucket(location.Root);

        if (await TryGetMetadataAsync(client, bucket, location.NativePath, cancellationToken) is { } metadata)
        {
            return FileEntry(Path.GetFileName(location.NativePath), metadata);
        }

        string prefix = AmazonS3Paths.ListingPrefix(location.NativePath);
        await foreach (var _ in ListKeysAsync(client, bucket, prefix, cancellationToken))
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
        string prefix = AmazonS3Paths.ListingPrefix(directory.NativePath);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;

        do
        {
            ListObjectsV2Response response;
            try
            {
                response = await client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = bucket,
                    Prefix = prefix,
                    Delimiter = "/",
                    ContinuationToken = token,
                }, cancellationToken);
            }
            catch (AmazonS3Exception ex)
            {
                throw new StorageException("That folder could not be listed.", ex);
            }

            foreach (string childPrefix in response.CommonPrefixes ?? [])
            {
                string name = childPrefix.Length > prefix.Length ? childPrefix[prefix.Length..].TrimEnd('/') : string.Empty;
                if (name.Length > 0 && !name.Contains('/') && seen.Add(name))
                {
                    yield return DirectoryEntry(name, DateTime.UtcNow);
                }
            }

            foreach (var item in response.S3Objects ?? [])
            {
                if (!TryChildName(prefix, item.Key, out string name) || !seen.Add(name))
                {
                    continue;
                }

                yield return new StorageEntry(
                    name,
                    false,
                    item.Size ?? 0,
                    Utc(item.LastModified),
                    Utc(item.LastModified),
                    name.StartsWith('.'),
                    false);
            }

            token = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (token is not null);
    }

    public async Task CreateDirectoryAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        var client = Client(location.Root);
        string bucket = Bucket(location.Root);
        if (location.NativePath.Length > 0 && await TryGetMetadataAsync(client, bucket, location.NativePath, cancellationToken) is not null)
        {
            throw new StorageException($"'{Path.GetFileName(location.NativePath)}' already exists.");
        }

        await using var empty = new MemoryStream();
        await PutAsync(client, bucket, AmazonS3Paths.DirectoryMarker(location.NativePath), empty, "application/x-directory", cancellationToken);
    }

    public async Task CreateEmptyFileAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        var client = Client(location.Root);
        string bucket = Bucket(location.Root);
        if (await TryGetMetadataAsync(client, bucket, location.NativePath, cancellationToken) is not null)
        {
            throw new StorageException($"'{Path.GetFileName(location.NativePath)}' already exists.");
        }

        await using var empty = new MemoryStream();
        await PutAsync(client, bucket, location.NativePath, empty, ContentType(location.NativePath), cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client(location.Root).GetObjectAsync(new GetObjectRequest
            {
                BucketName = Bucket(location.Root),
                Key = location.NativePath,
            }, cancellationToken);

            return await CopyToTempAsync(response.ResponseStream, cancellationToken);
        }
        catch (AmazonS3Exception ex)
        {
            throw new StorageException("That file could not be opened.", ex);
        }
    }

    public Task WriteAsync(StorageLocation location, Stream content, bool overwrite, CancellationToken cancellationToken = default) =>
        WriteCoreAsync(location, content, overwrite, cancellationToken);

    public async Task CommitLocalFileAsync(
        StorageLocation location,
        string localFile,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        await using (var content = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
        {
            await WriteCoreAsync(location, content, overwrite, cancellationToken);
        }

        File.Delete(localFile);
    }

    public async Task DeleteFileAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        try
        {
            await Client(location.Root).DeleteObjectAsync(Bucket(location.Root), location.NativePath, cancellationToken);
        }
        catch (AmazonS3Exception ex)
        {
            throw new StorageException("That file could not be deleted.", ex);
        }
    }

    public async Task DeleteDirectoryAsync(StorageLocation location, CancellationToken cancellationToken = default)
    {
        var client = Client(location.Root);
        string bucket = Bucket(location.Root);
        string prefix = AmazonS3Paths.ListingPrefix(location.NativePath);

        try
        {
            await foreach (string key in ListKeysAsync(client, bucket, prefix, cancellationToken))
            {
                await client.DeleteObjectAsync(bucket, key, cancellationToken);
            }
        }
        catch (AmazonS3Exception ex)
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
                await sourceClient.DeleteObjectAsync(sourceBucket, source.NativePath, cancellationToken);
            }

            return;
        }

        string fromPrefix = AmazonS3Paths.ListingPrefix(source.NativePath);
        string toPrefix = AmazonS3Paths.ListingPrefix(destination.NativePath);
        var copied = new List<string>();

        await foreach (string key in ListKeysAsync(sourceClient, sourceBucket, fromPrefix, cancellationToken))
        {
            string targetKey = toPrefix + key[fromPrefix.Length..];
            var target = new StorageLocation(destination.Root, destination.VirtualPath, targetKey, destination.RootNativePath);
            var from = new StorageLocation(source.Root, source.VirtualPath, key, source.RootNativePath);
            await CopyObjectAsync(from, target, overwrite, cancellationToken);
            copied.Add(key);
        }

        if (copied.Count == 0)
        {
            await CreateDirectoryAsync(destination, cancellationToken);
        }

        if (deleteSource)
        {
            foreach (string key in copied)
            {
                await sourceClient.DeleteObjectAsync(sourceBucket, key, cancellationToken);
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
        if (!overwrite && await TryGetMetadataAsync(destinationClient, destinationBucket, destination.NativePath, cancellationToken) is not null)
        {
            throw new StorageException($"'{Path.GetFileName(destination.NativePath.TrimEnd('/'))}' already exists.");
        }

        if (ClientKey(source.Root) == ClientKey(destination.Root))
        {
            try
            {
                await destinationClient.CopyObjectAsync(new CopyObjectRequest
                {
                    SourceBucket = sourceBucket,
                    SourceKey = source.NativePath,
                    DestinationBucket = destinationBucket,
                    DestinationKey = destination.NativePath,
                }, cancellationToken);
                return;
            }
            catch (AmazonS3Exception)
            {
                // LocalStack and some gateways reject server-side copy. Stream the bytes instead.
            }
        }

        await using var content = await OpenReadAsync(source, cancellationToken);
        await WriteCoreAsync(destination, content, overwrite, cancellationToken);
    }

    private async Task<bool> IsDirectoryAsync(IAmazonS3 client, string bucket, string nativePath, CancellationToken cancellationToken)
    {
        if (nativePath.Length == 0)
        {
            return true;
        }

        if (await TryGetMetadataAsync(client, bucket, nativePath, cancellationToken) is not null)
        {
            return false;
        }

        await foreach (var _ in ListKeysAsync(client, bucket, AmazonS3Paths.ListingPrefix(nativePath), cancellationToken))
        {
            return true;
        }

        return false;
    }

    private async Task WriteCoreAsync(
        StorageLocation location,
        Stream content,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var client = Client(location.Root);
        string bucket = Bucket(location.Root);
        if (!overwrite && await TryGetMetadataAsync(client, bucket, location.NativePath, cancellationToken) is not null)
        {
            throw new StorageException($"'{Path.GetFileName(location.NativePath)}' already exists.");
        }

        await PutAsync(client, bucket, location.NativePath, content, ContentType(location.NativePath), cancellationToken);
    }

    private static async Task PutAsync(
        IAmazonS3 client,
        string bucket,
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (content.CanSeek)
        {
            await PutSeekableAsync(client, bucket, key, content, contentType, cancellationToken);
            return;
        }

        // S3 needs a known length. Archive entry streams are not seekable, so spool them first.
        string temporary = Path.Combine(Path.GetTempPath(), $"webfilez-{Guid.NewGuid():N}{Constants.TempFileSuffix}");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            await using var readable = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            await PutSeekableAsync(client, bucket, key, readable, contentType, cancellationToken);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static async Task PutSeekableAsync(
        IAmazonS3 client,
        string bucket,
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = content,
                ContentType = contentType,
                AutoCloseStream = false,
                AutoResetStreamPosition = true,
            }, cancellationToken);
        }
        catch (AmazonS3Exception ex)
        {
            throw new StorageException("That file could not be written.", ex);
        }
    }

    private static async Task<GetObjectMetadataResponse?> TryGetMetadataAsync(
        IAmazonS3 client,
        string bucket,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = bucket,
                Key = key,
            }, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (AmazonS3Exception ex)
        {
            throw new StorageException("That file could not be opened.", ex);
        }
    }

    private static async IAsyncEnumerable<string> ListKeysAsync(
        IAmazonS3 client,
        string bucket,
        string prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? token = null;
        do
        {
            ListObjectsV2Response response;
            try
            {
                response = await client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = bucket,
                    Prefix = prefix,
                    ContinuationToken = token,
                }, cancellationToken);
            }
            catch (AmazonS3Exception ex)
            {
                throw new StorageException("That folder could not be listed.", ex);
            }

            foreach (var item in response.S3Objects ?? [])
            {
                yield return item.Key;
            }

            token = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (token is not null);
    }

    private static bool TryChildName(string prefix, string key, out string name)
    {
        name = key.Length > prefix.Length ? key[prefix.Length..] : string.Empty;
        if (name.Length == 0 || name.EndsWith('/') || name.Contains('/') || name.EndsWith(Constants.TempFileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            name = string.Empty;
            return false;
        }

        return true;
    }

    private static async Task<Stream> CopyToTempAsync(Stream source, CancellationToken cancellationToken)
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"webfilez-{Guid.NewGuid():N}{Constants.TempFileSuffix}");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await source.CopyToAsync(file, cancellationToken);
            }

            return new FileStream(temporary, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose,
            });
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private IAmazonS3 Client(FileRoot root) => clients.GetOrAdd(ClientKey(root), _ => CreateClient(root));

    private static IAmazonS3 CreateClient(FileRoot root)
    {
        string region = Setting(root, AmazonS3Paths.RegionKey) ?? "us-east-1";
        string? serviceUrl = Setting(root, AmazonS3Paths.ServiceUrlKey);
        var config = new AmazonS3Config
        {
            AuthenticationRegion = region,
            ForcePathStyle = !string.IsNullOrWhiteSpace(serviceUrl),
        };

        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            config.ServiceURL = serviceUrl;
        }
        else
        {
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region);
        }

        return new AmazonS3Client(Required(root, AmazonS3Paths.AccessKeyKey), Required(root, AmazonS3Paths.SecretKeyKey), config);
    }

    private static string ClientKey(FileRoot root) =>
        string.Join('\n', Setting(root, AmazonS3Paths.ServiceUrlKey), Setting(root, AmazonS3Paths.RegionKey), Required(root, AmazonS3Paths.AccessKeyKey), Required(root, AmazonS3Paths.SecretKeyKey));

    private static string Bucket(FileRoot root) => Required(root, AmazonS3Paths.BucketKey);

    private static string Required(FileRoot root, string key)
    {
        string? value = Setting(root, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new StorageException($"The '{root.DisplayName}' root needs Settings.{key} for Amazon S3.");
        }

        return value;
    }

    private static string? Setting(FileRoot root, string key) =>
        root.Settings.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static StorageEntry DirectoryEntry(string name, DateTime modified) =>
        new(name, true, 0, modified, modified, name.StartsWith('.'), false);

    private static StorageEntry FileEntry(string name, GetObjectMetadataResponse metadata) =>
        new(name, false, metadata.ContentLength, Utc(metadata.LastModified), Utc(metadata.LastModified), name.StartsWith('.'), false);

    private static DateTime Utc(DateTime? value) => value is { } date
        ? date.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : date.ToUniversalTime()
        : DateTime.UtcNow;

    private static string ContentType(string objectKey) =>
        WebFilez.FileKinds.GetMimeType(Path.GetFileName(objectKey));

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