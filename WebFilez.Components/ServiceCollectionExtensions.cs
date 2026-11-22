using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebFilez.Components.Http;
using WebFilez.Components.Localization;

namespace WebFilez;

/// <summary>
/// Points the components at an HTTP API and registers the language catalogs.
/// </summary>
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
        /// <returns>The same collection.</returns>
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

            services.TryAddScoped<FileScopeToken>();
            services.TryAddScoped<IFileManagerService, HttpFileManagerService>();
            services.TryAddSingleton(new WebFilezApiAddress(normalized, includeCredentials));
            new WebFilezBuilder(services).AddComponents();

            return services;
        }
    }

    extension<TBuilder>(TBuilder builder) where TBuilder : IWebFilezBuilder
    {
        /// <summary>
        /// Registers the language catalogs. Built-in languages ship with the component. Point
        /// <see cref="WebFilezLocalizationOptions.Directory"/> at a folder of <c>{tag}.json</c>
        /// files to add or override a language. Safe to call before <c>AddWebFilezHttpClient</c>.
        /// A second call does nothing.
        /// </summary>
        /// <param name="configure">Where extra catalogs live. <see langword="null"/> uses the built-in catalogs only.</param>
        /// <returns>The same builder.</returns>
        public TBuilder AddComponents(Action<WebFilezLocalizationOptions>? configure = null)
        {
            if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IWebFilezLocalizer)))
            {
                return builder;
            }

            var options = new WebFilezLocalizationOptions();
            configure?.Invoke(options);
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton<IWebFilezLocalizer, WebFilezLocaleCatalog>();
            return builder;
        }
    }
}