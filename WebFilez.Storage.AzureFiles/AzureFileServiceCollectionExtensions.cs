using Microsoft.Extensions.DependencyInjection;
using WebFilez.Services.Storage;

namespace WebFilez.Storage.AzureFiles;

public static class AzureFileServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the Azure Files provider. A root selects it with <c>"Provider": "azure-file"</c>
        /// and supplies <c>ConnectionString</c> and <c>Share</c> in <c>Settings</c>. <c>Path</c>, or
        /// <c>Settings.Prefix</c>, is the directory inside the share. The share is not created.
        /// </summary>
        public IServiceCollection AddWebFilezAzureFiles()
        {
            services.AddSingleton<IStorageProvider, AzureFileStorageProvider>();
            return services;
        }
    }
}