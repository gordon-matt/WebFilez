using WebFilez.Components.History;
using WebFilez.Tests.Infrastructure;

namespace WebFilez.Tests.Components;

public class NavigationHistoryTests
{
    [Fact]
    public void BackAndForward_WalkTheFoldersVisited()
    {
        var history = new NavigationHistory();
        history.Reset(Place(0));
        history.Visit(Place(0), Place(1));
        history.Visit(Place(1), Place(2));

        Assert.Equal("/f1", history.Back()?.Path);
        Assert.True(history.CanForward);
        Assert.Equal("/f2", history.Forward()?.Path);
        Assert.False(history.CanForward);
    }

    [Fact]
    public void Visit_SamePlace_DoesNotEnableBack()
    {
        var history = new NavigationHistory();
        history.Reset(Place(0));

        history.Visit(Place(0), Place(0));

        Assert.False(history.CanBack);
    }

    [Fact]
    public void Visit_AfterBack_DropsTheForwardStack()
    {
        var history = new NavigationHistory();
        history.Reset(Place(0));
        history.Visit(Place(0), Place(1));
        history.Visit(Place(1), Place(2));
        history.Back();

        history.Visit(Place(1), Place(3));

        Assert.False(history.CanForward);
        Assert.Equal("/f1", history.Back()?.Path);
        Assert.Equal("/f0", history.Back()?.Path);
    }

    [Fact]
    public void Visit_DropsTheOldestPlacePastTheLimit()
    {
        var history = new NavigationHistory();
        history.Reset(Place(0));
        for (int i = 1; i <= NavigationHistory.Limit + 1; i++)
        {
            history.Visit(Place(i - 1), Place(i));
        }

        BrowsePlace? oldest = null;
        int steps = 0;
        while (history.Back() is { } place)
        {
            oldest = place;
            steps++;
        }

        Assert.Equal(NavigationHistory.Limit, steps);
        Assert.Equal("/f1", oldest?.Path);
    }

    private static BrowsePlace Place(int n) => new("files", $"/f{n}");
}

public class EditHistoryTests : ServiceTest
{
    [Fact]
    public void Push_ClearsRedo_AndDropsTheOldest()
    {
        var history = new EditHistory();
        history.Push(new Marker(1));
        history.TryUndo();
        Assert.True(history.CanRedo);

        history.Push(new Marker(2));

        Assert.False(history.CanRedo);
        Assert.Equal(2, ((Marker)history.TryUndo()!).Id);
    }

    [Fact]
    public void RevertUndo_PutsTheFailedStepBack()
    {
        var history = new EditHistory();
        history.Push(new Marker(1));
        history.TryUndo();

        history.RevertUndo();

        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal(1, ((Marker)history.TryUndo()!).Id);
    }

    [Fact]
    public async Task CreateFolder_UndoRemovesAnEmptyFolder_AndRedoRestoresIt()
    {
        var created = (await Service.CreateDirectoryAsync(new CreateDirectoryRequest("files", "/", "Notes"), Token)).ShouldSucceed();
        var action = new CreateAction("files", "/", created.Name, isDirectory: true, created.Path);

        Assert.True((await action.UndoAsync(Service, Token)).Succeeded);
        Assert.False(Files.Exists("Notes"));

        Assert.True((await action.RedoAsync(Service, Token)).Succeeded);
        Assert.True(Files.Exists("Notes"));
    }

    [Fact]
    public async Task CreateFolder_UndoLeavesAFolderThatGainedFiles()
    {
        var created = (await Service.CreateDirectoryAsync(new CreateDirectoryRequest("files", "/", "Notes"), Token)).ShouldSucceed();
        (await Service.CreateFileAsync(new CreateFileRequest("files", "/Notes", "a.txt"), Token)).ShouldSucceed();
        var action = new CreateAction("files", "/", created.Name, isDirectory: true, created.Path);

        var outcome = await action.UndoAsync(Service, Token);

        Assert.True(outcome.Blocked);
        Assert.True(Files.Exists("Notes/a.txt"));
    }

    [Fact]
    public async Task CreateFile_UndoLeavesAFileThatWasWritten()
    {
        var created = (await Service.CreateFileAsync(new CreateFileRequest("files", "/", "note.txt"), Token)).ShouldSucceed();
        Files.Write("note.txt", "hello");
        var action = new CreateAction("files", "/", created.Name, isDirectory: false, created.Path);

        var outcome = await action.UndoAsync(Service, Token);

        Assert.True(outcome.Blocked);
        Assert.Equal("hello", File.ReadAllText(Files.Resolve("note.txt")));
    }

    [Fact]
    public async Task Rename_UndoAndRedo_SwapTheNames()
    {
        Files.Write("a.txt", "hi");
        var renamed = (await Service.RenameAsync(new RenameRequest("files", "/a.txt", "b.txt"), Token)).ShouldSucceed();
        var action = new RenameAction("files", "/a.txt", "a.txt", renamed.Path, renamed.Name);

        Assert.True((await action.UndoAsync(Service, Token)).Succeeded);
        Assert.True(Files.Exists("a.txt"));
        Assert.False(Files.Exists("b.txt"));

        Assert.True((await action.RedoAsync(Service, Token)).Succeeded);
        Assert.True(Files.Exists("b.txt"));
        Assert.False(Files.Exists("a.txt"));
    }

    [Fact]
    public async Task Move_UndoAndRedo_PutTheFileBack()
    {
        Files.Write("b.txt", "x");
        Files.CreateDirectory("dir");
        var moved = (await Service.TransferAsync(
            new TransferRequest("files", ["/b.txt"], "files", "/dir", IsCopy: false), Token)).ShouldSucceed();
        var action = new TransferAction(
            "files",
            "files",
            "/dir",
            isCopy: false,
            [new TransferredItem("/b.txt", moved.Results[0].ResultPath!)]);

        Assert.True((await action.UndoAsync(Service, Token)).Succeeded);
        Assert.True(Files.Exists("b.txt"));
        Assert.False(Files.Exists("dir/b.txt"));

        Assert.True((await action.RedoAsync(Service, Token)).Succeeded);
        Assert.False(Files.Exists("b.txt"));
        Assert.True(Files.Exists("dir/b.txt"));
    }

    [Fact]
    public async Task Duplicate_UndoDeletesTheCopy_AndRedoCreatesItAgain()
    {
        Files.Write("report.pdf", "pdf");
        var copied = (await Service.TransferAsync(
            new TransferRequest("files", ["/report.pdf"], "files", "/", IsCopy: true), Token)).ShouldSucceed();
        var action = new TransferAction(
            "files",
            "files",
            "/",
            isCopy: true,
            [new TransferredItem("/report.pdf", copied.Results[0].ResultPath!)]);

        Assert.True((await action.UndoAsync(Service, Token)).Succeeded);
        Assert.True(Files.Exists("report.pdf"));
        Assert.False(Files.Exists("report (2).pdf"));

        Assert.True((await action.RedoAsync(Service, Token)).Succeeded);
        Assert.True(Files.Exists("report (2).pdf"));
    }

    [Fact]
    public async Task TextEdit_UndoRestoresThePreviousText()
    {
        Files.Write("a.txt", "one");
        (await Service.SaveTextAsync(new SaveTextFileRequest("files", "/a.txt", "two"), Token)).ShouldSucceed();
        var action = new TextEditAction("files", "/a.txt", "one", "two");

        Assert.True((await action.UndoAsync(Service, Token)).Succeeded);
        Assert.Equal("one", (await Service.ReadTextAsync("files", "/a.txt", Token)).ShouldSucceed().Content);

        Assert.True((await action.RedoAsync(Service, Token)).Succeeded);
        Assert.Equal("two", (await Service.ReadTextAsync("files", "/a.txt", Token)).ShouldSucceed().Content);
    }

    [Fact]
    public async Task TextEdit_UndoLeavesAFileThatChangedAgain()
    {
        Files.Write("a.txt", "changed");
        var action = new TextEditAction("files", "/a.txt", "one", "two");

        var outcome = await action.UndoAsync(Service, Token);

        Assert.True(outcome.Blocked);
        Assert.Equal("changed", File.ReadAllText(Files.Resolve("a.txt")));
    }

    private sealed class Marker(int id) : HistoryAction
    {
        public int Id { get; } = id;

        public override Task<HistoryOutcome> UndoAsync(IFileManagerService service, CancellationToken cancellationToken) =>
            Task.FromResult(HistoryOutcome.Ok());

        public override Task<HistoryOutcome> RedoAsync(IFileManagerService service, CancellationToken cancellationToken) =>
            Task.FromResult(HistoryOutcome.Ok());
    }
}
