using WebFilez.Storage.AzureBlob;

namespace WebFilez.Tests.Storage;

public class AzureBlobPathTests
{
    [Fact]
    public void Blob_names_join_the_prefix_and_the_virtual_path()
    {
        var root = new FileRoot { Id = "uploads", Path = "campaigns" };

        Assert.Equal("campaigns", AzureBlobPaths.BlobName(AzureBlobPaths.Prefix(root), "/"));
        Assert.Equal("campaigns/a/b.txt", AzureBlobPaths.BlobName(AzureBlobPaths.Prefix(root), "/a/b.txt"));
        Assert.Equal("campaigns/a/", AzureBlobPaths.ListingPrefix(AzureBlobPaths.BlobName(AzureBlobPaths.Prefix(root), "/a")));
    }

    [Fact]
    public void A_settings_prefix_overrides_the_path_and_slashes_are_trimmed()
    {
        var root = new FileRoot
        {
            Id = "uploads",
            Path = "ignored",
            Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Prefix"] = "\\site\\files\\",
            },
        };

        Assert.Equal("site/files", AzureBlobPaths.Prefix(root));
        Assert.Equal("site/files/photo.jpg", AzureBlobPaths.BlobName(AzureBlobPaths.Prefix(root), "/photo.jpg"));
    }

    [Fact]
    public void An_empty_prefix_leaves_the_blob_at_the_container_root()
    {
        var root = new FileRoot { Id = "uploads", Path = "/" };

        Assert.Equal(string.Empty, AzureBlobPaths.Prefix(root));
        Assert.Equal("notes/a.txt", AzureBlobPaths.BlobName(string.Empty, "/notes/a.txt"));
        Assert.Equal(string.Empty, AzureBlobPaths.ListingPrefix(string.Empty));
    }

    [Fact]
    public void Mapping_rejects_hidden_excluded_and_temporary_names()
    {
        var root = new FileRoot
        {
            Id = "uploads",
            Path = "site",
            ExcludePatterns = ["*.bak"],
        };
        var provider = new AzureBlobStorageProvider();

        Assert.False(provider.TryMap(root, "/.env", out _));
        Assert.False(provider.TryMap(root, "/notes.bak", out _));
        Assert.False(provider.TryMap(root, "/upload.webfilez.tmp", out _));
        Assert.True(provider.TryMap(root, "/notes/a.txt", out var location));
        Assert.Equal("site/notes/a.txt", location.NativePath);
        Assert.Equal("site", location.RootNativePath);
    }

    [Fact]
    public void A_folder_contains_its_descendants_in_the_same_root_only()
    {
        var root = new FileRoot { Id = "uploads", Path = "site" };
        var other = new FileRoot { Id = "other", Path = "site" };
        var provider = new AzureBlobStorageProvider();

        Assert.True(provider.TryMap(root, "/docs", out var folder));
        Assert.True(provider.TryMap(root, "/docs/a.txt", out var child));
        Assert.True(provider.TryMap(root, "/", out var container));
        Assert.True(provider.TryMap(other, "/docs/a.txt", out var foreign));

        Assert.True(provider.Contains(folder, child));
        Assert.True(provider.Contains(container, child));
        Assert.True(provider.AreSame(folder, folder));
        Assert.False(provider.Contains(folder, foreign));
        Assert.False(provider.Contains(child, folder));
    }
}