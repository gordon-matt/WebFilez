namespace WebFilez.Services;

/// <summary>Configuration for WebFilez, bound from the <c>WebFilez</c> section.</summary>
public sealed class WebFilezOptions
{
    public const string SectionName = "WebFilez";

    /// <summary>Where <c>MapWebFilezApi()</c> mounts the HTTP API.</summary>
    public string ApiPrefix { get; set; } = Constants.DefaultApiPrefix;

    /// <summary>
    /// The folders the file manager exposes. Ignored when the host registers its own
    /// <see cref="IFileRootProvider"/>.
    /// </summary>
    public List<FileRoot> Roots { get; set; } = [];

    /// <summary>Largest single file accepted by an upload.</summary>
    public long MaxUploadBytes { get; set; } = Constants.DefaultMaxUploadBytes;

    /// <summary>
    /// When non-empty, the only extensions a file may be uploaded, created or renamed to, such as
    /// <c>.jpg</c>. Matched case-insensitively. Takes precedence over <see cref="BlockedExtensions"/>.
    /// </summary>
    public List<string> AllowedExtensions { get; set; } = [];

    /// <summary>
    /// Extensions that can never be uploaded, created or renamed to.
    /// </summary>
    /// <remarks>
    /// The defaults cover formats a web server might execute, executables, and the markup types
    /// (<c>.html</c>, <c>.svg</c>) a browser runs script from. A root with a
    /// <see cref="FileRoot.PublicUrl"/> is served by the static file middleware on the app's own
    /// origin, where an uploaded HTML or SVG file is stored XSS. Remove them from this list only
    /// if every root that accepts uploads is served from a separate origin or not at all.
    /// </remarks>
    public List<string> BlockedExtensions { get; set; } =
    [
        ".asax", ".ascx", ".ashx", ".asmx", ".asp", ".aspx", ".axd", ".bat", ".cer", ".cgi",
        ".cmd", ".com", ".config", ".cshtml", ".dll", ".exe", ".htaccess", ".htm", ".html",
        ".htpasswd", ".jsp", ".jspx", ".mht", ".mhtml", ".msi", ".phar", ".php", ".php3", ".php4",
        ".php5", ".phtml", ".pl", ".razor", ".scr", ".shtml", ".svg", ".svgz", ".vbhtml", ".vbs",
        ".xht", ".xhtml",
    ];

    public FileNameOptions FileNames { get; set; } = new();

    public ImageResizeOptions ImageResize { get; set; } = new();

    public ThumbnailOptions Thumbnails { get; set; } = new();

    /// <summary>Largest file the text editor opens.</summary>
    public long MaxEditableFileBytes { get; set; } = Constants.DefaultMaxEditableFileBytes;

    /// <summary>
    /// Refuses to keep extracting once an archive has written this much. A small archive that
    /// expands to terabytes is the classic decompression bomb, and a hard ceiling is the only
    /// real defence.
    /// </summary>
    public long MaxExtractedBytes { get; set; } = 10L * 1024 * 1024 * 1024;

    /// <summary>Companion to <see cref="MaxExtractedBytes"/>: a bomb can also be millions of tiny files.</summary>
    public int MaxExtractedEntries { get; set; } = 100_000;
}

/// <summary>How names supplied by users are cleaned up before they reach the disk.</summary>
public sealed class FileNameOptions
{
    /// <summary>Replace spaces with <see cref="SpaceReplacement"/>. Useful when files are served by URL.</summary>
    public bool ReplaceSpaces { get; set; }

    public string SpaceReplacement { get; set; } = "_";

    public bool LowerCase { get; set; }

    /// <summary>Strip accents, so <c>Café.jpg</c> becomes <c>Cafe.jpg</c>.</summary>
    public bool RemoveDiacritics { get; set; }
}

/// <summary>Shrinks uploaded images that exceed a maximum size, preserving the aspect ratio.</summary>
public sealed class ImageResizeOptions
{
    public bool Enabled { get; set; }

    /// <summary>Zero means no limit on this axis.</summary>
    public int MaxWidth { get; set; } = 2560;

    /// <summary>Zero means no limit on this axis.</summary>
    public int MaxHeight { get; set; } = 2560;
}

public sealed class ThumbnailOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Defaults to a <c>webfilez-thumbnails</c> folder under the system temp directory.</summary>
    public string? CacheDirectory { get; set; }

    /// <summary>WebP quality, 1 to 100.</summary>
    public int Quality { get; set; } = 75;
}