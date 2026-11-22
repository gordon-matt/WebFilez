using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebFilez.Services.Search;
using WebFilez.Services.Storage;
using WebFilez.Services.Transfers;

namespace WebFilez.Services;

/// <summary>Registers the file manager services.</summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the server-side file manager, bound to the <c>WebFilez</c> configuration
        /// section when <paramref name="configuration"/> is supplied.
        /// </summary>
        /// <remarks>
        /// The directory, access policy, activity sink, URL resolver, and index are registered with
        /// <c>TryAdd</c>. Register your own before this call; a registration afterwards does not replace them.
        /// Thumbnail sources are additive. Register one to thumbnail files the built-in decoder cannot read.
        /// None are registered here. A second call does not register the same services twice.
        /// </remarks>
        /// <param name="configuration">The application configuration. The <c>WebFilez</c> section is bound when this is set.</param>
        /// <param name="configure">Further changes applied after the section is bound.</param>
        /// <returns>The same collection, so storage packages can be chained.</returns>
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
            services.TryAddScoped<FileScope>();
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