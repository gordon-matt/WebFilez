namespace WebFilez.Enums;

/// <summary>Broad file classification driving icon, preview mode and the type filter.</summary>
public enum FileKind
{
    /// <summary>
    /// The other value.
    /// </summary>
    Other = 0,
    /// <summary>
    /// The folder value.
    /// </summary>
    Folder = 1,
    /// <summary>
    /// The image value.
    /// </summary>
    Image = 2,
    /// <summary>
    /// The video value.
    /// </summary>
    Video = 3,
    /// <summary>
    /// The audio value.
    /// </summary>
    Audio = 4,
    /// <summary>
    /// The document value.
    /// </summary>
    Document = 5,
    /// <summary>
    /// The archive value.
    /// </summary>
    Archive = 6,
    /// <summary>
    /// The text value.
    /// </summary>
    Text = 7,
    /// <summary>
    /// The code value.
    /// </summary>
    Code = 8,
    /// <summary>
    /// The pdf value.
    /// </summary>
    Pdf = 9,
}

/// <summary>
/// Specifies the listing sort field.
/// </summary>
public enum ListingSortField
{
    /// <summary>
    /// The name value.
    /// </summary>
    Name = 0,
    /// <summary>
    /// The size value.
    /// </summary>
    Size = 1,
    /// <summary>
    /// The modified value.
    /// </summary>
    Modified = 2,
    /// <summary>
    /// The kind value.
    /// </summary>
    Kind = 3,
}

/// <summary>Listing layouts offered by the file manager.</summary>
public enum ListingViewMode
{
    /// <summary>Medium tiles with a thumbnail. The default.</summary>
    Tiles = 0,

    /// <summary>One row per item with name, size and modified columns.</summary>
    List = 1,

    /// <summary>Dense rows, no thumbnails.</summary>
    Compact = 2,
}

/// <summary>The type filter buttons above the listing.</summary>
public enum KindFilter
{
    /// <summary>
    /// The all value.
    /// </summary>
    All = 0,
    /// <summary>
    /// The images value.
    /// </summary>
    Images = 1,
    /// <summary>
    /// The documents value.
    /// </summary>
    Documents = 2,
    /// <summary>
    /// The media value.
    /// </summary>
    Media = 3,
    /// <summary>
    /// The archives value.
    /// </summary>
    Archives = 4,

    /// <summary>Directories only. Files stay out of the listing.</summary>
    Folders = 5,
}