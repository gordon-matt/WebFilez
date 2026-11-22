using Microsoft.Extensions.DependencyInjection;
using WebFilez.Services.Storage;

namespace WebFilez.Storage.AzureBlob;

/// <summary>
/// Registers the Azure Blob Storage provider.
/// </summary>
public static class AzureBlobServiceCollectionExtensions
{
    extension<TBuilder>(TBuilder builder) where TBuilder : IWebFilezBuilder
    {
        /// <summary>
        /// Registers the Azure Blob Storage provider. A root selects it with
        /// <c>"Provider": "azure-blob"</c> and supplies <c>ConnectionString</c> and
        /// <c>Container</c> in <c>Settings</c>. <c>Path</c>, or <c>Settings.Prefix</c>, is the
        /// folder inside the container. Calling this again registers another provider.
        /// </summary>
        /// <returns>The same builder.</returns>
        public TBuilder AddAzureBlob()
        {
            builder.Services.AddSingleton<IStorageProvider, AzureBlobStorageProvider>();
            builder.Services.AddSingleton<IStoragePublicUrl, AzureBlobPublicUrl>();
            return builder;
        }
    }
}