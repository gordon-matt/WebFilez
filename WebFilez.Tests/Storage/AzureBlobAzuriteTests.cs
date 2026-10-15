using System.Net.Sockets;
using System.Text;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using WebFilez.Services.Transfers;
using WebFilez.Storage.AzureBlob;
using WebFilez.Tests.Infrastructure;

namespace WebFilez.Tests.Storage;

/// <summary>
/// Runs the file manager against Azurite. Skipped when nothing is listening on the blob port,
/// so a normal test run does not require the emulator.
/// </summary>
public class AzureBlobAzuriteTests : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";

    private ServiceProvider provider = null!;
    private AsyncServiceScope scope;
    private BlobServiceClient blobs = null!;
    private string uploadsContainer = null!;
    private string archiveContainer = null!;

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
        uploadsContainer = "uploads" + suffix;
        archiveContainer = "archive" + suffix;

        blobs = new BlobServiceClient(ConnectionString);
        await blobs.GetBlobContainerClient(uploadsContainer).CreateAsync(cancellationToken: Token);
        await blobs.GetBlobContainerClient(archiveContainer).CreateAsync(cancellationToken: Token);

        provider = new ServiceCollection()
            .AddLogging()
            .AddWebFilezServices(configure: options =>
            {
                options.Roots.Add(BlobRoot("uploads", "Uploads", uploadsContainer, "campaigns"));
                options.Roots.Add(BlobRoot("archive", "Archive", archiveContainer, path: ""));
                options.Thumbnails.CacheDirectory = Path.Combine(Path.GetTempPath(), "webfilez-azurite-thumbs");
            })
            .AddWebFilezAzureBlob()
            .BuildServiceProvider(validateScopes: true);

        scope = provider.CreateAsyncScope();
    }

    public async ValueTask DisposeAsync()
    {
        if (provider is null)
        {
            return;
        }

        await scope.DisposeAsync();
        await provider.DisposeAsync();

        await blobs.GetBlobContainerClient(uploadsContainer).DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
        await blobs.GetBlobContainerClient(archiveContainer).DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
    }

    [Fact]
    public async Task Blob_root_supports_the_file_manager_operations()
    {
        await SkipWithoutEmulatorAsync();

        var empty = (await Service.ListAsync(new ListingRequest("uploads"), Token)).ShouldSucceed();
        Assert.Empty(empty.Items);

        var notes = (await Service.CreateDirectoryAsync(new CreateDirectoryRequest("uploads", "/", "Notes"), Token)).ShouldSucceed();
        Assert.Equal("/Notes", notes.Path);
        Assert.True(notes.IsDirectory);

        var container = blobs.GetBlobContainerClient(uploadsContainer);
        Assert.True(await container.GetBlobClient("campaigns/Notes/.folder.webfilez.tmp").ExistsAsync(Token));

        (await Service.CreateFileAsync(new CreateFileRequest("uploads", "/Notes", "hello.txt"), Token)).ShouldSucceed();
        var saved = (await Service.SaveTextAsync(new SaveTextFileRequest("uploads", "/Notes/hello.txt", "hello azurite"), Token)).ShouldSucceed();
        var text = (await Service.ReadTextAsync("uploads", "/Notes/hello.txt", Token)).ShouldSucceed();
        Assert.Equal("hello azurite", text.Content);
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
        Assert.Equal(0, extracted.FailedCount);
        Assert.Equal("from a folder drop", (await Service.ReadTextAsync("uploads", "/restored/photos/day1/caption.txt", Token)).ShouldSucceed().Content);

        var deleted = (await Service.DeleteAsync(new DeleteRequest("uploads", ["/Notes", "/photos", "/photos.zip", "/restored"]), Token)).ShouldSucceed();
        Assert.Equal(0, deleted.FailedCount);
        Assert.Empty((await Service.ListAsync(new ListingRequest("uploads"), Token)).ShouldSucceed().Items);

        var names = new List<string>();
        await foreach (var blob in blobs.GetBlobContainerClient(uploadsContainer).GetBlobsAsync(cancellationToken: Token))
        {
            names.Add(blob.Name);
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
            Assert.Skip("Azurite is not listening on 127.0.0.1:10000.");
        }

        await InitializeAsync();
    }

    private static async Task<bool> EmulatorIsListeningAsync()
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", 10000);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static FileRoot BlobRoot(string id, string name, string container, string path) => new()
    {
        Id = id,
        Name = name,
        Provider = AzureBlobStorageProvider.ProviderKey,
        Path = path,
        Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionString"] = ConnectionString,
            ["Container"] = container,
        },
    };
}