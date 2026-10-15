namespace WebFilez.Services;

/// <summary>A folder exposed to the file manager under a stable id, wherever its provider stores it.</summary>
public sealed class FileRoot
{
    /// <summary>Stable identifier used in API requests and URLs. Keep it short and URL-safe.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name. Defaults to <see cref="Id"/>.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Which <see cref="Storage.IStorageProvider"/> holds this root. Empty, or
    /// <see cref="Storage.FileSystemStorageProvider.ProviderKey"/>, uses the file system.
    /// Any other value must match the <c>Key</c> published by that provider's package.
    /// </summary>
    public string Provider { get; set; } = Storage.FileSystemStorageProvider.ProviderKey;

    /// <summary>
    /// Where the root lives. For the file system provider this is a directory, and a relative path
    /// is resolved against the content root. For other providers it is a prefix inside the store,
    /// such as a blob folder, and is left as written.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Provider-specific settings, such as an Azure connection string and container. The file
    /// system provider does not use this.
    /// </summary>
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The URL the folder is publicly served at, such as <c>/uploads</c> when it sits under
    /// <c>wwwroot</c>. When set, files and folders carry a <c>PublicUrl</c> and picker mode returns it.
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>
    /// The most anyone can do in this root. An <see cref="Access.IFileAccessPolicy"/> may narrow
    /// it further per user or per path.
    /// </summary>
    public FilePermission Permissions { get; set; } = FilePermission.Full;

    /// <summary>Clamps every caller to browse, read and download, whatever the policy says.</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// Whether symbolic links and junctions are followed. Off by default: a link inside the root
    /// can point anywhere on the machine, so following it would let a user browse outside it.
    /// </summary>
    public bool FollowSymlinks { get; set; }

    /// <summary>
    /// Whether dot-files and hidden-attribute items are visible. When false they are not listed
    /// and can't be opened by path, which keeps files such as <c>.env</c> and <c>.git</c> private.
    /// </summary>
    public bool ShowHiddenFiles { get; set; }

    /// <summary>
    /// Names to hide entirely, as globs matched against each path segment: <c>*.bak</c>,
    /// <c>node_modules</c>, <c>Thumbs.db</c>. Excluded items can't be opened by path either.
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;

    /// <summary>The permissions the root itself allows, after the read-only clamp.</summary>
    public FilePermission EffectivePermissions =>
        IsReadOnly ? Permissions & FilePermissionExtensions.NonMutating : Permissions;
}