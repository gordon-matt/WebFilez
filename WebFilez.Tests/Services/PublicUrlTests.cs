using WebFilez.Tests.Infrastructure;

namespace WebFilez.Tests.Services;

public class PublicUrlTests : ServiceTest
{
    protected override void Configure(WebFilezOptions options)
    {
        options.Roots[0].PublicUrl = "/media";
    }

    [Fact]
    public async Task List_PublishesFolderAndFileAddresses()
    {
        Files.Write("Photo One.png");
        Files.CreateDirectory("Album");

        var listing = (await Service.ListAsync(new ListingRequest("files"), Token)).ShouldSucceed();

        Assert.Equal("/media/", listing.PublicUrl);
        Assert.Equal("/media/Album/", listing.Items.Single(i => i.Name == "Album").PublicUrl);
        Assert.Equal("/media/Photo%20One.png", listing.Items.Single(i => i.Name == "Photo One.png").PublicUrl);
    }

    [Fact]
    public async Task List_ImagesKeepsFoldersAndDropsOtherFiles()
    {
        Files.Write("photo.png");
        Files.Write("notes.txt");
        Files.CreateDirectory("Album");

        var listing = (await Service.ListAsync(new ListingRequest("files", Kind: KindFilter.Images), Token)).ShouldSucceed();

        Assert.Equal(["Album", "photo.png"], listing.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task List_FoldersHidesFiles()
    {
        Files.Write("photo.png");
        Files.CreateDirectory("Album");

        var listing = (await Service.ListAsync(new ListingRequest("files", Kind: KindFilter.Folders), Token)).ShouldSucceed();

        Assert.Equal(["Album"], listing.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task GetDetail_PublishesAFolderAddress()
    {
        Files.CreateDirectory("Album");

        var detail = (await Service.GetDetailAsync("files", "/Album", Token)).ShouldSucceed();

        Assert.True(detail.IsDirectory);
        Assert.Equal("/media/Album/", detail.PublicUrl);
    }
}
