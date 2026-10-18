using WebFilez.Tests.Infrastructure;

namespace WebFilez.Tests.Services;

public class SearchTests : ServiceTest
{
    [Fact]
    public async Task Search_FindsNamesAcrossARootTreeAndEveryRoot()
    {
        Files.Write("notes/quarter.txt", "q1");
        Files.Write("notes/draft.md");
        Files.Write("photo.png", "img");
        Docs.Write("quarter-plan.md", "plan");

        var inRoot = (await Service.SearchAsync(new SearchRequest("quarter", "files"), Token)).ShouldSucceed();

        Assert.Equal(["/notes/quarter.txt"], inRoot.Hits.Select(hit => hit.Path));
        Assert.Equal("files", Assert.Single(inRoot.Hits).RootId);
        Assert.Contains("notes", Assert.Single(inRoot.Hits).Location);

        var everywhere = (await Service.SearchAsync(new SearchRequest("quarter"), Token)).ShouldSucceed();

        Assert.Equal(2, everywhere.Hits.Count);
        Assert.Contains(everywhere.Hits, hit => hit.RootId == "docs" && hit.Path == "/quarter-plan.md");
    }

    [Fact]
    public async Task Search_MatchesExtensionAndKind()
    {
        Files.Write("report.PDF", "pdf");
        Files.Write("song.mp3", "audio");
        Files.Write("readme.txt");

        var byExtension = (await Service.SearchAsync(new SearchRequest("pdf", "files"), Token)).ShouldSucceed();
        Assert.Equal("/report.PDF", Assert.Single(byExtension.Hits).Path);

        var images = (await Service.SearchAsync(new SearchRequest("report", "files", Kind: KindFilter.Images), Token)).ShouldSucceed();
        Assert.Empty(images.Hits);

        var media = (await Service.SearchAsync(new SearchRequest("song", "files", Kind: KindFilter.Media), Token)).ShouldSucceed();
        Assert.Equal(FileKind.Audio, Assert.Single(media.Hits).Kind);
    }

    [Fact]
    public async Task Search_CanBeLimitedToAFolder()
    {
        Files.Write("keep/notes.txt");
        Files.Write("drop/notes.txt");

        var hits = (await Service.SearchAsync(new SearchRequest("notes", "files", "/keep"), Token)).ShouldSucceed();

        Assert.Equal("/keep/notes.txt", Assert.Single(hits.Hits).Path);
    }

    [Fact]
    public async Task Search_FollowsCreateRenameAndDelete()
    {
        Files.Write("seed.txt");
        (await Service.SearchAsync(new SearchRequest("seed", "files"), Token)).ShouldSucceed();

        (await Service.CreateFileAsync(new CreateFileRequest("files", "/", "alpha.txt"), Token)).ShouldSucceed();
        var created = (await Service.SearchAsync(new SearchRequest("alpha", "files"), Token)).ShouldSucceed();
        Assert.Equal("/alpha.txt", Assert.Single(created.Hits).Path);

        (await Service.RenameAsync(new RenameRequest("files", "/alpha.txt", "beta.txt"), Token)).ShouldSucceed();
        var renamed = (await Service.SearchAsync(new SearchRequest("beta", "files"), Token)).ShouldSucceed();
        Assert.Equal("/beta.txt", Assert.Single(renamed.Hits).Path);
        Assert.Empty((await Service.SearchAsync(new SearchRequest("alpha", "files"), Token)).ShouldSucceed().Hits);

        (await Service.DeleteAsync(new DeleteRequest("files", ["/beta.txt"]), Token)).ShouldSucceed();
        Assert.Empty((await Service.SearchAsync(new SearchRequest("beta", "files"), Token)).ShouldSucceed().Hits);
    }

    [Fact]
    public async Task Search_HidesDotFilesUnlessTheRootAllowsThem()
    {
        Files.Write(".secret");
        Files.Write("visible.txt");

        var hidden = (await Service.SearchAsync(new SearchRequest("secret", "files", ShowHidden: true), Token)).ShouldSucceed();
        Assert.Empty(hidden.Hits);

        var visible = (await Service.SearchAsync(new SearchRequest("visible", "files"), Token)).ShouldSucceed();
        Assert.Single(visible.Hits);
    }

    [Fact]
    public async Task Search_RejectsAnEmptyQuery()
    {
        var result = await Service.SearchAsync(new SearchRequest("  "), Token);

        Assert.Equal(ResultStatus.Invalid, result.Status);
    }
}
