using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebFilez.Components.Http;

namespace WebFilez;

public static class ComponentsServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Points the components at a WebFilez HTTP API instead of in-process services. Use it in
        /// Blazor WebAssembly, including the custom elements for React and plain HTML.
        /// </summary>
        /// <param name="apiBaseUrl">The API root, such as <c>https://files.example.com/api/webfilez</c>.</param>
        /// <param name="includeCredentials">
        /// Send cookies with downloads, uploads and thumbnails when the API is on another origin.
        /// JSON calls go through <see cref="HttpClient"/>, so add a handler in
        /// <paramref name="configureClient"/> for those.
        /// </param>
        /// <param name="configureClient">For example, to add an authentication handler.</param>
        public IServiceCollection AddWebFilezHttpClient(
            string apiBaseUrl,
            bool includeCredentials = false,
            Action<IHttpClientBuilder>? configureClient = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiBaseUrl);

            // A trailing slash so the relative API routes resolve beneath it rather than beside it.
            string normalized = apiBaseUrl.TrimEnd('/') + "/";
            var baseAddress = new Uri(normalized, UriKind.Absolute);

            var builder = services.AddHttpClient<WebFilezApiClient>(client => client.BaseAddress = baseAddress);
            configureClient?.Invoke(builder);

            services.TryAddScoped<IFileManagerService, HttpFileManagerService>();
            services.TryAddSingleton(new WebFilezApiAddress(normalized, includeCredentials));

            return services;
        }
    }
}