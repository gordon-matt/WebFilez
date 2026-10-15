using Microsoft.Extensions.DependencyInjection;
using WebFilez.Services.Storage;

namespace WebFilez.Storage.AmazonS3;

public static class AmazonS3ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the Amazon S3 provider. A root selects it with <c>"Provider": "amazon-s3"</c>
        /// and supplies <c>AccessKey</c>, <c>SecretKey</c> and <c>Bucket</c> in <c>Settings</c>.
        /// <c>Region</c> defaults to <c>us-east-1</c>. <c>ServiceUrl</c> points at LocalStack or
        /// another S3-compatible endpoint. <c>Path</c>, or <c>Settings.Prefix</c>, is the folder
        /// inside the bucket. The bucket is not created for you.
        /// </summary>
        public IServiceCollection AddWebFilezAmazonS3()
        {
            services.AddSingleton<IStorageProvider, AmazonS3StorageProvider>();
            return services;
        }
    }
}