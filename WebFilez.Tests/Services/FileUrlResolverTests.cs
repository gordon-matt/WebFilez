using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebFilez.Services.Storage;
using WebFilez.Storage.AmazonS3;
using WebFilez.Storage.AzureBlob;
using WebFilez.Storage.AzureFiles;
using WebFilez.Storage.GoogleCloud;
using WebFilez.Tests.Infrastructure;

namespace WebFilez.Tests.Services;

public class FileUrlResolverTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Filesystem_WithoutABase_ReturnsNull()
    {
        string? url = await Resolve(Root("filesystem", "C:\\data"), "/Photo One.png");

        Assert.Null(url);
    }

    [Theory]
    [InlineData("/Photo One.png", false, "/media/Photo%20One.png")]
    [InlineData("/Album", true, "/media/Album/")]
    [InlineData("/", true, "/media/")]
    [InlineData("/My Folder/a b#?.png", false, "/media/My%20Folder/a%20b%23%3F.png")]
    [InlineData("/100%+café.png", false, "/media/100%25%2Bcaf%C3%A9.png")]
    public async Task Filesystem_PublicUrl_MatchesTheExistingAddress(string path, bool directory, string expected)
    {
        var root = Root("filesystem", "C:\\data");
        root.PublicUrl = "/media/";

        Assert.Equal(expected, await Resolve(root, path, directory));
    }

    [Fact]
    public async Task Filesystem_PublicBaseUrl_KeepsTheVirtualPath()
    {
        var root = Root("filesystem", "C:\\data\\App_Data\\media");
        root.PublicBaseUrl = "https://cdn.example.com/media/";

        string? url = await Resolve(root, "/My Folder/Photo One.png");

        Assert.Equal("https://cdn.example.com/media/My%20Folder/Photo%20One.png", url);
        Assert.DoesNotContain("App_Data", url);
    }

    [Fact]
    public async Task RootPublicUrl_WinsOverTheGlobalBase()
    {
        var root = Root("filesystem", "C:\\data");
        root.PublicUrl = "/media";
        var resolver = Create(new WebFilezOptions { PublicBaseUrl = "https://cdn.example.com" });

        Assert.Equal("/media/a.png", await Resolve(resolver, root, "/a.png"));
    }

    [Fact]
    public async Task GlobalBase_AppliesWhenTheRootHasNone()
    {
        var resolver = Create(new WebFilezOptions { PublicBaseUrl = "https://cdn.example.com/" });

        Assert.Equal("https://cdn.example.com/Photo%20One.png", await Resolve(resolver, Root("filesystem", "C:\\data"), "/Photo One.png"));
    }

    [Fact]
    public async Task AzureBlob_PlainUrl_AndCdnBase_DoNotRepeatTheContainer()
    {
        var root = Root("azure-blob", "campaigns", new Dictionary<string, string>
        {
            ["ConnectionString"] = "DefaultEndpointsProtocol=https;AccountName=contoso;AccountKey=abc=;EndpointSuffix=core.windows.net",
            ["Container"] = "files",
        });

        Assert.Equal(
            "https://contoso.blob.core.windows.net/files/campaigns/a%20b.png",
            await Resolve(root, "/a b.png"));

        root.PublicBaseUrl = "https://cdn.example.com/";
        string? cdn = await Resolve(root, "/a b.png");
        Assert.Equal("https://cdn.example.com/campaigns/a%20b.png", cdn);
        Assert.DoesNotContain("/files/", cdn);
    }

    [Fact]
    public async Task AzureBlob_DevelopmentStorageAndBlobEndpoint_UseTheEmulatorHost()
    {
        var development = Root("azure-blob", "campaigns", new Dictionary<string, string>
        {
            ["ConnectionString"] = "UseDevelopmentStorage=true",
            ["Container"] = "files",
        });
        var endpoint = Root("azure-blob", "campaigns", new Dictionary<string, string>
        {
            ["ConnectionString"] = "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;AccountName=devstoreaccount1",
            ["Container"] = "files",
        });

        const string expected = "http://127.0.0.1:10000/devstoreaccount1/files/campaigns/a.png";
        Assert.Equal(expected, await Resolve(development, "/a.png"));
        Assert.Equal(expected, await Resolve(endpoint, "/a.png"));
    }

    [Fact]
    public async Task AzureFiles_PlainUrl_AndCdnBase()
    {
        var root = Root("azure-file", "campaigns", new Dictionary<string, string>
        {
            ["ConnectionString"] = "DefaultEndpointsProtocol=https;AccountName=contoso;AccountKey=abc;EndpointSuffix=core.windows.net",
            ["Share"] = "drop",
        });

        Assert.Equal(
            "https://contoso.file.core.windows.net/drop/campaigns/Photo%20One.png",
            await Resolve(root, "/Photo One.png"));

        root.Settings["PublicBaseUrl"] = "https://cdn.example.com";
        string? cdn = await Resolve(root, "/Photo One.png");
        Assert.Equal("https://cdn.example.com/campaigns/Photo%20One.png", cdn);
        Assert.DoesNotContain("/drop/", cdn);
    }

    [Fact]
    public async Task AmazonS3_PlainUrl_ServiceUrl_AndCdnBase()
    {
        var root = Root("amazon-s3", "campaigns", new Dictionary<string, string>
        {
            ["Bucket"] = "files",
            ["Region"] = "us-west-2",
        });

        Assert.Equal(
            "https://files.s3.us-west-2.amazonaws.com/campaigns/Photo%20One.png",
            await Resolve(root, "/Photo One.png"));
        Assert.Equal(
            "https://files.s3.us-west-2.amazonaws.com/campaigns/My%20Folder/",
            await Resolve(root, "/My Folder", directory: true));

        root.Settings.Remove("Region");
        Assert.Equal(
            "https://files.s3.us-east-1.amazonaws.com/campaigns/Photo%20One.png",
            await Resolve(root, "/Photo One.png"));

        root.Settings["ServiceUrl"] = "http://127.0.0.1:4566/";
        Assert.Equal(
            "http://127.0.0.1:4566/files/campaigns/Photo%20One.png",
            await Resolve(root, "/Photo One.png"));

        root.PublicBaseUrl = "https://cdn.example.com/";
        string? cdn = await Resolve(root, "/a b#?.png");
        Assert.Equal("https://cdn.example.com/campaigns/a%20b%23%3F.png", cdn);
        Assert.DoesNotContain("/files/", cdn);
    }

    [Fact]
    public async Task GoogleCloud_PlainUrl_ServiceUrl_AndCdnBase()
    {
        var root = Root("google-cloud", "campaigns", new Dictionary<string, string>
        {
            ["Bucket"] = "files",
        });

        Assert.Equal(
            "https://storage.googleapis.com/files/campaigns/100%25%2Bcaf%C3%A9.png",
            await Resolve(root, "/100%+café.png"));

        root.Settings["ServiceUrl"] = "http://127.0.0.1:4443";
        Assert.Equal(
            "http://127.0.0.1:4443/files/campaigns/a.png",
            await Resolve(root, "/a.png"));

        root.PublicBaseUrl = "https://cdn.example.com";
        string? cdn = await Resolve(root, "/a.png");
        Assert.Equal("https://cdn.example.com/campaigns/a.png", cdn);
        Assert.DoesNotContain("/files/", cdn);
    }

    [Fact]
    public async Task LegacyPublicUrl_OnACloudRoot_StaysTheVirtualPath()
    {
        var root = Root("amazon-s3", "campaigns", new Dictionary<string, string> { ["Bucket"] = "files" });
        root.PublicUrl = "/media";

        Assert.Equal("/media/Photo%20One.png", await Resolve(root, "/Photo One.png"));
    }

    [Fact]
    public async Task PublicBaseUrl_BeatsLegacyPublicUrl_AndUsesTheObjectKey()
    {
        var root = Root("amazon-s3", "campaigns", new Dictionary<string, string> { ["Bucket"] = "files" });
        root.PublicUrl = "/media";
        root.PublicBaseUrl = "https://cdn.example.com";

        Assert.Equal("https://cdn.example.com/campaigns/Photo%20One.png", await Resolve(root, "/Photo One.png"));
    }

    [Fact]
    public async Task PrefixSetting_ReplacesTheRootPath()
    {
        var root = Root("amazon-s3", "campaigns", new Dictionary<string, string>
        {
            ["Bucket"] = "files",
            ["Prefix"] = "other",
        });

        Assert.Equal(
            "https://files.s3.us-east-1.amazonaws.com/other/a.png",
            await Resolve(root, "/a.png"));
    }

    [Fact]
    public async Task UnknownProvider_HasNoPlainUrl_UntilABaseOrContributorIsSet()
    {
        var root = Root("custom-store", "campaigns");

        Assert.Null(await Resolve(root, "/a.png"));

        root.PublicBaseUrl = "https://cdn.example.com";
        Assert.Equal("https://cdn.example.com/campaigns/a.png", await Resolve(root, "/a.png"));
    }

    [Fact]
    public async Task CustomProvider_PlainUrl_ComesFromItsOwnContributor()
    {
        var resolver = new DefaultFileUrlResolver(
            new FixedOptions(new WebFilezOptions()),
            NullLogger<DefaultFileUrlResolver>.Instance,
            [new CustomPublicUrl()]);

        Assert.Equal(
            "custom://store/campaigns/a.png",
            await Resolve(resolver, Root("custom-store", "campaigns"), "/a.png"));
    }

    [Fact]
    public async Task ProviderPackage_SuppliesThePlainUrl_ThroughTheDefaultResolver()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddWebFilezServices()
            .AddWebFilezAmazonS3()
            .BuildServiceProvider();

        var resolver = provider.GetRequiredService<IFileUrlResolver>();
        string? url = await resolver.ResolveAsync(
            FileUrlContext.For(
                Root("amazon-s3", "campaigns", new Dictionary<string, string> { ["Bucket"] = "files" }),
                "/a.png",
                isDirectory: false,
                allowExpiringUrls: false),
            Token);

        Assert.Equal("https://files.s3.us-east-1.amazonaws.com/campaigns/a.png", url);
    }

    [Fact]
    public async Task MissingBucket_ReturnsNull_UntilABaseIsSet()
    {
        var root = Root("amazon-s3", "campaigns");

        Assert.Null(await Resolve(root, "/a.png"));

        root.PublicBaseUrl = "https://cdn.example.com";
        Assert.Equal("https://cdn.example.com/campaigns/a.png", await Resolve(root, "/a.png"));
    }

    [Fact]
    public async Task AllowExpiringUrls_DoesNotSign()
    {
        var root = Root("amazon-s3", "campaigns", new Dictionary<string, string> { ["Bucket"] = "files" });

        string? url = await Resolve(root, "/a.png", allowExpiring: true);

        Assert.Equal("https://files.s3.us-east-1.amazonaws.com/campaigns/a.png", url);
        Assert.DoesNotContain("?", url);
    }

    [Fact]
    public void AddWebFilezServices_UsesTheDefaultResolver_UnlessOneWasRegistered()
    {
        using var defaults = new ServiceCollection()
            .AddLogging()
            .AddWebFilezServices()
            .BuildServiceProvider();
        using var replaced = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IFileUrlResolver, HostResolver>()
            .AddWebFilezServices()
            .BuildServiceProvider();

        Assert.IsType<DefaultFileUrlResolver>(defaults.GetRequiredService<IFileUrlResolver>());
        Assert.IsType<HostResolver>(replaced.GetRequiredService<IFileUrlResolver>());
    }

    [Fact]
    public async Task ListingAndSearch_UseAResolverRegisteredEarlier()
    {
        using var folder = new TempFolder();
        folder.Write("Photo One.png");

        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IFileUrlResolver, HostResolver>()
            .AddWebFilezServices(configure: options =>
            {
                options.Roots.Add(new FileRoot { Id = "files", Name = "Files", Path = folder.Path, PublicUrl = "/media" });
            })
            .BuildServiceProvider(validateScopes: true);

        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IFileManagerService>();

        var listing = (await service.ListAsync(new ListingRequest("files"), Token)).ShouldSucceed();
        Assert.Equal("custom:/", listing.PublicUrl);
        Assert.Equal("custom:/Photo One.png", Assert.Single(listing.Items).PublicUrl);

        var search = (await service.SearchAsync(new SearchRequest("Photo"), Token)).ShouldSucceed();
        Assert.Equal("custom:/Photo One.png", Assert.Single(search.Hits).PublicUrl);
    }

    private static async Task<string?> Resolve(FileRoot root, string path, bool directory = false, bool allowExpiring = false) =>
        await Resolve(Create(new WebFilezOptions()), root, path, directory, allowExpiring);

    private static async Task<string?> Resolve(
        DefaultFileUrlResolver resolver,
        FileRoot root,
        string path,
        bool directory = false,
        bool allowExpiring = false) =>
        await resolver.ResolveAsync(FileUrlContext.For(root, path, directory, allowExpiring), Token);

    private static DefaultFileUrlResolver Create(WebFilezOptions options) =>
        new(
            new FixedOptions(options),
            NullLogger<DefaultFileUrlResolver>.Instance,
            [new AzureBlobPublicUrl(), new AzureFilePublicUrl(), new AmazonS3PublicUrl(), new GoogleCloudPublicUrl()]);

    private static FileRoot Root(string provider, string path, Dictionary<string, string>? settings = null) => new()
    {
        Id = "files",
        Provider = provider,
        Path = path,
        Settings = new Dictionary<string, string>(settings ?? [], StringComparer.OrdinalIgnoreCase),
    };

    private sealed class FixedOptions(WebFilezOptions options) : IOptionsMonitor<WebFilezOptions>
    {
        public WebFilezOptions CurrentValue { get; } = options;

        public WebFilezOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<WebFilezOptions, string?> listener) => null;
    }

    private sealed class HostResolver : IFileUrlResolver
    {
        public ValueTask<string?> ResolveAsync(FileUrlContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>("custom:" + context.VirtualPath);
    }

    private sealed class CustomPublicUrl : IStoragePublicUrl
    {
        public string ProviderKey => "custom-store";

        public string? GetUrl(FileUrlContext context) =>
            FileUrl.Combine("custom://store", FileUrl.ObjectKey(context), context.IsDirectory);
    }
}
