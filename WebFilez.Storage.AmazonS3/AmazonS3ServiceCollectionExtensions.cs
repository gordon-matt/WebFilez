using Microsoft.Extensions.DependencyInjection;
using WebFilez.Services.Storage;

namespace WebFilez.Storage.AmazonS3;

/// <summary>
/// Registers the Amazon S3 storage provider.
/// </summary>
public static class AmazonS3ServiceCollectionExtensions
{
    extension<TBuilder>(TBuilder builder) where TBuilder : IWebFilezBuilder
    {
        /// <summary>
        /// Registers the Amazon S3 provider. A root selects it with <c>"Provider": "amazon-s3"</c>
        /// and supplies <c>AccessKey</c>, <c>SecretKey</c> and <c>Bucket</c> in <c>Settings</c>.
        /// <c>Region</c> defaults to <c>us-east-1</c>. <c>ServiceUrl</c> points at LocalStack or
        /// another S3-compatible endpoint. <c>Path</c>, or <c>Settings.Prefix</c>, is the folder
        /// inside the bucket. The bucket is not created for you. Calling this again registers another provider.
        /// </summary>
        /// <returns>The same builder.</returns>
        public TBuilder AddAmazonS3()
        {
            builder.Services.AddSingleton<IStorageProvider, AmazonS3StorageProvider>();
            builder.Services.AddSingleton<IStoragePublicUrl, AmazonS3PublicUrl>();
            return builder;
        }
    }
}