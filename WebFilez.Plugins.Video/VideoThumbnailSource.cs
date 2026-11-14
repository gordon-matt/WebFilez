using Microsoft.Extensions.Logging;
using WebFilez.Plugins.Video.Ffmpeg;
using WebFilez.Services.FileSystem;
using WebFilez.Services.Images;
using WebFilez.Services.Storage;
using Xabe.FFmpeg;

namespace WebFilez.Plugins.Video;

/// <summary>
/// Grabs one frame from the middle of a video. The bytes are a JPEG so the normal thumbnail
/// pipeline can scale them to WebP and cache them with the image thumbnails.
/// </summary>
internal sealed class VideoThumbnailSource(ILogger<VideoThumbnailSource> logger) : IThumbnailSource
{
    private const int MaxConcurrentCaptures = 2;
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(15);
    private static readonly SemaphoreSlim Gate = new(MaxConcurrentCaptures, MaxConcurrentCaptures);

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mpeg", ".mpg", ".mkv", ".m4v", ".avi", ".mov", ".webm", ".wmv"
    };

    public bool Supports(string fileName) =>
        FfmpegBootstrap.IsAvailable && Extensions.Contains(Path.GetExtension(fileName));

    public async Task<byte[]?> TryReadImageAsync(ResolvedPath target, CancellationToken cancellationToken = default)
    {
        if (!Supports(Path.GetFileName(target.VirtualPath)))
        {
            return null;
        }

        string? ownedFile = null;
        try
        {
            string videoPath = LocalVideoPath(target);
            if (videoPath.Length == 0)
            {
                ownedFile = TempVideoPath(target);
                await CopyToTempAsync(target, ownedFile, cancellationToken);
                videoPath = ownedFile;
            }

            return await CaptureJpegAsync(videoPath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read a frame from {Path}", target.VirtualPath);
            return null;
        }
        finally
        {
            if (ownedFile is not null)
            {
                TryDelete(ownedFile);
            }
        }
    }

    /// <summary>
    /// A file-system root is already a local path. Any other provider's address is a key, not a file,
    /// so the bytes are copied before ffmpeg sees them.
    /// </summary>
    private static string LocalVideoPath(ResolvedPath target) =>
        FileSystemStorageProvider.IsFileSystem(target.Root.Provider) && File.Exists(target.PhysicalPath)
            ? target.PhysicalPath
            : string.Empty;

    private static string TempVideoPath(ResolvedPath target)
    {
        string extension = Path.GetExtension(target.VirtualPath);
        return Path.Combine(Path.GetTempPath(), "webfilez-" + Guid.NewGuid().ToString("N") + extension);
    }

    private static async Task CopyToTempAsync(ResolvedPath target, string path, CancellationToken cancellationToken)
    {
        await using Stream input = await target.Storage.OpenReadAsync(target.Location, cancellationToken);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static async Task<byte[]?> CaptureJpegAsync(string videoPath, CancellationToken cancellationToken)
    {
        string framePath = Path.Combine(Path.GetTempPath(), "webfilez-" + Guid.NewGuid().ToString("N") + ".jpg");
        await Gate.WaitAsync(cancellationToken);
        try
        {
            TimeSpan at = await GetMidpointAsync(videoPath, cancellationToken);
            bool captured = await TryCaptureAsync(videoPath, framePath, at, cancellationToken)
                || (at > TimeSpan.Zero && await TryCaptureAsync(videoPath, framePath, TimeSpan.Zero, cancellationToken));
            if (!captured)
            {
                return null;
            }

            return await File.ReadAllBytesAsync(framePath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Gate.Release();
            TryDelete(framePath);
        }
    }

    private static async Task<TimeSpan> GetMidpointAsync(string videoPath, CancellationToken cancellationToken)
    {
        IMediaInfo media = await FFmpeg.GetMediaInfo(videoPath, cancellationToken);
        TimeSpan duration = media.Duration;
        if (duration <= TimeSpan.Zero)
        {
            duration = media.VideoStreams.FirstOrDefault()?.Duration ?? TimeSpan.Zero;
        }

        return duration > TimeSpan.Zero
            ? TimeSpan.FromTicks(duration.Ticks / 2)
            : TimeSpan.Zero;
    }

    private static async Task<bool> TryCaptureAsync(
        string videoPath,
        string outputPath,
        TimeSpan at,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CaptureTimeout);
        try
        {
            IConversion conversion = await FFmpeg.Conversions.FromSnippet.Snapshot(videoPath, outputPath, at);
            conversion.SetOverwriteOutput(true);
            // A crafted file can otherwise make ffmpeg open http, concat, or subfile targets.
            conversion.AddParameter("-nostdin -protocol_whitelist file", ParameterPosition.PreInput);
            await conversion.Start(timeout.Token);
            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

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
