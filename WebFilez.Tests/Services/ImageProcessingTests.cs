using SkiaSharp;
using WebFilez.Services.Transfers;
using WebFilez.Tests.Infrastructure;

namespace WebFilez.Tests.Services;

public class ImageProcessingTests : ServiceTest
{
    protected override void Configure(WebFilezOptions options)
    {
        options.ImageResize.Enabled = true;
        options.ImageResize.MaxWidth = 8;
        options.ImageResize.MaxHeight = 8;
    }

    [Fact]
    public async Task GetDetailAsync_Png_ReportsDimensions()
    {
        WritePng("tiny.png", 3, 2);

        var detail = (await Service.GetDetailAsync("files", "/tiny.png", Token)).ShouldSucceed();

        Assert.Equal(3, detail.Width);
        Assert.Equal(2, detail.Height);
    }

    [Fact]
    public async Task GetDetailAsync_UndecodableImage_OmitsDimensions()
    {
        Files.Write("notes.png", "not an image");

        var detail = (await Service.GetDetailAsync("files", "/notes.png", Token)).ShouldSucceed();

        Assert.Null(detail.Width);
        Assert.Null(detail.Height);
    }

    [Fact]
    public async Task GetAsync_Png_ReturnsWebpFittedToTheBox()
    {
        WritePng("wide.png", 40, 20);

        var thumbnail = (await Thumbnails.GetAsync("files", "/wide.png", 128, Token)).ShouldSucceed();

        Assert.Equal("image/webp", thumbnail.MimeType);
        var size = Identify(thumbnail.Bytes);
        Assert.Equal(SKEncodedImageFormat.Webp, size.Format);
        Assert.Equal(128, size.Width);
        Assert.Equal(64, size.Height);
    }

    [Fact]
    public async Task GetAsync_UndecodableImage_IsNotFound()
    {
        Files.Write("broken.jpg", "not a jpeg");

        var result = await Thumbnails.GetAsync("files", "/broken.jpg", cancellationToken: Token);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task UploadAsync_ImageLargerThanTheLimit_ShrinksItAndKeepsPng()
    {
        await using var content = new MemoryStream(Png(32, 16));

        var uploaded = (await Transfers.UploadAsync(new UploadRequest("files", "/", "photo.png", content), Token)).ShouldSucceed();

        Assert.True(uploaded.WasResized);
        var detail = (await Service.GetDetailAsync("files", uploaded.Item.Path, Token)).ShouldSucceed();
        Assert.Equal(8, detail.Width);
        Assert.Equal(4, detail.Height);

        byte[] saved = await File.ReadAllBytesAsync(Files.Resolve("photo.png"), Token);
        Assert.Equal(SKEncodedImageFormat.Png, Identify(saved).Format);
    }

    [Fact]
    public async Task UploadAsync_ImageWithinTheLimit_LeavesIt()
    {
        await using var content = new MemoryStream(Png(4, 4));

        var uploaded = (await Transfers.UploadAsync(new UploadRequest("files", "/", "small.png", content), Token)).ShouldSucceed();

        Assert.False(uploaded.WasResized);
        var detail = (await Service.GetDetailAsync("files", uploaded.Item.Path, Token)).ShouldSucceed();
        Assert.Equal(4, detail.Width);
        Assert.Equal(4, detail.Height);
    }

    private void WritePng(string name, int width, int height) => File.WriteAllBytes(Files.Resolve(name), Png(width, height));

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        bitmap.Erase(new SKColor(220, 40, 40));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("Could not encode the test image.");
        return encoded.ToArray();
    }

    private static (int Width, int Height, SKEncodedImageFormat Format) Identify(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        Assert.NotNull(codec);
        return (codec.Info.Width, codec.Info.Height, codec.EncodedFormat);
    }
}
