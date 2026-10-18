namespace WebFilez.Components;

/// <summary>One row in the listing. <see cref="Key"/> is the path, or root plus path while searching.</summary>
public sealed record ListingEntry(string Key, string RootId, FileSystemItemDto Item, string? Location = null);
