namespace WebFilez.Services.Search;

/// <summary>One indexed file or folder. Paths are virtual, the same form listings use.</summary>
public sealed record FileIndexEntry(
    string RootId,
    string Path,
    string Name,
    bool IsDirectory,
    long SizeBytes,
    DateTime ModifiedAt,
    FileKind Kind,
    string? Extension,
    bool IsHidden);

/// <summary>What <see cref="IFileIndex.Query"/> matches inside a single root.</summary>
public sealed record FileIndexQuery(
    string RootId,
    string Text,
    string? PathPrefix,
    KindFilter Kind,
    bool IncludeHidden,
    int Limit);

/// <summary>
/// The store behind host-backed search. The default is an in-memory index filled by walking a
/// root the first time it is searched, then updated from every successful file activity. Register
/// another implementation before <c>AddWebFilez</c> to keep the same stream in a database or an
/// external engine.
/// </summary>
public interface IFileIndex
{
    /// <summary>Whether <paramref name="rootId"/> has a finished scan that activities are updating.</summary>
    /// <param name="rootId">The root.</param>
    /// <returns><see langword="true"/> when a finished scan is being kept current.</returns>
    bool IsCurrent(string rootId);

    /// <summary>Whether the last scan of <paramref name="rootId"/> stopped at the entry cap.</summary>
    /// <param name="rootId">The root.</param>
    /// <returns><see langword="true"/> when the scan stopped early.</returns>
    bool IsTruncated(string rootId);

    /// <summary>Starts a scan. Activities that arrive before <see cref="TryCommit"/> are kept and applied on top.</summary>
    /// <param name="rootId">The root to scan.</param>
    /// <returns>A generation. A later scan or invalidation makes an older generation uncommittable.</returns>
    int BeginRebuild(string rootId);

    /// <summary>Publishes <paramref name="entries"/> when no newer scan or invalidation has started.</summary>
    /// <param name="rootId">The root.</param>
    /// <param name="generation">The value returned by <see cref="BeginRebuild"/>.</param>
    /// <param name="entries">The scanned files and folders.</param>
    /// <param name="truncated">Whether the scan stopped at the entry cap.</param>
    /// <returns><see langword="false"/> when a newer scan or invalidation has started.</returns>
    bool TryCommit(string rootId, int generation, IReadOnlyList<FileIndexEntry> entries, bool truncated);

    /// <summary>Drops a root so the next search walks it again. Used when an activity adds many unknown files.</summary>
    /// <param name="rootId">The root to drop.</param>
    void Invalidate(string rootId);

    /// <summary>Folds one completed activity into the index. Ignored until the root has been scanned.</summary>
    /// <param name="activity">The completed change or download.</param>
    void Apply(FileActivity activity);

    /// <summary>Entries in one root that match the text, the folder, and the kind, up to the limit.</summary>
    /// <param name="query">What to match.</param>
    /// <returns>The matches. An unscanned root returns an empty list.</returns>
    IReadOnlyList<FileIndexEntry> Query(FileIndexQuery query);
}
