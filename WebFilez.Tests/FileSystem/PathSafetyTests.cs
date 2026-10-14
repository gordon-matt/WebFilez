using WebFilez.Services.FileSystem;
using WebFilez.Tests.Infrastructure;

namespace WebFilez.Tests.FileSystem;

public sealed class PathSafetyTests : IDisposable
{
    private readonly TempFolder folder = new();

    private FileRoot Root(Action<FileRoot>? configure = null)
    {
        var root = new FileRoot { Id = "files", Path = folder.Path };
        configure?.Invoke(root);
        return root;
    }

    [Fact]
    public void TryMapToDisk_StaysInsideTheRoot()
    {
        Assert.True(PathResolver.TryMapToDisk(Root(), "/a/b.txt", out string physical, out string rootPath));
        Assert.Equal(Path.Combine(folder.Path, "a", "b.txt"), physical);
        Assert.Equal(Path.GetFullPath(folder.Path), rootPath);
    }

    [Theory]
    [InlineData("/.git/config")]
    [InlineData("/upload.webfilez.tmp")]
    [InlineData("/file.txt:secret")]
    public void TryMapToDisk_RefusesNamesAListingHides(string path)
    {
        if (path.Contains(':') && !OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.False(PathResolver.TryMapToDisk(Root(), path, out _, out _));
    }

    [Fact]
    public void TryMapToDisk_RefusesExcludedSegments() =>
        Assert.False(PathResolver.TryMapToDisk(Root(r => r.ExcludePatterns = ["private"]), "/private/x.txt", out _, out _));

    [Fact]
    public void TryMapToDisk_AllowsDotFilesWhenTheRootShowsThem() =>
        Assert.True(PathResolver.TryMapToDisk(Root(r => r.ShowHiddenFiles = true), "/.config", out _, out _));

    [Fact]
    public void TryMapToDisk_RefusesLinksUnlessTheRootFollowsThem()
    {
        var outside = new TempFolder();
        try
        {
            string link = Path.Combine(folder.Path, "escape");
            try
            {
                Directory.CreateSymbolicLink(link, outside.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Creating links needs Developer Mode or elevation on Windows.
                return;
            }

            Assert.False(PathResolver.TryMapToDisk(Root(), "/escape/x.txt", out _, out _));
            Assert.True(PathResolver.TryMapToDisk(Root(r => r.FollowSymlinks = true), "/escape/x.txt", out _, out _));
        }
        finally
        {
            outside.Dispose();
        }
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("a/../../evil.txt")]
    [InlineData("/../evil.txt")]
    [InlineData(@"..\evil.txt")]
    [InlineData("C:/Windows/evil.txt")]
    public void ResolveEntryDestination_RejectsZipSlip(string entry) =>
        Assert.Null(FileManagerService.ResolveEntryDestination(Path.GetFullPath(folder.Path), entry));

    [Fact]
    public void ResolveEntryDestination_KeepsSafeEntriesInside() =>
        Assert.Equal(
            Path.Combine(Path.GetFullPath(folder.Path), "docs", "a.txt"),
            FileManagerService.ResolveEntryDestination(Path.GetFullPath(folder.Path), "/docs/./a.txt"));

    public void Dispose() => folder.Dispose();
}
