namespace WebFilez.Services.Access;

/// <summary>
/// Narrows what a caller can do at one path and everything under it. The longest matching rule
/// wins. A custom <see cref="IFileAccessPolicy"/> is applied as well and can only narrow further.
/// </summary>
/// <remarks>
/// <see cref="Read"/> false hides the path. <see cref="Write"/> false blocks creating or changing
/// contents. <see cref="Locked"/> blocks rename, move, and delete, which is what an elFinder
/// volume's locked flag means. These rules do not stop a public URL from being fetched.
/// </remarks>
public sealed class PathAccessRule
{
    /// <summary>Virtual path, such as <c>/readonly.txt</c> or <c>/Parent/Children</c>.</summary>
    public string Path { get; set; } = "/";

    /// <summary>When false, the path is not listed and cannot be opened.</summary>
    public bool Read { get; set; } = true;

    /// <summary>When false, nothing can be created or overwritten here.</summary>
    public bool Write { get; set; } = true;

    /// <summary>When true, the path cannot be renamed, moved, or deleted.</summary>
    public bool Locked { get; set; }

    /// <summary>The root's permissions with this rule's restrictions removed.</summary>
    public FilePermission Clamp(FilePermission ceiling)
    {
        FilePermission allowed = ceiling;

        if (!Read)
        {
            allowed &= ~(FilePermission.Browse | FilePermission.Read | FilePermission.Download);
        }

        if (!Write)
        {
            allowed &= ~(FilePermission.Create | FilePermission.Modify | FilePermission.Compress | FilePermission.Extract);
        }

        if (Locked)
        {
            allowed &= ~(FilePermission.Rename | FilePermission.Move | FilePermission.Delete);
        }

        return allowed;
    }
}

/// <summary>Picks the path rule that applies to a virtual path.</summary>
public static class PathAccess
{
    /// <summary>
    /// The permissions the root's rules allow at <paramref name="virtualPath"/>. With no matching
    /// rule, this is <see cref="FileRoot.EffectivePermissions"/>.
    /// </summary>
    public static FilePermission Clamp(FileRoot root, string virtualPath)
    {
        if (root.Rules.Count == 0)
        {
            return root.EffectivePermissions;
        }

        string path = VirtualPath.Normalize(virtualPath);
        PathAccessRule? match = null;
        int matchLength = -1;

        foreach (PathAccessRule rule in root.Rules)
        {
            string rulePath = VirtualPath.Normalize(rule.Path);
            if (!VirtualPath.IsSameOrUnder(path, rulePath) || rulePath.Length <= matchLength)
            {
                continue;
            }

            match = rule;
            matchLength = rulePath.Length;
        }

        return match is null ? root.EffectivePermissions : match.Clamp(root.EffectivePermissions);
    }
}
