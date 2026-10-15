using WebFilez.Services.FileSystem;

namespace WebFilez.Tests.FileSystem;

public class NamingTests
{
    [Fact]
    public void NaturalComparer_OrdersNumbersByValue()
    {
        string[] names = ["file10.txt", "file2.txt", "File1.txt", "file1a.txt"];

        Assert.Equal(["File1.txt", "file1a.txt", "file2.txt", "file10.txt"], names.Order(NaturalComparer.Instance));
    }

    [Theory]
    [InlineData("photo.jpg", true)]
    [InlineData("shell.php", false)]
    [InlineData("shell.PHP", false)]
    [InlineData("shell.php.jpg", false)]
    [InlineData(".htaccess", false)]
    [InlineData("page.html", false)]
    [InlineData("logo.svg", false)]
    [InlineData("setup.exe", false)]
    [InlineData("README", true)]
    public void BlockedExtensions_CheckEveryDottedSuffix(string name, bool expected) =>
        Assert.Equal(expected, FileNamePolicy.IsExtensionAllowed(name, new WebFilezOptions()));

    [Fact]
    public void AllowedExtensions_RestrictToTheList()
    {
        var options = new WebFilezOptions { AllowedExtensions = [".jpg", ".png"] };

        Assert.True(FileNamePolicy.IsExtensionAllowed("a.JPG", options));
        Assert.False(FileNamePolicy.IsExtensionAllowed("a.gif", options));
        Assert.False(FileNamePolicy.IsExtensionAllowed("noextension", options));
        Assert.False(FileNamePolicy.IsExtensionAllowed("a.php.jpg", options));
    }

    [Fact]
    public void Clean_AppliesTheConfiguredRules()
    {
        var settings = new FileNameOptions { ReplaceSpaces = true, LowerCase = true, RemoveDiacritics = true };

        Assert.Equal("cafe_menu.pdf", FileNamePolicy.Clean("  Café Menu.PDF ", settings));
        Assert.Equal("a_b", FileNamePolicy.Clean("a/b", new FileNameOptions()));
        Assert.Equal(string.Empty, FileNamePolicy.Clean("..", new FileNameOptions()));
    }

    [Theory]
    [InlineData("*.bak", "old.bak", true)]
    [InlineData("*.bak", "old.bak.txt", false)]
    [InlineData("node_modules", "Node_Modules", true)]
    [InlineData("thumb?.db", "thumb1.db", true)]
    public void ExcludeMatcher_MatchesGlobsCaseInsensitively(string pattern, string name, bool expected) =>
        Assert.Equal(expected, ExcludeMatcher.For([pattern]).IsExcluded(name));
}