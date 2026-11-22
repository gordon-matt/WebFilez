using Microsoft.Extensions.DependencyInjection;
using WebFilez.Services.Storage;

namespace WebFilez.Storage.AzureFiles;

/// <summary>
/// Registers the Azure Files provider.
/// </summary>
public static class AzureFileServiceCollectionExtensions
{
    extension<TBuilder>(TBuilder builder) where TBuilder : IWebFilezBuilder
    {
        /// <summary>
        /// Registers the Azure Files provider. A root selects it with <c>"Provider": "azure-file"</c>
        /// and supplies <c>ConnectionString</c> and <c>Share</c> in <c>Settings</c>. <c>Path</c>, or
        /// <c>Settings.Prefix</c>, is the directory inside the share. The share is not created.
        /// Calling this again registers another provider.
        /// </summary>
        /// <returns>The same builder.</returns>
        public TBuilder AddAzureFiles()
        {
            builder.Services.AddSingleton<IStorageProvider, AzureFileStorageProvider>();
            builder.Services.AddSingleton<IStoragePublicUrl, AzureFilePublicUrl>();
            return builder;
        }
    }
}