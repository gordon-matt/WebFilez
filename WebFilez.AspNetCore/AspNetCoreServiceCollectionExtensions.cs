using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WebFilez.Services.Storage;

namespace WebFilez;

public static class AspNetCoreServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers WebFilez and what its HTTP API needs. Relative root paths are resolved
        /// against the content root. Follow with <c>app.MapWebFilezApi()</c>.
        /// </summary>
        public IServiceCollection AddWebFilez(
            IConfiguration? configuration = null,
            Action<WebFilezOptions>? configure = null)
        {
            services.AddWebFilezServices(configuration, configure);
            services.AddProblemDetails();

            services.AddOptions<WebFilezOptions>().PostConfigure<IHostEnvironment>((options, environment) =>
            {
                foreach (var root in options.Roots)
                {
                    // Only the file system provider means Path as a directory. A blob prefix such
                    // as "photos" must not be rewritten into a folder under the content root.
                    if (FileSystemStorageProvider.IsFileSystem(root.Provider)
                        && !string.IsNullOrWhiteSpace(root.Path)
                        && !Path.IsPathRooted(root.Path))
                    {
                        root.Path = Path.GetFullPath(Path.Combine(environment.ContentRootPath, root.Path));
                    }
                }
            });

            // Components running in-process (Blazor Server, MVC) build download, upload and
            // thumbnail URLs from this; relative, so it follows the app's path base.
            services.TryAddSingleton(provider => new WebFilezApiAddress(
                provider.GetRequiredService<IOptions<WebFilezOptions>>().Value.ApiPrefix.Trim('/') + "/"));

            return services;
        }
    }
}