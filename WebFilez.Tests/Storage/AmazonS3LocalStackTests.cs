using System.Net;
using System.Net.Sockets;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using WebFilez.Services.Storage;
using WebFilez.Services.Transfers;
using WebFilez.Storage.AmazonS3;
using WebFilez.Tests.Infrastructure;

namespace WebFilez.Tests.Storage;

/// <summary>
/// Runs the file manager against LocalStack's S3 emulator. Skipped when nothing is listening,
/// so a normal test run does not require Docker.
/// </summary>
public class AmazonS3LocalStackTests : IAsyncLifetime
{
    private const string ServiceUrl = "http://127.0.0.1:4566";

    private ServiceProvider provider = null!;
    private AsyncServiceScope scope;
    private AmazonS3Client s3 = null!;
    private string uploadsBucket = null!;
    private string archiveBucket = null!;

    private IFileManagerService Service => scope.ServiceProvider.GetRequiredService<IFileManagerService>();

    private IFileTransferService Transfers => scope.ServiceProvider.GetRequiredService<IFileTransferService>();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (!await EmulatorIsListeningAsync())
        {
            return;
        }

        string suffix = Guid.NewGuid().ToString("N")[..12];
        uploadsBucket = "uploads" + suffix;
        archiveBucket = "archive" + suffix;

        s3 = new AmazonS3Client("test", "test", new AmazonS3Config
        {
            ServiceURL = ServiceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
        });
        await s3.PutBucketAsync(new PutBucketRequest { BucketName = uploadsBucket }, Token);
        await s3.PutBucketAsync(new PutBucketRequest { BucketName = archiveBucket }, Token);

        provider = new ServiceCollection()
            .AddLogging()
            .AddWebFilezServices(configure: options =>
            {
                options.Roots.Add(S3Root("uploads", "Uploads", uploadsBucket, "campaigns"));
                options.Roots.Add(S3Root("archive", "Archive", archiveBucket, path: ""));
                options.Thumbnails.CacheDirectory = Path.Combine(Path.GetTempPath(), "webfilez-s3-thumbs");
            })
            .AddWebFilezAmazonS3()
            .BuildServiceProvider(validateScopes: true);

        scope = provider.CreateAsyncScope();
    }

    public async ValueTask DisposeAsync()
    {
        if (provider is null)
        {
            s3?.Dispose();
            return;
        }

        await scope.DisposeAsync();
        await provider.DisposeAsync();
        await DeleteBucketAsync(uploadsBucket);
        await DeleteBucketAsync(archiveBucket);
        s3.Dispose();
    }

    [Fact]
    public async Task S3_root_supports_the_file_manager_operations()
    {
        await SkipWithoutEmulatorAsync();

        var empty = (await Service.ListAsync(new ListingRequest("uploads"), Token)).ShouldSucceed();
        Assert.Empty(empty.Items);

        var notes = (await Service.CreateDirectoryAsync(new CreateDirectoryRequest("uploads", "/", "Notes"), Token)).ShouldSucceed();
        Assert.Equal("/Notes", notes.Path);
        Assert.True(notes.IsDirectory);
        Assert.True(await ObjectExistsAsync(uploadsBucket, "campaigns/Notes/.folder.webfilez.tmp"));

        (await Service.CreateFileAsync(new CreateFileRequest("uploads", "/Notes", "hello.txt"), Token)).ShouldSucceed();
        var saved = (await Service.SaveTextAsync(new SaveTextFileRequest("uploads", "/Notes/hello.txt", "hello localstack"), Token)).ShouldSucceed();
        var text = (await Service.ReadTextAsync("uploads", "/Notes/hello.txt", Token)).ShouldSucceed();
        Assert.Equal("hello localstack", text.Content);
        Assert.Equal(saved.ModifiedAt, text.ModifiedAt, TimeSpan.FromSeconds(1));

        var listing = (await Service.ListAsync(new ListingRequest("uploads", "/Notes"), Token)).ShouldSucceed();
        Assert.Equal(["hello.txt"], listing.Items.Select(item => item.Name));

        var renamed = (await Service.RenameAsync(new RenameRequest("uploads", "/Notes/hello.txt", "readme.txt"), Token)).ShouldSucceed();
        Assert.Equal("/Notes/readme.txt", renamed.Path);

        var copied = (await Service.TransferAsync(
            new TransferRequest("uploads", ["/Notes/readme.txt"], "uploads", "/Notes", IsCopy: true),
            Token)).ShouldSucceed();
        Assert.Equal(1, copied.SucceededCount);
        var afterCopy = (await Service.ListAsync(new ListingRequest("uploads", "/Notes"), Token)).ShouldSucceed();
        Assert.Contains(afterCopy.Items, item => item.Name == "readme (2).txt");

        var moved = (await Service.TransferAsync(
            new TransferRequest("uploads", ["/Notes/readme.txt"], "archive", "/", IsCopy: false),
            Token)).ShouldSucceed();
        Assert.Equal(1, moved.SucceededCount);
        Assert.Equal("readme.txt", (await Service.ListAsync(new ListingRequest("archive"), Token)).ShouldSucceed().Items.Single().Name);
        Assert.DoesNotContain(
            (await Service.ListAsync(new ListingRequest("uploads", "/Notes"), Token)).ShouldSucceed().Items,
            item => item.Name == "readme.txt");

        await using var upload = new MemoryStream(Encoding.UTF8.GetBytes("from a folder drop"));
        var uploaded = (await Transfers.UploadAsync(
            new UploadRequest("uploads", "/", "caption.txt", upload, RelativeDirectory: "photos/day1"),
            Token)).ShouldSucceed();
        Assert.Equal("/photos/day1/caption.txt", uploaded.Item.Path);
        Assert.Equal("from a folder drop", (await Service.ReadTextAsync("uploads", uploaded.Item.Path, Token)).ShouldSucceed().Content);

        var archive = (await Service.CompressAsync(
            new CompressRequest("uploads", ["/photos"], "/", "photos.zip"),
            Token)).ShouldSucceed();
        Assert.Equal("/photos.zip", archive.Path);

        (await Service.CreateDirectoryAsync(new CreateDirectoryRequest("uploads", "/", "restored"), Token)).ShouldSucceed();
        var extracted = (await Service.ExtractAsync(new ExtractRequest("uploads", "/photos.zip", "/restored"), Token)).ShouldSucceed();
        Assert.True(extracted.IsCompleteSuccess, string.Join("; ", extracted.Results.Where(item => !item.Succeeded).Select(item => $"{item.Path}: {item.Error}")));
        Assert.Equal("from a folder drop", (await Service.ReadTextAsync("uploads", "/restored/photos/day1/caption.txt", Token)).ShouldSucceed().Content);

        var deleted = (await Service.DeleteAsync(new DeleteRequest("uploads", ["/Notes", "/photos", "/photos.zip", "/restored"]), Token)).ShouldSucceed();
        Assert.Equal(0, deleted.FailedCount);
        Assert.Empty((await Service.ListAsync(new ListingRequest("uploads"), Token)).ShouldSucceed().Items);

        var listed = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = uploadsBucket }, Token);
        Assert.DoesNotContain(listed.S3Objects ?? [], item => item.Key.StartsWith("campaigns/Notes", StringComparison.Ordinal));
    }

    private async Task SkipWithoutEmulatorAsync()
    {
        if (provider is not null)
        {
            return;
        }

        if (!await EmulatorIsListeningAsync())
        {
            Assert.Skip("LocalStack is not listening on 127.0.0.1:4566.");
        }

        await InitializeAsync();
    }

    private async Task<bool> ObjectExistsAsync(string bucket, string key)
    {
        try
        {
            await s3.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = bucket, Key = key }, Token);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task DeleteBucketAsync(string bucket)
    {
        string? token = null;
        do
        {
            var page = await s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucket,
                ContinuationToken = token,
            }, CancellationToken.None);

            foreach (var item in page.S3Objects ?? [])
            {
                await s3.DeleteObjectAsync(bucket, item.Key, CancellationToken.None);
            }

            token = page.IsTruncated == true ? page.NextContinuationToken : null;
        }
        while (token is not null);

        await s3.DeleteBucketAsync(bucket, CancellationToken.None);
    }

    private static async Task<bool> EmulatorIsListeningAsync()
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", 4566);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static FileRoot S3Root(string id, string name, string bucket, string path) => new()
    {
        Id = id,
        Name = name,
        Provider = StorageProviders.AmazonS3,
        Path = path,
        Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AccessKey"] = "test",
            ["SecretKey"] = "test",
            ["Region"] = "us-east-1",
            ["Bucket"] = bucket,
            ["ServiceUrl"] = ServiceUrl,
        },
    };
}
