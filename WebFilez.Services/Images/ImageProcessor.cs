using SkiaSharp;

namespace WebFilez.Services.Images;

/// <summary>
/// Decode and encode for thumbnails, dimension probes, and upload shrinking.
/// Downscales with Mitchell; Skia's default filter is harsher and makes thumbnails look soft.
/// Encoded bytes are pixels only, so a thumbnail cannot carry EXIF such as GPS from the original.
/// </summary>
internal static class ImageProcessor
{
    /// <summary>Mitchell is the cubic kernel that stays closest to the previous Lanczos thumbnails.</summary>
    private static readonly SKSamplingOptions Mitchell = new(SKCubicResampler.Mitchell);

    private static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest, SKMipmapMode.None);

    public static async Task<(int Width, int Height)?> TryIdentifyAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] bytes = await ReadAllAsync(stream, cancellationToken);
        return TryIdentify(bytes);
    }

    public static (int Width, int Height)? TryIdentify(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);

        return codec is null ? null : (codec.Info.Width, codec.Info.Height);
    }

    /// <summary>
    /// Fits the image inside the box, keeping the aspect ratio. A non-positive side is unlimited.
    /// A null <paramref name="format"/> keeps an encodable source container; anything Skia cannot
    /// write back (TIFF, for example) becomes PNG. A smaller image is enlarged until one side hits
    /// the box — callers that must not enlarge check the size first.
    /// </summary>
    public static byte[] ResizeToFit(
        byte[] source,
        int maxWidth,
        int maxHeight,
        SKEncodedImageFormat? format,
        int quality,
        long maxPixels)
    {
        using var bitmap = Decode(source, maxPixels, DecodeBox(maxWidth, maxHeight), out var detected);
        using var fitted = ScaleToFit(bitmap, maxWidth, maxHeight);
        return Encode(fitted, format ?? Encodeable(detected), quality);
    }

    public static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static SKBitmap Decode(byte[] bytes, long maxPixels, SKSizeI? target, out SKEncodedImageFormat format)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new InvalidOperationException("The image could not be decoded.");
        var info = codec.Info;
        format = codec.EncodedFormat;

        if ((long)info.Width * info.Height > maxPixels)
        {
            throw new InvalidOperationException($"{info.Width}x{info.Height} exceeds the decode limit of {maxPixels} pixels.");
        }

        int width = info.Width;
        int height = info.Height;

        // Ask the codec for a smaller frame when the thumbnail is much smaller than the photo,
        // so a multi-megapixel JPEG is not expanded fully just to be thrown away.
        if (target is { } box && (info.Width > box.Width || info.Height > box.Height))
        {
            float scale = Math.Min((float)box.Width / info.Width, (float)box.Height / info.Height);

            if (scale is > 0 and < 1)
            {
                var scaled = codec.GetScaledDimensions(scale);

                if (scaled.Width > 0 && scaled.Height > 0)
                {
                    width = scaled.Width;
                    height = scaled.Height;
                }
            }
        }

        var decodeInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(decodeInfo);
        var result = codec.GetPixels(decodeInfo, bitmap.GetPixels());

        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            throw new InvalidOperationException($"The image could not be decoded ({result}).");
        }

        return bitmap;
    }

    private static SKBitmap ScaleToFit(SKBitmap bitmap, int maxWidth, int maxHeight)
    {
        int boundWidth = maxWidth > 0 ? maxWidth : bitmap.Width;
        int boundHeight = maxHeight > 0 ? maxHeight : bitmap.Height;
        double scale = Math.Min((double)boundWidth / bitmap.Width, (double)boundHeight / bitmap.Height);
        int fittedWidth = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        int fittedHeight = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        var sampling = fittedWidth == bitmap.Width && fittedHeight == bitmap.Height ? Nearest : Mitchell;

        return Resize(bitmap, fittedWidth, fittedHeight, sampling);
    }

    private static SKBitmap Resize(SKBitmap bitmap, int width, int height, SKSamplingOptions sampling)
    {
        if (bitmap.Width == width && bitmap.Height == height)
        {
            return bitmap.Copy() ?? throw new InvalidOperationException("Could not copy the image.");
        }

        var destination = new SKImageInfo(width, height, bitmap.ColorType, bitmap.AlphaType);

        return bitmap.Resize(destination, sampling)
            ?? throw new InvalidOperationException($"Could not resize the image to {width}x{height}.");
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, Math.Clamp(quality, 1, 100))
            ?? throw new InvalidOperationException($"Could not encode {format}.");

        return data.ToArray();
    }

    private static SKSizeI? DecodeBox(int maxWidth, int maxHeight) =>
        maxWidth <= 0 || maxHeight <= 0 ? null : new SKSizeI(maxWidth, maxHeight);

    private static SKEncodedImageFormat Encodeable(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
        SKEncodedImageFormat.Png => SKEncodedImageFormat.Png,
        SKEncodedImageFormat.Gif => SKEncodedImageFormat.Gif,
        SKEncodedImageFormat.Webp => SKEncodedImageFormat.Webp,
        SKEncodedImageFormat.Bmp => SKEncodedImageFormat.Bmp,
        _ => SKEncodedImageFormat.Png,
    };
}