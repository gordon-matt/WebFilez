using Microsoft.Extensions.Options;

namespace WebFilez.Services.Images;

/// <summary>Shrinks uploaded images that exceed <see cref="ImageResizeOptions"/>.</summary>
public sealed class ImageResizer(
    ILogger<ImageResizer> logger,
    IOptionsMonitor<WebFilezOptions> options)
{
    /// <summary>
    /// Refuses to decode images beyond this many pixels. Decoding cost follows pixel count, not
    /// file size, so a small file with enormous dimensions (a decompression bomb) is the risk.
    /// </summary>
    internal const long MaxPixels = 100_000_000;

    /// <summary>JPEG and WebP quality the shrink path has always written.</summary>
    private const int ShrinkQuality = 75;

    /// <summary>
    /// Resizes the image at <paramref name="physicalPath"/> in place when it is larger than the
    /// configured maximum. Returns whether it changed. Files that aren't decodable images are
    /// left alone.
    /// </summary>
    public async Task<bool> TryShrinkAsync(string physicalPath, string fileName, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue.ImageResize;
        if (!settings.Enabled
            || (settings.MaxWidth <= 0 && settings.MaxHeight <= 0)
            || !FileKinds.SupportsThumbnail(fileName)
            || !File.Exists(physicalPath))
        {
            return false;
        }

        try
        {
            byte[] source = await File.ReadAllBytesAsync(physicalPath, cancellationToken);

            if (ImageProcessor.TryIdentify(source) is not (int width, int height))
            {
                return false;
            }

            if ((long)width * height > MaxPixels)
            {
                return false;
            }

            bool tooWide = settings.MaxWidth > 0 && width > settings.MaxWidth;
            bool tooTall = settings.MaxHeight > 0 && height > settings.MaxHeight;

            if (!tooWide && !tooTall)
            {
                return false;
            }

            byte[] resized = ImageProcessor.ResizeToFit(source, settings.MaxWidth, settings.MaxHeight, format: null, ShrinkQuality, MaxPixels);

            // Written beside the original and moved over it, so a failed encode never truncates the upload.
            string temporary = physicalPath + ".resize";
            await File.WriteAllBytesAsync(temporary, resized, cancellationToken);
            File.Move(temporary, physicalPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not resize uploaded image {Name}", fileName);
            return false;
        }
    }
}