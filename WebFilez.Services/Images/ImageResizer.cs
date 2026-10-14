using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace WebFilez.Services.Images;

/// <summary>Shrinks uploaded images that exceed <see cref="ImageResizeOptions"/>.</summary>
public sealed class ImageResizer(ILogger<ImageResizer> logger, IOptionsMonitor<WebFilezOptions> options)
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
            var info = await Image.IdentifyAsync(physicalPath, cancellationToken);
            if ((long)info.Width * info.Height > MaxPixels)
            {
                return false;
            }

            bool tooWide = settings.MaxWidth > 0 && info.Width > settings.MaxWidth;
            bool tooTall = settings.MaxHeight > 0 && info.Height > settings.MaxHeight;
            if (!tooWide && !tooTall)
            {
                return false;
            }

            using var image = await Image.LoadAsync(physicalPath, cancellationToken);
            var format = image.Metadata.DecodedImageFormat;
            if (format is null)
            {
                return false;
            }

            image.Mutate(context => context.Resize(new ResizeOptions
            {
                Size = new Size(
                    settings.MaxWidth > 0 ? settings.MaxWidth : int.MaxValue,
                    settings.MaxHeight > 0 ? settings.MaxHeight : int.MaxValue),
                Mode = ResizeMode.Max,
                Sampler = KnownResamplers.Lanczos3,
            }));

            string resized = physicalPath + ".resize";
            await image.SaveAsync(resized, image.Configuration.ImageFormatsManager.GetEncoder(format), cancellationToken);
            File.Move(resized, physicalPath, overwrite: true);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not resize uploaded image {Name}", fileName);
            return false;
        }
    }
}
