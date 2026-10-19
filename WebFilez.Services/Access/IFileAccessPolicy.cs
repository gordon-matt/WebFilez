namespace WebFilez.Services.Access;

/// <summary>
/// Decides what the current caller may do at a path. Registered scoped, so an implementation can
/// inject <c>IHttpContextAccessor</c> or <c>AuthenticationStateProvider</c> to find out who the
/// caller is, and can cache rule lookups for the lifetime of a request.
/// </summary>
/// <remarks>
/// <para>
/// The result is always intersected with the root's own
/// <see cref="FileRoot.EffectivePermissions"/>, so a policy can narrow a root but can never make
/// a read-only root writable. Called once per listed item, so keep it cheap.
/// </para>
/// <para>
/// These permissions only control what the file manager UI and API allow. They do not secure
/// files in a public bucket or web root. A file this policy hides can still be fetched by its
/// public URL.
/// </para>
/// </remarks>
public interface IFileAccessPolicy
{
    /// <param name="root">The root containing the path.</param>
    /// <param name="path">A normalized virtual path within the root, such as <c>/photos/2024</c>.</param>
    ValueTask<FilePermission> GetPermissionsAsync(FileRoot root, string path, CancellationToken cancellationToken = default);
}

/// <summary>Grants every caller whatever the root allows.</summary>
public sealed class RootFileAccessPolicy : IFileAccessPolicy
{
    public ValueTask<FilePermission> GetPermissionsAsync(
        FileRoot root,
        string path,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(root.EffectivePermissions);
}