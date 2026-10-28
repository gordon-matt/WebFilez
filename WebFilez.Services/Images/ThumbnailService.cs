using System.Security.Cryptography;
using System.Text;
using Heroicsoft.NetForge.Drawing;
using Microsoft.Extensions.Options;
using WebFilez.Services.Storage;
using WebFilez.Services.Transfers;

namespace WebFilez.Services.Images;

public interface IThumbnailService
{
    /// <summary>A WebP thumbnail of an image, snapped to the nearest supported size.</summary>
    Task<Result<ThumbnailDto>> GetAsync(string rootId, string path, int size = 256, CancellationToken cancellationToken = default);
}

/// <summary>Generates and disk-caches WebP thumbnails.</summary>
public sealed class ThumbnailService(
    ILogger<ThumbnailService> logger,
    PathResolver resolver,
    IOptionsMonitor<WebFilezOptions> options,
    IImageProcessor images) : IThumbnailService
{
    private const string WebpMimeType = "image/webp";

    /// <summary>
    /// Sizes the UI may ask for. Arbitrary sizes would let one client fill the cache directory
    /// with near-identical files.
    /// </summary>
    internal static readonly int[] AllowedSizes = [128, 256, 512];

    /// <summary>Bounds concurrent decodes, so a grid of a hundred tiles can't pin every core.</summary>
    private static readonly SemaphoreSlim GenerationLock = new(Math.Max(1, Environment.ProcessorCount / 2));

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
        if (info is not { IsDirectory: false } || !FileKinds.SupportsThumbnail(info.Name))
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
        await using var content = await target.Storage.OpenReadAsync(target.Location, cancellationToken);
        var header = await images.TryIdentifyAsync(content, cancellationToken);
        if (header is null)
        {
            throw new InvalidOperationException("The file is not a decodable image.");
        }

        if ((long)header.Width * header.Height > ImageResizer.MaxPixels)
        {
            throw new InvalidOperationException($"{header.Width}x{header.Height} exceeds the decode limit.");
        }

        // EXIF can carry GPS coordinates; a thumbnail has no need to. The processor rewinds a
        // seekable stream, so the identify above does not consume the bytes ResizeAsync reads.
        var processed = await images.ResizeAsync(content, new ImageTransform
        {
            Width = size,
            Height = size,
            Mode = ImageScaleMode.Max,
            Format = RasterFormat.Webp,
            Quality = Math.Clamp(quality, 1, 100),
            StripMetadata = true,
            MaxPixels = ImageResizer.MaxPixels,
        }, cancellationToken);

        return processed.Bytes;
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