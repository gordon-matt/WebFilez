namespace WebFilez;

/// <summary>
/// The folder the current caller cannot navigate above. Scoped, so a Blazor circuit and an HTTP
/// request each have their own. A null <see cref="BasePath"/> leaves the root open, which is the
/// ordinary file manager.
/// </summary>
public sealed class FileScope
{
    /// <summary>When set, other roots are refused.</summary>
    public string? RootId { get; private set; }

    /// <summary>Normalized virtual path, such as <c>/Tenant_1</c>. Null means the whole root.</summary>
    public string? BasePath { get; private set; }

    /// <summary>
    /// Confines later operations to <paramref name="basePath"/> inside <paramref name="rootId"/>.
    /// A null, empty, or root path clears the confinement.
    /// </summary>
    public void Confine(string? rootId, string? basePath)
    {
        string normalized = VirtualPath.Normalize(basePath);
        if (normalized == Constants.RootPath)
        {
            RootId = null;
            BasePath = null;
            return;
        }

        RootId = string.IsNullOrWhiteSpace(rootId) ? null : rootId;
        BasePath = normalized;
    }

    /// <summary>
    /// Ignores the confinement until disposed. The search index scans a whole root and then
    /// filters, so building it must be allowed to see folders the caller cannot.
    /// </summary>
    public IDisposable Suspend()
    {
        string? rootId = RootId;
        string? basePath = BasePath;
        RootId = null;
        BasePath = null;
        return new Suspension(this, rootId, basePath);
    }

    private sealed class Suspension(FileScope scope, string? rootId, string? basePath) : IDisposable
    {
        public void Dispose()
        {
            scope.RootId = rootId;
            scope.BasePath = basePath;
        }
    }

    /// <summary>True when <paramref name="virtualPath"/> may be used in <paramref name="rootId"/>.</summary>
    public bool Allows(string rootId, string virtualPath)
    {
        if (BasePath is null)
        {
            return true;
        }

        if (RootId is not null && !rootId.Equals(RootId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return VirtualPath.IsSameOrUnder(VirtualPath.Normalize(virtualPath), BasePath);
    }
}

/// <summary>
/// Mints the token a browser sends with uploads, downloads and thumbnails so those HTTP calls
/// stay inside a <see cref="FileScope"/>. The component creates one when it is rendered on the
/// server. A WebAssembly host, which cannot mint tokens, passes one through to the component.
/// </summary>
public interface IFileScopeProtector
{
    /// <summary>A token that confines API calls to <paramref name="basePath"/> inside <paramref name="rootId"/>.</summary>
    string Protect(string? rootId, string basePath);

    /// <summary>Reads a token this protector issued. False for anything else, including a token for the root itself.</summary>
    bool TryUnprotect(string? token, out string? rootId, out string basePath);
}
