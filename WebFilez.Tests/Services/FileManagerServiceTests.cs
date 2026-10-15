using System.IO.Compression;
using System.Text;
using WebFilez.Services.Transfers;
using WebFilez.Tests.Infrastructure;

namespace WebFilez.Tests.Services;

public class FileManagerServiceTests : ServiceTest
{
    [Fact]
    public async Task GetRoots_ReportsPermissions()
    {
        var roots = (await Service.GetRootsAsync(Token)).ShouldSucceed();

        Assert.Equal(["files", "docs"], roots.Select(r => r.Id));
        Assert.False(roots[0].IsReadOnly);
        Assert.True(roots[1].IsReadOnly);
        Assert.False(roots[1].Permissions.HasFlag(FilePermission.Delete));
    }

    [Fact]
    public async Task List_SortsFoldersFirstNaturallyAndHidesDotFiles()
    {
        Files.Write("file10.txt");
        Files.Write("file2.txt");
        Files.Write(".secret");
        Files.CreateDirectory("zeta");

        var listing = (await Service.ListAsync(new ListingRequest("files"), Token)).ShouldSucceed();

        Assert.Equal(["zeta", "file2.txt", "file10.txt"], listing.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task List_ClampsTraversalToTheRoot()
    {
        Files.Write("inside.txt");

        var listing = (await Service.ListAsync(new ListingRequest("files", "/../../.."), Token)).ShouldSucceed();

        Assert.Equal("/", listing.Path);
        Assert.Contains(listing.Items, i => i.Name == "inside.txt");
    }

    [Fact]
    public async Task List_UnknownRootIsNotFound()
    {
        var result = await Service.ListAsync(new ListingRequest("nope"), Token);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task CreateAndRename_UseTheNamePolicy()
    {
        var folder = (await Service.CreateDirectoryAsync(new CreateDirectoryRequest("files", "/", "Reports"), Token)).ShouldSucceed();
        Assert.Equal("/Reports", folder.Path);

        var duplicate = await Service.CreateDirectoryAsync(new CreateDirectoryRequest("files", "/", "reports"), Token);
        Assert.Equal(ResultStatus.Conflict, duplicate.Status);

        var blocked = await Service.CreateFileAsync(new CreateFileRequest("files", "/Reports", "x.php"), Token);
        Assert.Equal(ResultStatus.Invalid, blocked.Status);

        (await Service.CreateFileAsync(new CreateFileRequest("files", "/Reports", "q1.txt"), Token)).ShouldSucceed();
        var renamed = (await Service.RenameAsync(new RenameRequest("files", "/Reports/q1.txt", "Q1 final.txt"), Token)).ShouldSucceed();

        Assert.Equal("/Reports/Q1 final.txt", renamed.Path);
        Assert.True(Files.Exists("Reports/Q1 final.txt"));
        Assert.False(Files.Exists("Reports/q1.txt"));
    }

    [Fact]
    public async Task Rename_CannotSmuggleAPathIntoTheName()
    {
        Files.Write("a.txt");

        var renamed = (await Service.RenameAsync(new RenameRequest("files", "/a.txt", "sub/a.txt"), Token)).ShouldSucceed();

        Assert.Equal("/sub_a.txt", renamed.Path);
        Assert.True(Files.Exists("sub_a.txt"));
    }

    [Fact]
    public async Task Delete_ReportsEachItemAndRefusesTheRoot()
    {
        Files.Write("a.txt");
        Files.Write("dir/b.txt");

        var result = (await Service.DeleteAsync(new DeleteRequest("files", ["/a.txt", "/dir", "/missing", "/"]), Token)).ShouldSucceed();

        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(2, result.FailedCount);
        Assert.False(Files.Exists("a.txt"));
        Assert.False(Files.Exists("dir"));
    }

    [Fact]
    public async Task ReadOnlyRoot_IsForbiddenForChanges()
    {
        Docs.Write("guide.md", "# Guide");

        Assert.Equal(ResultStatus.Forbidden, (await Service.RenameAsync(new RenameRequest("docs", "/guide.md", "x.md"), Token)).Status);
        Assert.Equal(ResultStatus.Forbidden, (await Service.SaveTextAsync(new SaveTextFileRequest("docs", "/guide.md", "x"), Token)).Status);

        var delete = (await Service.DeleteAsync(new DeleteRequest("docs", ["/guide.md"]), Token)).ShouldSucceed();
        Assert.Contains("permission", Assert.Single(delete.Results).Error);

        var text = (await Service.ReadTextAsync("docs", "/guide.md", Token)).ShouldSucceed();
        Assert.Equal("# Guide", text.Content);
        Assert.False(text.CanSave);
        Assert.True(Docs.Exists("guide.md"));
    }

    [Fact]
    public async Task Transfer_CopiesAcrossRootsAndDuplicatesInPlace()
    {
        Docs.Write("guide.md", "# Guide");
        Files.Write("report.pdf", "pdf");
        Files.CreateDirectory("inbox");

        var across = (await Service.TransferAsync(
            new TransferRequest("docs", ["/guide.md"], "files", "/inbox", IsCopy: true), Token)).ShouldSucceed();
        Assert.True(across.IsCompleteSuccess);
        Assert.True(Files.Exists("inbox/guide.md"));

        var duplicate = (await Service.TransferAsync(
            new TransferRequest("files", ["/report.pdf"], "files", "/", IsCopy: true), Token)).ShouldSucceed();
        Assert.True(duplicate.IsCompleteSuccess);
        Assert.True(Files.Exists("report (2).pdf"));
    }

    [Fact]
    public async Task Transfer_MovesAndRefusesToNestAFolderInItself()
    {
        Files.Write("dir/a.txt");
        Files.Write("b.txt");

        var intoSelf = (await Service.TransferAsync(
            new TransferRequest("files", ["/dir"], "files", "/dir", IsCopy: false), Token)).ShouldSucceed();
        Assert.Equal(1, intoSelf.FailedCount);

        var move = (await Service.TransferAsync(
            new TransferRequest("files", ["/b.txt"], "files", "/dir", IsCopy: false), Token)).ShouldSucceed();
        Assert.True(move.IsCompleteSuccess);
        Assert.True(Files.Exists("dir/b.txt"));
        Assert.False(Files.Exists("b.txt"));
    }

    [Fact]
    public async Task Transfer_IntoAReadOnlyRootIsForbidden()
    {
        Files.Write("a.txt");

        var result = await Service.TransferAsync(new TransferRequest("files", ["/a.txt"], "docs", "/", IsCopy: true), Token);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task SaveText_DetectsAConcurrentEdit()
    {
        Files.Write("notes.txt", "one");
        var opened = (await Service.ReadTextAsync("files", "/notes.txt", Token)).ShouldSucceed();

        (await Service.SaveTextAsync(new SaveTextFileRequest("files", "/notes.txt", "two", opened.ModifiedAt), Token)).ShouldSucceed();
        var stale = await Service.SaveTextAsync(new SaveTextFileRequest("files", "/notes.txt", "three", opened.ModifiedAt.AddMinutes(-5)), Token);

        Assert.Equal(ResultStatus.Conflict, stale.Status);
        Assert.Equal("two", File.ReadAllText(Files.Resolve("notes.txt")));
    }

    [Fact]
    public async Task CompressAndExtract_RoundTrip()
    {
        Files.Write("src/a.txt", "alpha");
        Files.Write("src/sub/b.txt", "beta");

        var archive = (await Service.CompressAsync(new CompressRequest("files", ["/src"], "/", "bundle"), Token)).ShouldSucceed();
        Assert.Equal("/bundle.zip", archive.Path);

        Files.CreateDirectory("out");
        var extracted = (await Service.ExtractAsync(new ExtractRequest("files", "/bundle.zip", "/out"), Token)).ShouldSucceed();

        Assert.True(extracted.IsCompleteSuccess);
        Assert.Equal("beta", File.ReadAllText(Files.Resolve("out/src/sub/b.txt")));

        var again = await Service.ExtractAsync(new ExtractRequest("files", "/bundle.zip", "/out"), Token);
        Assert.Equal(ResultStatus.Conflict, again.Status);

        (await Service.ExtractAsync(new ExtractRequest("files", "/bundle.zip", "/out", Overwrite: true), Token)).ShouldSucceed();
    }

    [Fact]
    public async Task Extract_SkipsEntriesThatEscapeOrAreBlocked()
    {
        using (var zip = ZipFile.Open(Files.Resolve("evil.zip"), ZipArchiveMode.Create))
        {
            WriteEntry(zip, "../escaped.txt", "x");
            WriteEntry(zip, "shell.php", "<?php");
            WriteEntry(zip, "fine.txt", "ok");
        }

        Files.CreateDirectory("out");
        var result = (await Service.ExtractAsync(new ExtractRequest("files", "/evil.zip", "/out"), Token)).ShouldSucceed();

        Assert.Equal(1, result.SucceededCount);
        Assert.Contains(result.Results, r => !r.Succeeded && r.Error!.Contains("outside"));
        Assert.Contains(result.Results, r => !r.Succeeded && r.Error!.Contains("not allowed"));
        Assert.True(Files.Exists("out/fine.txt"));
        Assert.False(File.Exists(Path.Combine(Files.Path, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(Files.Path)!, "escaped.txt")));
    }

    [Fact]
    public async Task Upload_CreatesFoldersAndRejectsBlockedTypes()
    {
        var uploaded = (await Transfers.UploadAsync(
            new UploadRequest("files", "/", "a.txt", Stream("hello"), RelativeDirectory: "deep/er"), Token)).ShouldSucceed();
        Assert.Equal("/deep/er/a.txt", uploaded.Item.Path);

        var blocked = await Transfers.UploadAsync(new UploadRequest("files", "/", "shell.php.jpg", Stream("x")), Token);
        Assert.Equal(ResultStatus.Invalid, blocked.Status);

        var conflict = await Transfers.UploadAsync(
            new UploadRequest("files", "/", "a.txt", Stream("again"), RelativeDirectory: "deep/er"), Token);
        Assert.Equal(ResultStatus.Conflict, conflict.Status);

        Assert.Empty(Directory.GetFiles(Files.Path, "*" + Constants.TempFileSuffix, SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DownloadTicket_IsSingleUseAndZipsTheSelection()
    {
        Files.Write("a.txt", "alpha");
        Files.Write("dir/b.txt", "beta");

        var ticket = (await Service.CreateDownloadTicketAsync(new DownloadTicketRequest("files", ["/a.txt", "/dir"]), Token)).ShouldSucceed();
        var redeemed = Transfers.RedeemTicket(ticket.Token).ShouldSucceed();
        Assert.Equal(ResultStatus.NotFound, Transfers.RedeemTicket(ticket.Token).Status);

        using var buffer = new MemoryStream();
        (await Transfers.WriteZipAsync(redeemed, buffer, Token)).ShouldSucceed();

        buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        Assert.Equal(["a.txt", "dir/b.txt"], zip.Entries.Where(e => e.Length > 0).Select(e => e.FullName).Order());
    }

    private static MemoryStream Stream(string content) => new(Encoding.UTF8.GetBytes(content));

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }
}