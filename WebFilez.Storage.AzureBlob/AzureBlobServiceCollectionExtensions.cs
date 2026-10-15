using Microsoft.Extensions.DependencyInjection;
using WebFilez.Services.Storage;

namespace WebFilez.Storage.AzureBlob;

public static class AzureBlobServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the Azure Blob Storage provider. A root selects it with
        /// <c>"Provider": "azure-blob"</c> and supplies <c>ConnectionString</c> and
        /// <c>Container</c> in <c>Settings</c>. <c>Path</c>, or <c>Settings.Prefix</c>, is the
        /// folder inside the container.
        /// </summary>
        public IServiceCollection AddWebFilezAzureBlob()
        {
            services.AddSingleton<IStorageProvider, AzureBlobStorageProvider>();
            return services;
        }
    }
}