namespace WebFilez.Enums;

/// <summary>Broad file classification driving icon, preview mode and the type filter.</summary>
public enum FileKind
{
    Other = 0,
    Folder = 1,
    Image = 2,
    Video = 3,
    Audio = 4,
    Document = 5,
    Archive = 6,
    Text = 7,
    Code = 8,
    Pdf = 9,
}

public enum ListingSortField
{
    Name = 0,
    Size = 1,
    Modified = 2,
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

    /// <summary>Large thumbnails, no labels. For image folders.</summary>
    Gallery = 3,
}

/// <summary>The type filter buttons above the listing.</summary>
public enum KindFilter
{
    All = 0,
    Images = 1,
    Documents = 2,
    Media = 3,
    Archives = 4,

    /// <summary>Directories only. Files stay out of the listing.</summary>
    Folders = 5,
}
