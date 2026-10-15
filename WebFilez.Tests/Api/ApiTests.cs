using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WebFilez.Tests.Infrastructure;

namespace WebFilez.Tests.Api;

/// <summary>
/// Runs the endpoints in a test server and drives them with the same HTTP client the WebAssembly
/// components use, so a route, payload or status mapping that drifts between the two fails here.
/// </summary>
public sealed class ApiTests : IAsyncLifetime
{
    private const string API_BASE_URL = "http://localhost/api/webfilez/";

    private readonly TempFolder files = new();
    private readonly TempFolder docs = new();
    private WebApplication app = null!;
    private ServiceProvider client = null!;
    private HttpClient http = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private IFileManagerService Service => client.GetRequiredService<IFileManagerService>();

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddWebFilez(configure: options =>
        {
            options.Roots.Add(new FileRoot { Id = "files", Name = "Files", Path = files.Path });
            options.Roots.Add(new FileRoot { Id = "docs", Name = "Docs", Path = docs.Path, IsReadOnly = true });
            options.Thumbnails.Enabled = false;
        });

        app = builder.Build();
        app.MapWebFilezApi();
        await app.StartAsync(Token);

        var server = app.GetTestServer();
        http = server.CreateClient();
        http.BaseAddress = new Uri(API_BASE_URL);

        client = new ServiceCollection()
            .AddWebFilezHttpClient(API_BASE_URL, configureClient: b => b.ConfigurePrimaryHttpMessageHandler(() => server.CreateHandler()))
            .BuildServiceProvider();
    }

    [Fact]
    public async Task Roots_RoundTrip()
    {
        var roots = (await Service.GetRootsAsync(Token)).ShouldSucceed();

        Assert.Equal(["files", "docs"], roots.Select(r => r.Id));
        Assert.True(roots[1].IsReadOnly);
    }

    [Fact]
    public async Task Listing_RoundTrip()
    {
        files.Write("dir/a.txt", "alpha");
        files.Write("b.txt");

        var listing = (await Service.ListAsync(new ListingRequest("files"), Token)).ShouldSucceed();

        Assert.Equal(["dir", "b.txt"], listing.Items.Select(i => i.Name));
        Assert.True(listing.Items[0].IsDirectory);

        var folders = (await Service.GetFoldersAsync("files", "/", Token)).ShouldSucceed();
        Assert.Equal("/dir", Assert.Single(folders).Path);
    }

    [Fact]
    public async Task Failures_KeepTheirStatusAndMessage()
    {
        docs.Write("guide.md");
        files.Write("taken.txt");

        var notFound = await Service.ListAsync(new ListingRequest("nope"), Token);
        var forbidden = await Service.RenameAsync(new RenameRequest("docs", "/guide.md", "x.md"), Token);
        var conflict = await Service.CreateFileAsync(new CreateFileRequest("files", "/", "taken.txt"), Token);
        var invalid = await Service.CreateFileAsync(new CreateFileRequest("files", "/", "x.php"), Token);

        Assert.Equal(ResultStatus.NotFound, notFound.Status);
        Assert.Equal(ResultStatus.Forbidden, forbidden.Status);
        Assert.Equal(ResultStatus.Conflict, conflict.Status);
        Assert.Contains("already exists", conflict.GetDisplayMessage());
        Assert.Equal(ResultStatus.Invalid, invalid.Status);
        Assert.Contains(".php", invalid.GetDisplayMessage());
    }

    [Fact]
    public async Task Mutations_RoundTrip()
    {
        (await Service.CreateDirectoryAsync(new CreateDirectoryRequest("files", "/", "box"), Token)).ShouldSucceed();
        (await Service.CreateFileAsync(new CreateFileRequest("files", "/box", "a.txt"), Token)).ShouldSucceed();

        var opened = (await Service.ReadTextAsync("files", "/box/a.txt", Token)).ShouldSucceed();
        (await Service.SaveTextAsync(new SaveTextFileRequest("files", "/box/a.txt", "hello", opened.ModifiedAt), Token)).ShouldSucceed();
        (await Service.RenameAsync(new RenameRequest("files", "/box/a.txt", "b.txt"), Token)).ShouldSucceed();

        var copy = (await Service.TransferAsync(new TransferRequest("files", ["/box/b.txt"], "files", "/", IsCopy: true), Token)).ShouldSucceed();
        Assert.True(copy.IsCompleteSuccess);
        Assert.Equal("hello", File.ReadAllText(files.Resolve("b.txt")));

        var deleted = (await Service.DeleteAsync(new DeleteRequest("files", ["/box"]), Token)).ShouldSucceed();
        Assert.Equal(1, deleted.SucceededCount);
        Assert.False(files.Exists("box"));
    }

    [Fact]
    public async Task Download_NeverRendersActiveContentInline()
    {
        files.Write("page.txt", "<script>alert(1)</script>");
        File.WriteAllText(Path.Combine(files.Path, "page.html"), "<script>alert(1)</script>");

        using var html = await http.GetAsync("download?root=files&path=/page.html&inline=true", Token);
        Assert.Equal(HttpStatusCode.OK, html.StatusCode);
        Assert.Equal("attachment", html.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("application/octet-stream", html.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", html.Headers.GetValues("X-Content-Type-Options").Single());

        using var text = await http.GetAsync("download?root=files&path=/page.txt&inline=true", Token);
        Assert.Equal("inline", text.Content.Headers.ContentDisposition?.DispositionType);
    }

    [Fact]
    public async Task Download_SupportsRanges()
    {
        files.Write("digits.txt", "0123456789");

        using var request = new HttpRequestMessage(HttpMethod.Get, "download?root=files&path=/digits.txt");
        request.Headers.Range = new RangeHeaderValue(2, 4);
        using var response = await http.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("234", await response.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task Download_ClampsTraversal()
    {
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(files.Path)!, "outside.txt"), "secret");

        using var response = await http.GetAsync("download?root=files&path=/../outside.txt", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Zip_TicketIsSingleUse()
    {
        files.Write("a.txt", "alpha");
        files.Write("b.txt", "beta");

        var ticket = (await Service.CreateDownloadTicketAsync(new DownloadTicketRequest("files", ["/a.txt", "/b.txt"]), Token)).ShouldSucceed();

        using var first = await http.GetAsync($"zip/{ticket.Token}", Token);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("application/zip", first.Content.Headers.ContentType?.MediaType);
        Assert.True((await first.Content.ReadAsByteArrayAsync(Token)).Length > 0);

        using var second = await http.GetAsync($"zip/{ticket.Token}", Token);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task Upload_RequiresTheCsrfHeader()
    {
        using var withoutHeader = await http.PostAsync("upload", Upload("a.txt"), Token);
        Assert.Equal(HttpStatusCode.BadRequest, withoutHeader.StatusCode);
        Assert.False(files.Exists("a.txt"));

        using var request = new HttpRequestMessage(HttpMethod.Post, "upload") { Content = Upload("a.txt", "deep/a.txt") };
        request.Headers.Add(Constants.RequestHeaderName, Constants.RequestHeaderValue);
        using var withHeader = await http.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, withHeader.StatusCode);
        Assert.Equal("hello", File.ReadAllText(files.Resolve("deep/a.txt")));
    }

    [Fact]
    public async Task Upload_IntoAReadOnlyRootIsForbidden()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "upload") { Content = Upload("a.txt", root: "docs") };
        request.Headers.Add(Constants.RequestHeaderName, Constants.RequestHeaderValue);
        using var response = await http.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static MultipartFormDataContent Upload(string fileName, string? relativePath = null, string root = "files")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(root), "root" },
            { new StringContent("/"), "path" },
        };

        if (relativePath is not null)
        {
            form.Add(new StringContent(relativePath), "relativePath");
        }

        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("hello")), "file", fileName);
        return form;
    }

    public async ValueTask DisposeAsync()
    {
        http.Dispose();
        await client.DisposeAsync();
        await app.DisposeAsync();
        files.Dispose();
        docs.Dispose();
    }
}