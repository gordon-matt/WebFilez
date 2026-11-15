using WebFilez.Tests.Infrastructure;

namespace WebFilez.Tests.Services;

public class PathRuleTests : ServiceTest
{
    protected override void Configure(WebFilezOptions options)
    {
        options.Roots[0].Rules =
        [
            new() { Path = "/secret", Read = false, Write = false, Locked = true },
            new() { Path = "/locked.txt", Write = false, Locked = true },
            new() { Path = "/Parent/Children", Write = false, Locked = true },
        ];
    }

    [Fact]
    public async Task HiddenFolder_IsNotListedOrOpened()
    {
        Files.Write("secret/note.txt", "x");
        Files.Write("visible.txt", "y");

        var listing = (await Service.ListAsync(new ListingRequest("files"), Token)).ShouldSucceed();

        Assert.Contains(listing.Items, item => item.Name == "visible.txt");
        Assert.DoesNotContain(listing.Items, item => item.Name == "secret");
        Assert.Equal(ResultStatus.NotFound, (await Service.ListAsync(new ListingRequest("files", "/secret"), Token)).Status);
        Assert.Equal(ResultStatus.NotFound, (await Service.ReadTextAsync("files", "/secret/note.txt", Token)).Status);
    }

    [Fact]
    public async Task LockedFile_CanBeReadButNotRenamedOrDeleted()
    {
        Files.Write("locked.txt", "keep");

        var text = (await Service.ReadTextAsync("files", "/locked.txt", Token)).ShouldSucceed();
        Assert.Equal("keep", text.Content);
        Assert.False(text.CanSave);

        var renamed = await Service.RenameAsync(new RenameRequest("files", "/locked.txt", "other.txt"), Token);
        Assert.Equal(ResultStatus.Forbidden, renamed.Status);

        var deleted = (await Service.DeleteAsync(new DeleteRequest("files", ["/locked.txt"]), Token)).ShouldSucceed();
        Assert.False(deleted.Results[0].Succeeded);
        Assert.Equal("keep", await File.ReadAllTextAsync(Files.Path + "/locked.txt", Token));
    }

    [Fact]
    public async Task LockedFolder_AppliesToEverythingUnderIt()
    {
        Files.Write("Parent/Children/a.txt", "a");
        Files.Write("Parent/free.txt", "free");

        var child = await Service.RenameAsync(new RenameRequest("files", "/Parent/Children/a.txt", "b.txt"), Token);
        Assert.Equal(ResultStatus.Forbidden, child.Status);

        var sibling = (await Service.RenameAsync(new RenameRequest("files", "/Parent/free.txt", "open.txt"), Token)).ShouldSucceed();
        Assert.Equal("/Parent/open.txt", sibling.Path);
    }
}
