namespace WebFilez.Enums;

/// <summary>Broad file classification driving icon, preview mode and the type filter.</summary>
public enum FileKind
{
    /// <summary>An extension the kind table does not know.</summary>
    Other = 0,

    /// <summary>A directory.</summary>
    Folder = 1,

    /// <summary>A picture, including SVG.</summary>
    Image = 2,

    /// <summary>A video.</summary>
    Video = 3,

    /// <summary>An audio file.</summary>
    Audio = 4,

    /// <summary>An office or ebook file.</summary>
    Document = 5,

    /// <summary>A zip, tar, or similar package.</summary>
    Archive = 6,

    /// <summary>Plain text, Markdown, a log, or CSV.</summary>
    Text = 7,

    /// <summary>Source or configuration the built-in editor can open.</summary>
    Code = 8,

    /// <summary>A PDF.</summary>
    Pdf = 9,
}

/// <summary>The column a listing is sorted by.</summary>
public enum ListingSortField
{
    /// <summary>The file name, compared naturally.</summary>
    Name = 0,

    /// <summary>The size in bytes.</summary>
    Size = 1,

    /// <summary>When the item was last changed.</summary>
    Modified = 2,

    /// <summary>The file kind, then the name.</summary>
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
    /// <summary>Every file and folder.</summary>
    All = 0,

    /// <summary>Images. Folders stay so the user can open them.</summary>
    Images = 1,

    /// <summary>Documents, PDF, text, and code.</summary>
    Documents = 2,

    /// <summary>Video and audio.</summary>
    Media = 3,

    /// <summary>Archives.</summary>
    Archives = 4,

    /// <summary>Directories only. Files stay out of the listing.</summary>
    Folders = 5,
}