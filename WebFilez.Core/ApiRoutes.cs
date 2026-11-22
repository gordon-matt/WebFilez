namespace WebFilez;

/// <summary>
/// Endpoint paths relative to the API prefix. Shared by the server mapping and the client so the
/// two can't drift apart.
/// </summary>
public static class ApiRoutes
{
    /// <summary>The roots the caller can browse.</summary>
    public const string Roots = "roots";

    /// <summary>One page of a folder.</summary>
    public const string List = "list";

    /// <summary>A name search.</summary>
    public const string Search = "search";

    /// <summary>The immediate subfolders of a folder.</summary>
    public const string Folders = "folders";

    /// <summary>The info panel for one item.</summary>
    public const string Detail = "detail";

    /// <summary>The recursive size of a folder.</summary>
    public const string Size = "size";

    /// <summary>Creates a folder.</summary>
    public const string Directories = "directories";

    /// <summary>Creates an empty file.</summary>
    public const string Files = "files";

    /// <summary>Renames one item.</summary>
    public const string Rename = "rename";

    /// <summary>Deletes the listed items.</summary>
    public const string Delete = "delete";

    /// <summary>Copies or moves items.</summary>
    public const string Transfer = "transfer";

    /// <summary>Reads or saves a text file.</summary>
    public const string Text = "text";

    /// <summary>Writes a zip, tar, or gzipped tar.</summary>
    public const string Compress = "compress";

    /// <summary>Extracts an archive.</summary>
    public const string Extract = "extract";

    /// <summary>Issues a one-time zip link.</summary>
    public const string Tickets = "tickets";

    /// <summary>Checks whether one insertable URL can be read anonymously.</summary>
    public const string UrlCheck = "url-check";

    /// <summary>Streams one file.</summary>
    public const string Download = "download";

    /// <summary>A cached WebP thumbnail.</summary>
    public const string Thumbnail = "thumb";

    /// <summary>Stores an uploaded file. The request must send <c>X-WebFilez: 1</c>.</summary>
    public const string Upload = "upload";

    /// <summary>Streams the zip for a ticket. <c>{token}</c> is the ticket.</summary>
    public const string Zip = "zip";
}