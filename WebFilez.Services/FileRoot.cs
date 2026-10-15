namespace WebFilez.Services;

/// <summary>A folder on disk exposed to the file manager under a stable id.</summary>
public sealed class FileRoot
{
    /// <summary>Stable identifier used in API requests and URLs. Keep it short and URL-safe.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name. Defaults to <see cref="Id"/>.</summary>
    public string? Name { get; set; }

    /// <summary>Physical folder. A relative path is resolved against the app's content root.</summary>
    public string Path { get; set; } = string.Empty;

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
