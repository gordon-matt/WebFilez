namespace WebFilez;

/// <summary>
/// Extension-driven classification of files into <see cref="FileKind"/>, plus the MIME type that
/// goes with it. Centralised so the listing icons, the preview and the type filter can never
/// disagree about what an <c>.mkv</c> is.
/// </summary>
public static class FileKinds
{
    public const string DefaultMimeType = "application/octet-stream";

    private static readonly Dictionary<string, FileKind> KindByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = FileKind.Image,
        [".jpeg"] = FileKind.Image,
        [".png"] = FileKind.Image,
        [".gif"] = FileKind.Image,
        [".bmp"] = FileKind.Image,
        [".webp"] = FileKind.Image,
        [".svg"] = FileKind.Image,
        [".ico"] = FileKind.Image,
        [".tif"] = FileKind.Image,
        [".tiff"] = FileKind.Image,
        [".avif"] = FileKind.Image,

        [".mp4"] = FileKind.Video,
        [".webm"] = FileKind.Video,
        [".mkv"] = FileKind.Video,
        [".avi"] = FileKind.Video,
        [".mov"] = FileKind.Video,
        [".wmv"] = FileKind.Video,
        [".m4v"] = FileKind.Video,
        [".mpg"] = FileKind.Video,
        [".mpeg"] = FileKind.Video,

        [".mp3"] = FileKind.Audio,
        [".flac"] = FileKind.Audio,
        [".wav"] = FileKind.Audio,
        [".ogg"] = FileKind.Audio,
        [".oga"] = FileKind.Audio,
        [".opus"] = FileKind.Audio,
        [".m4a"] = FileKind.Audio,
        [".aac"] = FileKind.Audio,

        [".pdf"] = FileKind.Pdf,

        [".doc"] = FileKind.Document,
        [".docx"] = FileKind.Document,
        [".xls"] = FileKind.Document,
        [".xlsx"] = FileKind.Document,
        [".ppt"] = FileKind.Document,
        [".pptx"] = FileKind.Document,
        [".odt"] = FileKind.Document,
        [".ods"] = FileKind.Document,
        [".odp"] = FileKind.Document,
        [".rtf"] = FileKind.Document,
        [".epub"] = FileKind.Document,

        [".zip"] = FileKind.Archive,
        [".tar"] = FileKind.Archive,
        [".gz"] = FileKind.Archive,
        [".tgz"] = FileKind.Archive,
        [".bz2"] = FileKind.Archive,
        [".xz"] = FileKind.Archive,
        [".7z"] = FileKind.Archive,
        [".rar"] = FileKind.Archive,

        [".txt"] = FileKind.Text,
        [".md"] = FileKind.Text,
        [".log"] = FileKind.Text,
        [".csv"] = FileKind.Text,

        [".cs"] = FileKind.Code,
        [".js"] = FileKind.Code,
        [".mjs"] = FileKind.Code,
        [".ts"] = FileKind.Code,
        [".jsx"] = FileKind.Code,
        [".tsx"] = FileKind.Code,
        [".json"] = FileKind.Code,
        [".xml"] = FileKind.Code,
        [".yml"] = FileKind.Code,
        [".yaml"] = FileKind.Code,
        [".toml"] = FileKind.Code,
        [".ini"] = FileKind.Code,
        [".sql"] = FileKind.Code,
        [".html"] = FileKind.Code,
        [".htm"] = FileKind.Code,
        [".css"] = FileKind.Code,
        [".scss"] = FileKind.Code,
        [".py"] = FileKind.Code,
        [".sh"] = FileKind.Code,
        [".ps1"] = FileKind.Code,
    };

    private static readonly Dictionary<string, string> MimeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
        [".avif"] = "image/avif",

        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".mkv"] = "video/x-matroska",
        [".avi"] = "video/x-msvideo",
        [".mov"] = "video/quicktime",
        [".m4v"] = "video/x-m4v",
        [".mpg"] = "video/mpeg",
        [".mpeg"] = "video/mpeg",

        [".mp3"] = "audio/mpeg",
        [".flac"] = "audio/flac",
        [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg",
        [".opus"] = "audio/opus",
        [".m4a"] = "audio/mp4",
        [".aac"] = "audio/aac",

        [".pdf"] = "application/pdf",
        [".epub"] = "application/epub+zip",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",

        [".zip"] = "application/zip",
        [".tar"] = "application/x-tar",
        [".gz"] = "application/gzip",
        [".7z"] = "application/x-7z-compressed",
        [".rar"] = "application/vnd.rar",

        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".log"] = "text/plain",
        [".csv"] = "text/csv",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "text/javascript",
    };

    /// <summary>Raster formats the thumbnail generator can decode.</summary>
    private static readonly HashSet<string> ThumbnailExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff",
    };

    public static FileKind Classify(string fileName, bool isDirectory)
    {
        if (isDirectory)
        {
            return FileKind.Folder;
        }

        string extension = Path.GetExtension(fileName);
        return string.IsNullOrEmpty(extension)
            ? FileKind.Other
            : KindByExtension.GetValueOrDefault(extension, FileKind.Other);
    }

    public static string GetMimeType(string fileName)
    {
        string extension = Path.GetExtension(fileName);
        return string.IsNullOrEmpty(extension)
            ? DefaultMimeType
            : MimeByExtension.GetValueOrDefault(extension, DefaultMimeType);
    }

    /// <summary>Text-ish kinds the built-in editor opens.</summary>
    public static bool IsEditable(FileKind kind) => kind is FileKind.Text or FileKind.Code;

    /// <summary>Kinds the preview dialog can show.</summary>
    public static bool CanPreview(FileKind kind) => kind is
        FileKind.Image or FileKind.Video or FileKind.Audio or FileKind.Pdf
        or FileKind.Text or FileKind.Code;

    /// <summary>Kinds the preview dialog can page through with the arrow keys.</summary>
    public static bool IsMedia(FileKind kind) => kind is
        FileKind.Image or FileKind.Video or FileKind.Audio or FileKind.Pdf;

    public static bool SupportsThumbnail(string fileName) =>
        ThumbnailExtensions.Contains(Path.GetExtension(fileName));

    /// <summary>
    /// Whether a download may be served with <c>Content-Disposition: inline</c>.
    /// </summary>
    /// <remarks>
    /// An allow-list on purpose. Rendering uploaded content inline on the app's own origin turns
    /// any upload into stored XSS, so <c>text/html</c>, <c>image/svg+xml</c> and anything
    /// unrecognised are always sent as attachments. Only media the browser decodes rather than
    /// executes, plus PDF and plain text, is listed.
    /// </remarks>
    public static bool IsInlineSafe(string? mimeType) => !string.IsNullOrEmpty(mimeType)
            && !mimeType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase) && (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
            || mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            || mimeType.Equals("text/plain", StringComparison.OrdinalIgnoreCase));

    public static bool Matches(FileKind kind, KindFilter filter) => filter switch
    {
        KindFilter.Images => kind == FileKind.Image,
        KindFilter.Documents => kind is FileKind.Document or FileKind.Pdf or FileKind.Text or FileKind.Code,
        KindFilter.Media => kind is FileKind.Video or FileKind.Audio,
        KindFilter.Archives => kind == FileKind.Archive,
        KindFilter.Folders => kind == FileKind.Folder,
        _ => true,
    };

    /// <summary>
    /// Whether a file satisfies an HTML-style <c>accept</c> list, such as
    /// <c>image/*,.pdf</c>. An empty list accepts everything.
    /// </summary>
    public static bool IsAccepted(string fileName, string? accept)
    {
        if (string.IsNullOrWhiteSpace(accept))
        {
            return true;
        }

        string extension = Path.GetExtension(fileName);
        string mimeType = GetMimeType(fileName);

        foreach (string token in accept.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            bool matches = token.StartsWith('.')
                ? token.Equals(extension, StringComparison.OrdinalIgnoreCase)
                : token.EndsWith("/*", StringComparison.Ordinal)
                    ? mimeType.StartsWith(token[..^1], StringComparison.OrdinalIgnoreCase)
                    : token.Equals(mimeType, StringComparison.OrdinalIgnoreCase);

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Formats a byte count for display (1 KB = 1024 B).</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 0)
        {
            return "-";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} B"
            : $"{value.ToString(value < 10 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture)} {units[unit]}";
    }
}