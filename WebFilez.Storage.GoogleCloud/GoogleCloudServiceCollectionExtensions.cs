using Microsoft.Extensions.DependencyInjection;
using WebFilez.Services.Storage;

namespace WebFilez.Storage.GoogleCloud;

/// <summary>
/// Registers the Google Cloud Storage provider.
/// </summary>
public static class GoogleCloudServiceCollectionExtensions
{
    extension<TBuilder>(TBuilder builder) where TBuilder : IWebFilezBuilder
    {
        /// <summary>
        /// Registers the Google Cloud Storage provider. A root selects it with
        /// <c>"Provider": "google-cloud"</c> and supplies <c>Bucket</c> in <c>Settings</c>.
        /// Credentials come from <c>CredentialJson</c>, <c>CredentialFile</c>, or Application
        /// Default Credentials. <c>ServiceUrl</c> points at fake-gcs-server or another JSON API
        /// endpoint, and skips credentials when none are set. <c>Path</c>, or
        /// <c>Settings.Prefix</c>, is the folder inside the bucket. The bucket is not created for you.
        /// Calling this again registers another provider.
        /// </summary>
        /// <returns>The same builder.</returns>
        public TBuilder AddGoogleCloud()
        {
            builder.Services.AddSingleton<IStorageProvider, GoogleCloudStorageProvider>();
            builder.Services.AddSingleton<IStoragePublicUrl, GoogleCloudPublicUrl>();
            return builder;
        }
    }
}