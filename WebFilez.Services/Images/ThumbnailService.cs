using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SkiaSharp;
using WebFilez.Services.Storage;
using WebFilez.Services.Transfers;

namespace WebFilez.Services.Images;

/// <summary>Builds a WebP thumbnail of a file the caller can read.</summary>
public interface IThumbnailService
{
    /// <summary>
    /// A WebP thumbnail, snapped to 128, 256, or 512. Thumbnails that are turned off, and files no source can read, are not found.
    /// The caller needs read permission. A cached file is returned when the size and the file have not changed.
    /// </summary>
    /// <param name="rootId">The root.</param>
    /// <param name="path">The file.</param>
    /// <param name="size">The requested edge length. The nearest supported size is used. The default is 256.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The WebP bytes, or not found.</returns>
    Task<Result<ThumbnailDto>> GetAsync(string rootId, string path, int size = 256, CancellationToken cancellationToken = default);
}

/// <summary>Generates and disk-caches WebP thumbnails.</summary>
public sealed class ThumbnailService(
    ILogger<ThumbnailService> logger,
    PathResolver resolver,
    IEnumerable<IThumbnailSource> thumbnailSources,
    IOptionsMonitor<WebFilezOptions> options) : IThumbnailService
{
    private readonly ThumbnailSupport thumbnails = new(thumbnailSources);
    private const string WebpMimeType = "image/webp";

    /// <summary>
    /// Sizes the UI may ask for. Arbitrary sizes would let one client fill the cache directory
    /// with near-identical files.
    /// </summary>
    internal static readonly int[] AllowedSizes = [128, 256, 512];

    /// <summary>Bounds concurrent decodes, so a grid of a hundred tiles can't pin every core.</summary>
    private static readonly SemaphoreSlim GenerationLock = new(Math.Max(1, Environment.ProcessorCount / 2));

    /// <inheritdoc />
    public async Task<Result<ThumbnailDto>> GetAsync(
        string rootId,
        string path,
        int size = 256,
        CancellationToken cancellationToken = default)
    {
        var settings = options.CurrentValue.Thumbnails;
        if (!settings.Enabled)
        {
            return Result<ThumbnailDto>.NotFound();
        }

        int resolvedSize = AllowedSizes.MinBy(s => Math.Abs(s - size));

        var resolved = await resolver.RequireAsync(rootId, path, FilePermission.Read, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.To<ThumbnailDto>();
        }

        var target = resolved.Value;
        var info = await target.Storage.GetAsync(target.Location, cancellationToken);
        if (info is not { IsDirectory: false } || !thumbnails.Supports(info.Name))
        {
            return Result<ThumbnailDto>.NotFound();
        }

        string etag = $"{info.SizeBytes:x}-{info.ModifiedAt.Ticks:x}-{resolvedSize}";
        string cachePath = BuildCachePath(settings, target, resolvedSize, info);

        if (File.Exists(cachePath))
        {
            try
            {
                return Result<ThumbnailDto>.Success(new ThumbnailDto(await File.ReadAllBytesAsync(cachePath, cancellationToken), WebpMimeType, etag));
            }
            catch (IOException ex)
            {
                logger.LogDebug(ex, "Could not read cached thumbnail {Cache}", cachePath);
            }
        }

        await GenerationLock.WaitAsync(cancellationToken);
        try
        {
            byte[] bytes = await GenerateAsync(target, resolvedSize, settings.Quality, cancellationToken);
            await WriteCacheAsync(cachePath, bytes, cancellationToken);

            return Result<ThumbnailDto>.Success(new ThumbnailDto(bytes, WebpMimeType, etag));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An image extension on something that isn't a decodable image is common; the UI
            // falls back to an icon.
            logger.LogDebug(ex, "Could not generate a thumbnail for {Path}", target.VirtualPath);
            return Result<ThumbnailDto>.NotFound();
        }
        finally
        {
            GenerationLock.Release();
        }
    }

    private async Task<byte[]> GenerateAsync(ResolvedPath target, int size, int quality, CancellationToken cancellationToken)
    {
        string fileName = Path.GetFileName(target.VirtualPath);
        byte[] source = FileKinds.SupportsThumbnail(fileName)
            ? await ReadImageAsync(target, cancellationToken)
            : await ReadContributedFrameAsync(target, fileName, cancellationToken);

        return ImageProcessor.ResizeToFit(
            source,
            size,
            size,
            SKEncodedImageFormat.Webp,
            Math.Clamp(quality, 1, 100),
            ImageResizer.MaxPixels);
    }

    private static async Task<byte[]> ReadImageAsync(ResolvedPath target, CancellationToken cancellationToken)
    {
        await using var content = await target.Storage.OpenReadAsync(target.Location, cancellationToken);

        // One buffer for identify and resize, so this does not depend on the stream being seekable.
        // WebP from Skia is pixels only, so GPS in the original EXIF does not survive into the thumbnail.
        byte[] source = await ImageProcessor.ReadAllAsync(content, cancellationToken);

        if (ImageProcessor.TryIdentify(source) is not (int width, int height))
        {
            throw new InvalidOperationException("The file is not a decodable image.");
        }

        if ((long)width * height > ImageResizer.MaxPixels)
        {
            throw new InvalidOperationException($"{width}x{height} exceeds the decode limit.");
        }

        return source;
    }

    private async Task<byte[]> ReadContributedFrameAsync(ResolvedPath target, string fileName, CancellationToken cancellationToken)
    {
        IThumbnailSource? source = thumbnails.Find(fileName);
        byte[]? frame = source is null ? null : await source.TryReadImageAsync(target, cancellationToken);
        if (frame is null || frame.Length == 0 || ImageProcessor.TryIdentify(frame) is null)
        {
            throw new InvalidOperationException("No thumbnail could be produced.");
        }

        return frame;
    }

    private async Task WriteCacheAsync(string cachePath, byte[] bytes, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            // Written to a temp name and moved, so a concurrent reader never sees half a file.
            string temporary = $"{cachePath}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Caching is an optimisation; failing to cache must not fail the request.
            logger.LogDebug(ex, "Could not cache thumbnail at {Cache}", cachePath);
        }
    }

    /// <summary>
    /// Keyed on the file's identity plus its size and timestamp, so editing a file invalidates
    /// its thumbnail with no bookkeeping anywhere else.
    /// </summary>
    private static string BuildCachePath(ThumbnailOptions settings, ResolvedPath target, int size, StorageEntry info)
    {
        string directory = string.IsNullOrWhiteSpace(settings.CacheDirectory)
            ? Path.Combine(Path.GetTempPath(), "webfilez-thumbnails")
            : settings.CacheDirectory;

        string key = $"{target.Root.Id}|{target.VirtualPath}|{size}|{info.SizeBytes}|{info.ModifiedAt.Ticks}";
        string hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

        return Path.Combine(directory, hex[..2], hex[2..4], $"{hex}.webp");
    }
}
