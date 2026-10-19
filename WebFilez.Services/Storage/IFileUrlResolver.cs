namespace WebFilez.Services.Storage;

/// <summary>
/// Decides the URL for a file or folder. Every insertable address WebFilez returns — listings,
/// details, search hits, uploads, and the picker's <c>PublicUrl</c> — comes from this.
/// </summary>
/// <remarks>
/// <para>
/// The URL is what a host stores in content. It should keep working for as long as the file stays
/// at that path. Return a presigned, SAS, or other expiring URL only when
/// <see cref="FileUrlContext.AllowExpiringUrls"/> is true. An expiring URL stops loading and breaks
/// pages that saved it.
/// </para>
/// <para>
/// Register a replacement before <c>AddWebFilez</c>. The registration uses <c>TryAdd</c>, so the
/// host's implementation is kept. The built-in resolver never returns an expiring URL.
/// </para>
/// <para>
/// Downloads, previews and thumbnails inside the manager stay on the WebFilez API, which checks
/// permissions. Those addresses are not stored in content and do not come from this resolver.
/// </para>
/// </remarks>
public interface IFileUrlResolver
{
    /// <summary>
    /// The URL a host may store for this file or folder, or <c>null</c> when it has no public
    /// address. <paramref name="context"/> carries the root, the provider key, the virtual path,
    /// and the object key when the caller already knows it.
    /// </summary>
    ValueTask<string?> ResolveAsync(FileUrlContext context, CancellationToken cancellationToken = default);
}

/// <summary>What <see cref="IFileUrlResolver"/> needs to build one file or folder URL.</summary>
public sealed record FileUrlContext
{
    public required FileRoot Root { get; init; }

    /// <summary>The <see cref="IStorageProvider.Key"/> that stores this root, such as <c>amazon-s3</c>.</summary>
    public required string ProviderKey { get; init; }

    /// <summary>Root-relative path, such as <c>/album/photo.png</c>. The root itself is <c>/</c>.</summary>
    public required string VirtualPath { get; init; }

    /// <summary>Folder addresses end with a slash, so a host can tell them from files.</summary>
    public required bool IsDirectory { get; init; }

    /// <summary>
    /// Object key, blob name, or path inside a share, without the bucket, container, or share name.
    /// When null, the built-in resolver derives it from the root prefix and <see cref="VirtualPath"/>.
    /// </summary>
    public string? StorageKey { get; init; }

    /// <summary>
    /// True only when <see cref="WebFilezOptions.AllowExpiringUrls"/> is set. A resolver may return
    /// a presigned or SAS URL only when this is true. Those URLs expire and break content that
    /// stored them. The built-in resolver ignores this flag and always returns a stable URL.
    /// </summary>
    public bool AllowExpiringUrls { get; init; }

    /// <summary>Builds a context from a root. An empty provider selects the file system.</summary>
    public static FileUrlContext For(FileRoot root, string virtualPath, bool isDirectory, bool allowExpiringUrls) =>
        new()
        {
            Root = root,
            ProviderKey = FileSystemStorageProvider.IsFileSystem(root.Provider)
                ? FileSystemStorageProvider.ProviderKey
                : root.Provider.Trim(),
            VirtualPath = global::WebFilez.VirtualPath.Normalize(virtualPath),
            IsDirectory = isDirectory,
            AllowExpiringUrls = allowExpiringUrls,
        };
}
