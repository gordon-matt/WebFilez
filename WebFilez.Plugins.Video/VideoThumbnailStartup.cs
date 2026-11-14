using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebFilez.Plugins.Video.Ffmpeg;

namespace WebFilez.Plugins.Video;

/// <summary>
/// Points Xabe at ffmpeg before the app accepts requests. Failure leaves video thumbnails off.
/// </summary>
internal sealed class VideoThumbnailStartup(
    IOptions<VideoThumbnailOptions> options,
    IHostEnvironment environment,
    ILogger<VideoThumbnailStartup> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        VideoThumbnailOptions settings = options.Value;
        string? downloadDirectory = settings.DownloadDirectory;
        if (settings.DownloadIfMissing && string.IsNullOrWhiteSpace(downloadDirectory))
        {
            downloadDirectory = Path.Combine(environment.ContentRootPath, "ffmpeg");
        }

        return FfmpegBootstrap.EnsureAsync(
            new FfmpegBootstrapOptions
            {
                ConfiguredPath = settings.FfmpegPath,
                DownloadDirectory = downloadDirectory,
                SearchWellKnownLocations = settings.SearchWellKnownLocations,
                SearchApplicationDirectories = settings.SearchApplicationDirectories,
                SearchPath = settings.SearchPath,
                PreferLinuxSystemDirectory = settings.PreferLinuxSystemDirectory,
                DownloadIfMissing = settings.DownloadIfMissing,
                RequireFfprobe = true
            },
            logger,
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
