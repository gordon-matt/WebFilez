namespace WebFilez.Enums;

/// <summary>
/// What the caller may do at a path. Roots carry a default set, and an
/// <c>IFileAccessPolicy</c> can narrow or widen it per path and per user.
/// </summary>
/// <remarks>
/// These permissions only control what the file manager UI and API allow. They do not secure
/// files that a public bucket or the web root already serves. A denied path can still be fetched
/// by anyone who has its public URL.
/// </remarks>
[Flags]
public enum FilePermission
{
    None = 0,

    /// <summary>See the folder and list its children. Without this the path is invisible.</summary>
    Browse = 1 << 0,

    /// <summary>Open file contents in the browser (preview, editor, thumbnails).</summary>
    Read = 1 << 1,

    /// <summary>Fetch raw bytes: a direct download, or a zip of a selection.</summary>
    Download = 1 << 2,

    /// <summary>Upload files and create files and folders.</summary>
    Create = 1 << 3,

    /// <summary>Overwrite an existing file (editor save, upload-replace).</summary>
    Modify = 1 << 4,

    Rename = 1 << 5,

    /// <summary>Move out of this path. The destination separately needs <see cref="Create"/>.</summary>
    Move = 1 << 6,

    Delete = 1 << 7,

    /// <summary>Write a zip or tar of a selection. Also needs <see cref="Create"/> where it's written.</summary>
    Compress = 1 << 8,

    /// <summary>Extract an archive. Also needs <see cref="Create"/> for the output.</summary>
    Extract = 1 << 9,

    /// <summary>Look but don't touch.</summary>
    ReadOnly = Browse | Read | Download,

    /// <summary>Read-only plus additions. Nothing that already exists can be changed or removed.</summary>
    Contributor = ReadOnly | Create | Compress | Extract,

    Full = Contributor | Modify | Rename | Move | Delete,
}

public static class FilePermissionExtensions
{
    /// <summary>Permissions that never change the filesystem. Used to clamp read-only roots.</summary>
    public const FilePermission NonMutating = FilePermission.Browse
        | FilePermission.Read
        | FilePermission.Download;

    public static bool Has(this FilePermission value, FilePermission required) =>
        required == FilePermission.None || (value & required) == required;

    public static bool HasAny(this FilePermission value, FilePermission any) => (value & any) != 0;
}