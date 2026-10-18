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
    bool IsCurrent(string rootId);

    /// <summary>Whether the last scan of <paramref name="rootId"/> stopped at the entry cap.</summary>
    bool IsTruncated(string rootId);

    /// <summary>Starts a scan. Activities that arrive before <see cref="TryCommit"/> are kept and applied on top.</summary>
    int BeginRebuild(string rootId);

    /// <summary>Publishes <paramref name="entries"/> when no newer scan or invalidation has started.</summary>
    bool TryCommit(string rootId, int generation, IReadOnlyList<FileIndexEntry> entries, bool truncated);

    /// <summary>Drops a root so the next search walks it again. Used when an activity adds many unknown files.</summary>
    void Invalidate(string rootId);

    /// <summary>Folds one completed activity into the index. Ignored until the root has been scanned.</summary>
    void Apply(FileActivity activity);

    IReadOnlyList<FileIndexEntry> Query(FileIndexQuery query);
}
