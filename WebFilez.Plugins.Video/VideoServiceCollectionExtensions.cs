using Microsoft.Extensions.DependencyInjection;
using WebFilez;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using WebFilez.Services.Images;

namespace WebFilez.Plugins.Video;

/// <summary>
/// Provides extension methods for registering video thumbnails.
/// </summary>
public static class VideoServiceCollectionExtensions
{
    extension(WebFilezBuilder builder)
    {
        /// <summary>
        /// Thumbnails <c>.mp4</c>, <c>.webm</c>, <c>.mkv</c>, <c>.mov</c> and the other common
        /// video types by grabbing a frame from the middle of the file. Chain it from
        /// <c>AddWebFilez</c>. Leave it out and WebFilez thumbnails images only.
        /// </summary>
        /// <remarks>
        /// ffmpeg is located on the machine. Nothing is downloaded unless
        /// <see cref="VideoThumbnailOptions.DownloadIfMissing"/> is set, because the official
        /// build is GPL-licensed. A missing ffmpeg does not throw and does not stop the host;
        /// video files keep their normal icon.
        /// </remarks>
        public WebFilezBuilder AddVideo(Action<VideoThumbnailOptions>? configure = null)
        {
            builder.Services.AddOptions<VideoThumbnailOptions>();
            if (configure is not null)
            {
                builder.Services.Configure(configure);
            }

            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IThumbnailSource, VideoThumbnailSource>());
            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, VideoThumbnailStartup>());
            return builder;
        }
    }
}