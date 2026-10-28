using Heroicsoft.NetForge.Drawing;
using Microsoft.Extensions.Options;

namespace WebFilez.Services.Images;

/// <summary>Shrinks uploaded images that exceed <see cref="ImageResizeOptions"/>.</summary>
public sealed class ImageResizer(
    ILogger<ImageResizer> logger,
    IOptionsMonitor<WebFilezOptions> options,
    IImageProcessor imageProcessor)
{
    /// <summary>
    /// Refuses to decode images beyond this many pixels. Decoding cost follows pixel count, not
    /// file size, so a small file with enormous dimensions (a decompression bomb) is the risk.
    /// </summary>
    internal const long MaxPixels = 100_000_000;

    /// <summary>
    /// Resizes the image at <paramref name="physicalPath"/> in place when it is larger than the
    /// configured maximum. Returns whether it changed. Files that aren't decodable images are
    /// left alone.
    /// </summary>
    public async Task<bool> TryShrinkAsync(string physicalPath, string fileName, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue.ImageResize;
        if (!settings.Enabled || (settings.MaxWidth <= 0 && settings.MaxHeight <= 0) || !FileKinds.SupportsThumbnail(fileName))
        {
            return false;
        }

        try
        {
            return await imageProcessor.TryShrinkFileAsync(physicalPath, new ImageShrinkOptions
            {
                MaxWidth = settings.MaxWidth,
                MaxHeight = settings.MaxHeight,
                MaxPixels = MaxPixels,
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not resize uploaded image {Name}", fileName);
            return false;
        }
    }
}
