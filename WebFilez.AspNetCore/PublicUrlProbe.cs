using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WebFilez;

/// <summary>
/// Requests one insertable URL anonymously. A 401, 403 or 404 means visitors will not see the
/// file. The check is off unless <see cref="WebFilezOptions.ProbePublicUrls"/> is set, and a
/// failure here never fails an upload.
/// </summary>
public sealed class PublicUrlProbe(
    IFileManagerService files,
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<WebFilezOptions> options,
    ILogger<PublicUrlProbe> logger)
{
    public const string HttpClientName = "WebFilez.PublicUrlProbe";

    /// <summary>Warning returned and logged when the insertable URL is not anonymously readable.</summary>
    public const string NotPubliclyReadable =
        "This URL is not publicly readable. Inserted files may not display for site visitors. See README: Storage setups.";

    public async Task<PublicUrlProbeResult> CheckAsync(
        string? rootId,
        string? path,
        CancellationToken cancellationToken = default)
    {
        if (!options.CurrentValue.ProbePublicUrls)
        {
            return new PublicUrlProbeResult(false, rootId, path, null, null, null);
        }

        try
        {
            return await CheckEnabledAsync(rootId, path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The public URL check failed. Uploads were not affected.");
            return new PublicUrlProbeResult(
                true,
                rootId,
                path,
                null,
                null,
                "The public URL could not be checked. Uploads were not affected.");
        }
    }

    private async Task<PublicUrlProbeResult> CheckEnabledAsync(
        string? rootId,
        string? path,
        CancellationToken cancellationToken)
    {
        var sample = await FindSampleAsync(rootId, path, cancellationToken);
        if (sample.Error is not null)
        {
            return new PublicUrlProbeResult(true, sample.RootId, sample.Path, null, null, sample.Error);
        }

        if (string.IsNullOrWhiteSpace(sample.Url))
        {
            return new PublicUrlProbeResult(
                true,
                sample.RootId,
                sample.Path,
                null,
                null,
                "This root has no insertable URL. Set a public base URL, or register a resolver. See README: Storage setups.");
        }

        if (!Uri.TryCreate(sample.Url, UriKind.Absolute, out var absolute)
            || absolute.Scheme is not ("http" or "https"))
        {
            return new PublicUrlProbeResult(
                true,
                sample.RootId,
                sample.Path,
                sample.Url,
                null,
                "The insertable URL is relative, so it was not requested anonymously. The site that serves it decides who can read it.");
        }

        int status = await StatusAsync(absolute, cancellationToken);
        string? warning = status is 401 or 403 or 404 ? NotPubliclyReadable : null;
        if (warning is not null)
        {
            logger.LogWarning("{Warning} URL: {Url} Status: {StatusCode}", warning, sample.Url, status);
        }

        return new PublicUrlProbeResult(true, sample.RootId, sample.Path, sample.Url, status, warning);
    }

    private async Task<Sample> FindSampleAsync(string? rootId, string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rootId))
        {
            var roots = await files.GetRootsAsync(cancellationToken);
            if (!roots.IsSuccess || roots.Value.Count == 0)
            {
                return new Sample(rootId, path, null, "No root is available to check.");
            }

            rootId = roots.Value[0].Id;
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            var detail = await files.GetDetailAsync(rootId, path, cancellationToken);
            if (!detail.IsSuccess)
            {
                return new Sample(rootId, path, null, detail.GetDisplayMessage());
            }

            if (!detail.Value.IsDirectory)
            {
                return new Sample(rootId, detail.Value.Path, detail.Value.PublicUrl, null);
            }

            path = detail.Value.Path;
        }

        var listing = await files.ListAsync(new ListingRequest(rootId, path, PageSize: 200), cancellationToken);
        if (!listing.IsSuccess)
        {
            return new Sample(rootId, path, null, listing.GetDisplayMessage());
        }

        var file = listing.Value.Items.FirstOrDefault(item => !item.IsDirectory);
        if (file is null)
        {
            var folder = listing.Value.Items.FirstOrDefault(item => item.IsDirectory);
            if (folder is not null)
            {
                var nested = await files.ListAsync(new ListingRequest(rootId, folder.Path, PageSize: 200), cancellationToken);
                file = nested.IsSuccess ? nested.Value.Items.FirstOrDefault(item => !item.IsDirectory) : null;
            }
        }

        return file is null
            ? new Sample(rootId, path, null, "No file was found to check.")
            : new Sample(rootId, file.Path, file.PublicUrl, null);
    }

    /// <summary>HEAD when the server allows it, otherwise GET. The body is not read.</summary>
    private async Task<int> StatusAsync(Uri url, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using (var head = new HttpRequestMessage(HttpMethod.Head, url))
        using (var headResponse = await client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            if (headResponse.StatusCode is not HttpStatusCode.MethodNotAllowed and not HttpStatusCode.NotImplemented)
            {
                return (int)headResponse.StatusCode;
            }
        }

        using var get = new HttpRequestMessage(HttpMethod.Get, url);
        using var getResponse = await client.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return (int)getResponse.StatusCode;
    }

    private sealed record Sample(string? RootId, string? Path, string? Url, string? Error);
}

/// <summary>
/// Runs <see cref="PublicUrlProbe"/> once at startup when it is enabled. Returns immediately so
/// startup is not blocked, and swallows failures so a private bucket cannot stop the host.
/// </summary>
public sealed class PublicUrlProbeHostedService(
    IServiceScopeFactory scopes,
    IOptionsMonitor<WebFilezOptions> options,
    ILogger<PublicUrlProbeHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.ProbePublicUrls)
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var probe = scope.ServiceProvider.GetRequiredService<PublicUrlProbe>();
            await probe.CheckAsync(rootId: null, path: null, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The public URL check failed. Startup and uploads were not affected.");
        }
    }
}
