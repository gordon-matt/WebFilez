using WebFilez.Storage.AzureFiles;

namespace WebFilez.Tests.Storage;

public class AzureFilePathTests
{
    [Fact]
    public void Paths_join_the_prefix_and_the_virtual_path_inside_the_share()
    {
        var root = new FileRoot { Id = "shared", Path = "campaigns" };

        Assert.Equal("campaigns", AzureFilePaths.FilePath(AzureFilePaths.Prefix(root), "/"));
        Assert.Equal("campaigns/a/b.txt", AzureFilePaths.FilePath(AzureFilePaths.Prefix(root), "/a/b.txt"));
        Assert.Equal("campaigns/a", AzureFilePaths.Parent("campaigns/a/b.txt"));
        Assert.Equal("b.txt", AzureFilePaths.Name("campaigns/a/b.txt"));
    }

    [Fact]
    public void A_settings_prefix_overrides_the_path_and_slashes_are_trimmed()
    {
        var root = new FileRoot
        {
            Id = "shared",
            Path = "ignored",
            Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Prefix"] = "\\site\\files\\",
            },
        };

        Assert.Equal("site/files", AzureFilePaths.Prefix(root));
        Assert.Equal("site/files/photo.jpg", AzureFilePaths.FilePath(AzureFilePaths.Prefix(root), "/photo.jpg"));
    }

    [Fact]
    public void An_empty_prefix_leaves_the_file_at_the_share_root()
    {
        var root = new FileRoot { Id = "shared", Path = "/" };

        Assert.Equal(string.Empty, AzureFilePaths.Prefix(root));
        Assert.Equal("notes/a.txt", AzureFilePaths.FilePath(string.Empty, "/notes/a.txt"));
        Assert.Equal(string.Empty, AzureFilePaths.Parent("notes.txt"));
    }

    [Fact]
    public void Mapping_rejects_hidden_excluded_and_temporary_names()
    {
        var root = new FileRoot
        {
            Id = "shared",
            Path = "site",
            ExcludePatterns = ["*.bak"],
        };
        var provider = new AzureFileStorageProvider();

        Assert.False(provider.TryMap(root, "/.env", out _));
        Assert.False(provider.TryMap(root, "/notes.bak", out _));
        Assert.False(provider.TryMap(root, "/upload.webfilez.tmp", out _));
        Assert.True(provider.TryMap(root, "/notes/a.txt", out var location));
        Assert.Equal("site/notes/a.txt", location.NativePath);
        Assert.Equal("site", location.RootNativePath);
    }

    [Fact]
    public void Names_compare_without_case_and_only_within_the_same_root()
    {
        var root = new FileRoot { Id = "shared", Path = "site" };
        var other = new FileRoot { Id = "other", Path = "site" };
        var provider = new AzureFileStorageProvider();

        Assert.True(provider.TryMap(root, "/Docs", out var folder));
        Assert.True(provider.TryMap(root, "/docs/a.txt", out var child));
        Assert.True(provider.TryMap(root, "/DOCS", out var sameFolder));
        Assert.True(provider.TryMap(root, "/", out var shareRoot));
        Assert.True(provider.TryMap(other, "/docs/a.txt", out var foreign));

        Assert.True(provider.Contains(folder, child));
        Assert.True(provider.Contains(shareRoot, child));
        Assert.True(provider.AreSame(folder, sameFolder));
        Assert.False(provider.Contains(folder, foreign));
        Assert.False(provider.Contains(child, folder));
    }
}