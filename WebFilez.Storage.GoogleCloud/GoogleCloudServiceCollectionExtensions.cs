using Microsoft.Extensions.DependencyInjection;
using WebFilez.Services.Storage;

namespace WebFilez.Storage.GoogleCloud;

public static class GoogleCloudServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the Google Cloud Storage provider. A root selects it with
        /// <c>"Provider": "google-cloud"</c> and supplies <c>Bucket</c> in <c>Settings</c>.
        /// Credentials come from <c>CredentialJson</c>, <c>CredentialFile</c>, or Application
        /// Default Credentials. <c>ServiceUrl</c> points at fake-gcs-server or another JSON API
        /// endpoint, and skips credentials when none are set. <c>Path</c>, or
        /// <c>Settings.Prefix</c>, is the folder inside the bucket. The bucket is not created for you.
        /// </summary>
        public IServiceCollection AddWebFilezGoogleCloud()
        {
            services.AddSingleton<IStorageProvider, GoogleCloudStorageProvider>();
            return services;
        }
    }
}