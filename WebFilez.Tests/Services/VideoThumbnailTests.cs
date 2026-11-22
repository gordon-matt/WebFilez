using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using WebFilez.Plugins.Video;
using WebFilez.Services.FileSystem;
using WebFilez.Services.Images;
using WebFilez.Tests.Infrastructure;

namespace WebFilez.Tests.Services;

public class VideoThumbnailTests : ServiceTest
{
    [Fact]
    public async Task ListAsync_VideoWithoutASource_HasNoThumbnail()
    {
        Files.Write("clip.mp4", "not a video");

        var listing = (await Service.ListAsync(new ListingRequest("files"), Token)).ShouldSucceed();

        FileSystemItemDto clip = Assert.Single(listing.Items, item => item.Name == "clip.mp4");
        Assert.False(clip.HasThumbnail);
        Assert.Equal(ResultStatus.NotFound, (await Thumbnails.GetAsync("files", "/clip.mp4", cancellationToken: Token)).Status);
    }

    [Fact]
    public void BuildCaptureArguments_SeeksBeforeTheInputAndLimitsTheFrame()
    {
        string arguments = VideoThumbnailSource.BuildCaptureArguments(
            @"C:\clips\my file.mp4",
            @"C:\temp\frame.jpg",
            TimeSpan.FromSeconds(12.5));

        int seek = arguments.IndexOf("-ss 12.5", StringComparison.Ordinal);
        int input = arguments.IndexOf("-i \"C:\\clips\\my file.mp4\"", StringComparison.Ordinal);

        Assert.True(seek >= 0);
        Assert.True(input > seek);
        Assert.Contains("-protocol_whitelist file", arguments, StringComparison.Ordinal);
        Assert.Contains("scale=512:512:force_original_aspect_ratio=decrease", arguments, StringComparison.Ordinal);
    }
}

public class ContributedVideoThumbnailTests : ServiceTest
{
    protected override void Register(IServiceCollection services) =>
        services.AddSingleton<IThumbnailSource>(new FixedFrameSource());

    [Fact]
    public async Task ListAsync_VideoWithASource_OffersAWebpThumbnail()
    {
        Files.Write("clip.mp4", "not a video");
        Files.Write("notes.txt", "hello");

        var listing = (await Service.ListAsync(new ListingRequest("files"), Token)).ShouldSucceed();

        Assert.True(Assert.Single(listing.Items, item => item.Name == "clip.mp4").HasThumbnail);
        Assert.False(Assert.Single(listing.Items, item => item.Name == "notes.txt").HasThumbnail);

        var thumbnail = (await Thumbnails.GetAsync("files", "/clip.mp4", 128, Token)).ShouldSucceed();
        Assert.Equal("image/webp", thumbnail.MimeType);
        using SKData data = SKData.CreateCopy(thumbnail.Bytes);
        using SKCodec codec = SKCodec.Create(data);
        Assert.NotNull(codec);
        Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
        Assert.Equal(128, codec.Info.Width);
        Assert.Equal(64, codec.Info.Height);
    }

    /// <summary>Stands in for ffmpeg: a video extension becomes a known PNG frame.</summary>
    private sealed class FixedFrameSource : IThumbnailSource
    {
        public bool Supports(string fileName) =>
            Path.GetExtension(fileName).Equals(".mp4", StringComparison.OrdinalIgnoreCase);

        public Task<byte[]?> TryReadImageAsync(ResolvedPath target, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(Frame());

        private static byte[] Frame()
        {
            using var bitmap = new SKBitmap(new SKImageInfo(40, 20, SKColorType.Rgba8888, SKAlphaType.Opaque));
            bitmap.Erase(new SKColor(20, 80, 200));
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("Could not encode the test frame.");
            return encoded.ToArray();
        }
    }
}
