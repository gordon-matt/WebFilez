using System.Net;
using System.Net.Sockets;
using System.Text;
using Google;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.DependencyInjection;
using WebFilez.Services.Storage;
using WebFilez.Services.Transfers;
using WebFilez.Storage.GoogleCloud;
using WebFilez.Tests.Infrastructure;

namespace WebFilez.Tests.Storage;

/// <summary>
/// Runs the file manager against fake-gcs-server. Skipped when nothing is listening,
/// so a normal test run does not require Docker.
/// </summary>
public class GoogleCloudFakeGcsTests : IAsyncLifetime
{
    private const string ServiceUrl = "http://127.0.0.1:4443";

    private ServiceProvider provider = null!;
    private AsyncServiceScope scope;
    private StorageClient storage = null!;
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

        storage = new StorageClientBuilder
        {
            BaseUri = GoogleCloudPaths.ApiBaseUri(ServiceUrl),
            UnauthenticatedAccess = true,
        }.Build();
        await storage.CreateBucketAsync("webfilez", uploadsBucket, cancellationToken: Token);
        await storage.CreateBucketAsync("webfilez", archiveBucket, cancellationToken: Token);

        provider = new ServiceCollection()
            .AddLogging()
            .AddWebFilezServices(configure: options =>
            {
                options.Roots.Add(CloudRoot("uploads", "Uploads", uploadsBucket, "campaigns"));
                options.Roots.Add(CloudRoot("archive", "Archive", archiveBucket, path: ""));
                options.Thumbnails.CacheDirectory = Path.Combine(Path.GetTempPath(), "webfilez-gcs-thumbs");
            })
            .AddWebFilezGoogleCloud()
            .BuildServiceProvider(validateScopes: true);

        scope = provider.CreateAsyncScope();
    }

    public async ValueTask DisposeAsync()
    {
        if (provider is null)
        {
            storage?.Dispose();
            return;
        }

        await scope.DisposeAsync();
        await provider.DisposeAsync();
        await DeleteBucketAsync(uploadsBucket);
        await DeleteBucketAsync(archiveBucket);
        storage.Dispose();
    }

    [Fact]
    public async Task Cloud_storage_root_supports_the_file_manager_operations()
    {
        await SkipWithoutEmulatorAsync();

        var empty = (await Service.ListAsync(new ListingRequest("uploads"), Token)).ShouldSucceed();
        Assert.Empty(empty.Items);

        var notes = (await Service.CreateDirectoryAsync(new CreateDirectoryRequest("uploads", "/", "Notes"), Token)).ShouldSucceed();
        Assert.Equal("/Notes", notes.Path);
        Assert.True(notes.IsDirectory);
        Assert.True(await ObjectExistsAsync(uploadsBucket, "campaigns/Notes/.folder.webfilez.tmp"));

        (await Service.CreateFileAsync(new CreateFileRequest("uploads", "/Notes", "hello.txt"), Token)).ShouldSucceed();
        var saved = (await Service.SaveTextAsync(new SaveTextFileRequest("uploads", "/Notes/hello.txt", "hello cloud"), Token)).ShouldSucceed();
        var text = (await Service.ReadTextAsync("uploads", "/Notes/hello.txt", Token)).ShouldSucceed();
        Assert.Equal("hello cloud", text.Content);
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

        var names = new List<string>();
        await foreach (var item in storage.ListObjectsAsync(uploadsBucket))
        {
            names.Add(item.Name);
        }

        Assert.DoesNotContain(names, name => name.StartsWith("campaigns/Notes", StringComparison.Ordinal));
    }

    private async Task SkipWithoutEmulatorAsync()
    {
        if (provider is not null)
        {
            return;
        }

        if (!await EmulatorIsListeningAsync())
        {
            Assert.Skip("fake-gcs-server is not listening on 127.0.0.1:4443.");
        }

        await InitializeAsync();
    }

    private async Task<bool> ObjectExistsAsync(string bucket, string name)
    {
        try
        {
            await storage.GetObjectAsync(bucket, name, cancellationToken: Token);
            return true;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task DeleteBucketAsync(string bucket)
    {
        await foreach (var item in storage.ListObjectsAsync(bucket))
        {
            await storage.DeleteObjectAsync(bucket, item.Name, cancellationToken: CancellationToken.None);
        }

        await storage.DeleteBucketAsync(bucket, cancellationToken: CancellationToken.None);
    }

    private static async Task<bool> EmulatorIsListeningAsync()
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", 4443);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static FileRoot CloudRoot(string id, string name, string bucket, string path) => new()
    {
        Id = id,
        Name = name,
        Provider = StorageProviders.GoogleCloud,
        Path = path,
        Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Bucket"] = bucket,
            ["ServiceUrl"] = ServiceUrl,
        },
    };
}
