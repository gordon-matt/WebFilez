using Microsoft.Extensions.DependencyInjection;
using WebFilez.Plugins.Video;
using WebFilez.Services.Images;
using WebFilez.Services.Storage;
using WebFilez.Storage.AmazonS3;
using WebFilez.Storage.AzureBlob;
using WebFilez.Storage.AzureFiles;
using WebFilez.Storage.GoogleCloud;

namespace WebFilez.Tests.Registration;

public class WebFilezBuilderTests
{
    [Fact]
    public void AddWebFilez_ChainsOptionalPackagesOntoTheSameCollection()
    {
        var services = new ServiceCollection();

        var builder = services
            .AddWebFilez()
            .AddVideo()
            .AddComponents()
            .AddAzureBlob()
            .AddAzureFiles()
            .AddAmazonS3()
            .AddGoogleCloud();

        Assert.Same(services, builder.Services);

        using var provider = services.BuildServiceProvider();
        Assert.Contains(provider.GetServices<IStorageProvider>(), provider => provider.Key == AzureBlobStorageProvider.ProviderKey);
        Assert.Contains(provider.GetServices<IStorageProvider>(), provider => provider.Key == AzureFileStorageProvider.ProviderKey);
        Assert.Contains(provider.GetServices<IStorageProvider>(), provider => provider.Key == AmazonS3StorageProvider.ProviderKey);
        Assert.Contains(provider.GetServices<IStorageProvider>(), provider => provider.Key == GoogleCloudStorageProvider.ProviderKey);
        Assert.Contains(provider.GetServices<IThumbnailSource>(), source => source is VideoThumbnailSource);
    }
}