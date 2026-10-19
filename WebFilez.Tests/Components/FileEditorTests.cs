using WebFilez.Components;

namespace WebFilez.Tests.Components;

public class FileEditorTests
{
    private static FileEditor Editor(string id, string accept) => new(id, id, accept);

    [Fact]
    public void EmptyConfigurationUsesEachEditorsOwnTypes()
    {
        FileEditor[] registered =
        [
            Editor("filerobot", ".jpg,.jpeg,.png,.webp"),
            Editor("pintura", ".gif,.bmp"),
        ];

        var images = FileEditors.Match(registered, null, "photo.jpg");
        var gifs = FileEditors.Match(registered, "  ", "anim.gif");

        Assert.Equal(["filerobot"], images.Select(editor => editor.Id));
        Assert.Equal(["pintura"], gifs.Select(editor => editor.Id));
    }

    [Fact]
    public void ConfigurationAssignsTypesAndOrder()
    {
        FileEditor[] registered =
        [
            Editor("filerobot", ".jpg,.png,.gif"),
            Editor("pintura", ".jpg,.png,.gif"),
        ];

        var matches = FileEditors.Match(registered, "pintura:.gif,.bmp | filerobot: .jpg, .png", "photo.jpg");
        var gif = FileEditors.Match(registered, "pintura:.gif,.bmp|filerobot:.jpg,.png", "anim.gif");

        Assert.Equal(["filerobot"], matches.Select(editor => editor.Id));
        Assert.Equal(".jpg, .png", matches[0].Accept);
        Assert.Equal(["pintura"], gif.Select(editor => editor.Id));
    }

    [Fact]
    public void ConfigurationCanKeepAnEditorsOwnList()
    {
        var matches = FileEditors.Match(
            [Editor("filerobot", "image/*")],
            "filerobot",
            "scan.webp");

        Assert.Equal(["filerobot"], matches.Select(editor => editor.Id));
        Assert.Equal("image/*", matches[0].Accept);
    }

    [Fact]
    public void UnknownEditorsAndDuplicatesAreSkipped()
    {
        var matches = FileEditors.Match(
            [Editor("filerobot", ".png")],
            "missing:.png|filerobot:.png|filerobot:.jpg",
            "icon.png");

        Assert.Equal(["filerobot"], matches.Select(editor => editor.Id));
    }

    [Fact]
    public void AnEditorWithNoTypesClaimsNothingUntilConfigured()
    {
        var registered = new FileEditor("codespace", "Codespace", "");

        Assert.Empty(FileEditors.Match([registered], null, "notes.txt"));
        Assert.Equal(["codespace"], FileEditors.Match([registered], "codespace:.txt,.md", "notes.txt").Select(editor => editor.Id));
    }

    [Theory]
    [InlineData("filerobot:.jpg,.png|pintura:.gif", 2)]
    [InlineData(": .jpg", 0)]
    [InlineData("filerobot:", 1)]
    public void ParseCountsEntries(string configuration, int count) =>
        Assert.Equal(count, FileEditors.Parse(configuration).Count);

    [Fact]
    public void ABareIdKeepsTheEditorsOwnAcceptList()
    {
        var bindings = FileEditors.Parse("filerobot");

        Assert.Equal("filerobot", bindings[0].Id);
        Assert.Null(bindings[0].Accept);
    }
}
