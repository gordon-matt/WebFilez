using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebFilez.Services.Search;
using WebFilez.Services.Storage;
using WebFilez.Services.Transfers;

namespace WebFilez.Services;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the server-side file manager, bound to the <c>WebFilez</c> configuration
        /// section when <paramref name="configuration"/> is supplied.
        /// </summary>
        /// <remarks>
        /// Every extension point is registered with <c>TryAdd</c>, so register your own
        /// <see cref="IFileRootProvider"/>, <see cref="IFileAccessPolicy"/>,
        /// <see cref="IFileActivitySink"/> or <see cref="IFileUrlResolver"/> before or after
        /// calling this and yours wins.
        /// </remarks>
        public IServiceCollection AddWebFilezServices(
            IConfiguration? configuration = null,
            Action<WebFilezOptions>? configure = null)
        {
            var builder = services.AddOptions<WebFilezOptions>();

            if (configuration is not null)
            {
                builder.Bind(configuration.GetSection(WebFilezOptions.SectionName));
            }

            if (configure is not null)
            {
                builder.Configure(configure);
            }

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<DownloadTicketService>();
            services.TryAddSingleton<FileNamePolicy>();
            services.TryAddSingleton<ImageResizer>();

            services.TryAddScoped<IFileRootProvider, OptionsFileRootProvider>();
            services.TryAddScoped<IFileAccessPolicy, RootFileAccessPolicy>();
            services.TryAddScoped<IFileActivitySink, NullFileActivitySink>();
            services.TryAddSingleton<IFileUrlResolver, DefaultFileUrlResolver>();
            services.TryAddSingleton<IFileIndex, MemoryFileIndex>();

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IStorageProvider, FileSystemStorageProvider>());
            services.TryAddSingleton<IStorageProviderSelector, StorageProviderSelector>();

            services.TryAddScoped<PathResolver>();
            services.TryAddScoped<IFileManagerService, FileManagerService>();
            services.TryAddScoped<IFileTransferService, FileTransferService>();
            services.TryAddScoped<IThumbnailService, ThumbnailService>();

            return services;
        }
    }
}