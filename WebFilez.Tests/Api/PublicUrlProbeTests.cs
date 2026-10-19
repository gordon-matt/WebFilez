using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebFilez.Tests.Infrastructure;

namespace WebFilez.Tests.Api;

public class PublicUrlProbeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UrlCheck_WarnsWhenTheInsertableUrlIsNotPublic()
    {
        var state = new ProbeHandlerState { HeadStatus = HttpStatusCode.Forbidden };
        await using var host = await ProbeHost.StartAsync(state, enabled: true, publicBaseUrl: "https://cdn.example.com");

        using var json = await ReadAsync(host);

        Assert.True(json.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(403, json.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(PublicUrlProbe.NotPubliclyReadable, json.RootElement.GetProperty("warning").GetString());
        Assert.EndsWith("/Album/Photo%20One.png", json.RootElement.GetProperty("url").GetString());
        Assert.DoesNotContain(state.Methods, method => method == HttpMethod.Get);
        Assert.Contains(HttpMethod.Head, state.Methods);
    }

    [Fact]
    public async Task UrlCheck_FallsBackToGetWhenHeadIsNotSupported()
    {
        var state = new ProbeHandlerState
        {
            HeadStatus = HttpStatusCode.MethodNotAllowed,
            GetStatus = HttpStatusCode.OK,
        };
        await using var host = await ProbeHost.StartAsync(state, enabled: true, publicBaseUrl: "https://cdn.example.com");

        using var json = await ReadAsync(host);

        Assert.Equal(200, json.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("warning").ValueKind);
        Assert.Contains(HttpMethod.Head, state.Methods);
        Assert.Contains(HttpMethod.Get, state.Methods);
    }

    [Fact]
    public async Task UrlCheck_DoesNotRequestARelativeUrl()
    {
        var state = new ProbeHandlerState { Throw = true };
        await using var host = await ProbeHost.StartAsync(state, enabled: true, publicUrl: "/media");

        using var json = await ReadAsync(host);

        Assert.Equal("/media/Album/Photo%20One.png", json.RootElement.GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("statusCode").ValueKind);
        Assert.Contains("relative", json.RootElement.GetProperty("warning").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(state.Methods);
    }

    [Fact]
    public async Task UrlCheck_DoesNothingWhenDisabled()
    {
        var state = new ProbeHandlerState { Throw = true };
        await using var host = await ProbeHost.StartAsync(state, enabled: false, publicBaseUrl: "https://cdn.example.com");

        using var json = await ReadAsync(host);

        Assert.False(json.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("warning").ValueKind);
        Assert.Empty(state.Methods);
    }

    private static async Task<JsonDocument> ReadAsync(ProbeHost host)
    {
        string body = await host.Client.GetStringAsync("/api/webfilez/url-check", Token);
        return JsonDocument.Parse(body);
    }

    private sealed class ProbeHost : IAsyncDisposable
    {
        private readonly WebApplication app;
        private readonly TempFolder folder;

        private ProbeHost(WebApplication app, HttpClient client, TempFolder folder)
        {
            this.app = app;
            this.folder = folder;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<ProbeHost> StartAsync(
            ProbeHandlerState state,
            bool enabled,
            string? publicBaseUrl = null,
            string? publicUrl = null)
        {
            var folder = new TempFolder();
            folder.Write("Album/Photo One.png", "x");

            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.SetMinimumLevel(LogLevel.None);
            builder.Services.AddWebFilez(configure: options =>
            {
                options.ProbePublicUrls = enabled;
                options.Thumbnails.Enabled = false;
                options.Roots.Add(new FileRoot
                {
                    Id = "files",
                    Name = "Files",
                    Path = folder.Path,
                    PublicUrl = publicUrl,
                    PublicBaseUrl = publicBaseUrl,
                });
            });
            builder.Services.AddHttpClient(PublicUrlProbe.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new ProbeHandler(state));

            var app = builder.Build();
            app.MapWebFilezApi();
            await app.StartAsync(Token);

            return new ProbeHost(app, app.GetTestServer().CreateClient(), folder);
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync(Token);
            await app.DisposeAsync();
            folder.Dispose();
        }
    }

    private sealed class ProbeHandlerState
    {
        public HttpStatusCode HeadStatus { get; set; } = HttpStatusCode.OK;

        public HttpStatusCode GetStatus { get; set; } = HttpStatusCode.OK;

        public bool Throw { get; set; }

        public ConcurrentQueue<HttpMethod> Methods { get; } = [];
    }

    private sealed class ProbeHandler(ProbeHandlerState state) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (state.Throw)
            {
                throw new InvalidOperationException("The probe requested a URL while it should not have.");
            }

            state.Methods.Enqueue(request.Method);
            HttpStatusCode status = request.Method == HttpMethod.Head ? state.HeadStatus : state.GetStatus;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
