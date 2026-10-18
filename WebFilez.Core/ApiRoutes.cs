namespace WebFilez;

/// <summary>
/// Endpoint paths relative to the API prefix. Shared by the server mapping and the client so the
/// two can't drift apart.
/// </summary>
public static class ApiRoutes
{
    public const string Roots = "roots";
    public const string List = "list";
    public const string Search = "search";
    public const string Folders = "folders";
    public const string Detail = "detail";
    public const string Size = "size";
    public const string Directories = "directories";
    public const string Files = "files";
    public const string Rename = "rename";
    public const string Delete = "delete";
    public const string Transfer = "transfer";
    public const string Text = "text";
    public const string Compress = "compress";
    public const string Extract = "extract";
    public const string Tickets = "tickets";

    public const string Download = "download";
    public const string Thumbnail = "thumb";
    public const string Upload = "upload";
    public const string Zip = "zip";
}