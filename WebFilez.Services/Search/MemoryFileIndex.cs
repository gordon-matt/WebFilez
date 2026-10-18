namespace WebFilez.Services.Search;

/// <summary>
/// Process-wide in-memory index. Search still checks the caller's permissions, so a shared index
/// does not reveal files the current user cannot browse.
/// </summary>
public sealed class MemoryFileIndex : IFileIndex
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, Bucket> buckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<FileActivity>> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> generations = new(StringComparer.OrdinalIgnoreCase);

    public bool IsCurrent(string rootId)
    {
        lock (gate)
        {
            return buckets.TryGetValue(rootId, out var bucket) && bucket.Current;
        }
    }

    public bool IsTruncated(string rootId)
    {
        lock (gate)
        {
            return buckets.TryGetValue(rootId, out var bucket) && bucket.Truncated;
        }
    }

    public int BeginRebuild(string rootId)
    {
        lock (gate)
        {
            if (buckets.TryGetValue(rootId, out var bucket))
            {
                buckets[rootId] = bucket with { Current = false };
            }

            pending[rootId] = [];
            int generation = generations.GetValueOrDefault(rootId) + 1;
            generations[rootId] = generation;
            return generation;
        }
    }

    public bool TryCommit(string rootId, int generation, IReadOnlyList<FileIndexEntry> entries, bool truncated)
    {
        lock (gate)
        {
            if (generations.GetValueOrDefault(rootId) != generation)
            {
                return false;
            }

            var queued = pending.GetValueOrDefault(rootId) ?? [];
            pending.Remove(rootId);
            buckets[rootId] = new Bucket(
                entries.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase),
                truncated,
                Current: true);

            foreach (var activity in queued)
            {
                Mutate(activity);
            }

            return true;
        }
    }

    public void Invalidate(string rootId)
    {
        lock (gate)
        {
            buckets.Remove(rootId);
            pending.Remove(rootId);
            generations[rootId] = generations.GetValueOrDefault(rootId) + 1;
        }
    }

    public void Apply(FileActivity activity)
    {
        lock (gate)
        {
            foreach (string rootId in AffectedRoots(activity))
            {
                if (pending.TryGetValue(rootId, out var queued))
                {
                    queued.Add(activity);
                    continue;
                }

                if (buckets.TryGetValue(rootId, out var bucket) && bucket.Current)
                {
                    Mutate(activity);
                }
            }
        }
    }

    public IReadOnlyList<FileIndexEntry> Query(FileIndexQuery query)
    {
        lock (gate)
        {
            if (!buckets.TryGetValue(query.RootId, out var bucket) || !bucket.Current)
            {
                return [];
            }

            string text = query.Text.Trim();
            string? prefix = string.IsNullOrWhiteSpace(query.PathPrefix) ? null : VirtualPath.Normalize(query.PathPrefix);
            int limit = Math.Max(1, query.Limit);
            var matches = new List<FileIndexEntry>();

            foreach (var entry in bucket.Entries.Values)
            {
                if (matches.Count >= limit)
                {
                    break;
                }

                if (!query.IncludeHidden && entry.IsHidden)
                {
                    continue;
                }

                if (prefix is not null && prefix != Constants.RootPath
                    && (!VirtualPath.IsSameOrUnder(entry.Path, prefix) || entry.Path.Equals(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // Folders stay in every kind view except "folders", which is directories only. Same rule as a listing.
                bool kindMatches = query.Kind switch
                {
                    KindFilter.Folders => entry.IsDirectory,
                    KindFilter.All => true,
                    _ => entry.IsDirectory || FileKinds.Matches(entry.Kind, query.Kind),
                };

                if (!kindMatches)
                {
                    continue;
                }

                if (!MatchesText(entry, text))
                {
                    continue;
                }

                matches.Add(entry);
            }

            return matches;
        }
    }

    private void Mutate(FileActivity activity)
    {
        switch (activity.Kind)
        {
            case FileActivityKind.Download:
                return;

            case FileActivityKind.Delete:
                RemoveTree(activity.RootId, activity.Path);
                return;

            case FileActivityKind.Extract:
                InvalidateLocked(activity.TargetRootId ?? activity.RootId);
                return;

            case FileActivityKind.Rename:
            case FileActivityKind.Move:
                Relocate(
                    activity.RootId,
                    activity.Path,
                    activity.TargetRootId ?? activity.RootId,
                    activity.TargetPath ?? activity.Path);
                return;

            case FileActivityKind.Copy:
                CopyTree(
                    activity.RootId,
                    activity.Path,
                    activity.TargetRootId ?? activity.RootId,
                    activity.TargetPath ?? activity.Path);
                return;

            case FileActivityKind.CreateFolder:
                Upsert(EntryFromActivity(activity, directory: true));
                return;

            case FileActivityKind.Modify:
                Upsert(UpdateExisting(activity) ?? EntryFromActivity(activity, directory: false));
                return;

            case FileActivityKind.CreateFile:
            case FileActivityKind.Upload:
            case FileActivityKind.Compress:
                Upsert(EntryFromActivity(activity, directory: false));
                return;
        }
    }

    private void Upsert(FileIndexEntry entry)
    {
        if (buckets.TryGetValue(entry.RootId, out var bucket) && bucket.Current)
        {
            bucket.Entries[entry.Path] = entry;
        }
    }

    private FileIndexEntry? UpdateExisting(FileActivity activity)
    {
        string path = VirtualPath.Normalize(activity.Path);
        if (buckets.TryGetValue(activity.RootId, out var bucket)
            && bucket.Entries.TryGetValue(path, out var existing))
        {
            return existing with
            {
                SizeBytes = activity.SizeBytes ?? existing.SizeBytes,
                ModifiedAt = DateTime.UtcNow,
            };
        }

        return null;
    }

    private void RemoveTree(string rootId, string path)
    {
        if (!buckets.TryGetValue(rootId, out var bucket))
        {
            return;
        }

        string normalized = VirtualPath.Normalize(path);
        foreach (string key in bucket.Entries.Keys.Where(candidate => VirtualPath.IsSameOrUnder(candidate, normalized)).ToList())
        {
            bucket.Entries.Remove(key);
        }
    }

    private void Relocate(string rootId, string path, string targetRootId, string targetPath)
    {
        if (!buckets.TryGetValue(rootId, out var source) || !source.Current)
        {
            InvalidateLocked(targetRootId);
            return;
        }

        string from = VirtualPath.Normalize(path);
        string to = VirtualPath.Normalize(targetPath);
        var moving = source.Entries.Values.Where(entry => VirtualPath.IsSameOrUnder(entry.Path, from)).ToList();

        foreach (var entry in moving)
        {
            source.Entries.Remove(entry.Path);
        }

        if (!buckets.TryGetValue(targetRootId, out var target) || !target.Current)
        {
            return;
        }

        foreach (var entry in moving)
        {
            var relocated = Retarget(entry, targetRootId, Rewrite(entry.Path, from, to));
            target.Entries[relocated.Path] = relocated;
        }
    }

    private void CopyTree(string rootId, string path, string targetRootId, string targetPath)
    {
        if (!buckets.TryGetValue(rootId, out var source) || !source.Current
            || !buckets.TryGetValue(targetRootId, out var target) || !target.Current)
        {
            InvalidateLocked(targetRootId);
            return;
        }

        string from = VirtualPath.Normalize(path);
        string to = VirtualPath.Normalize(targetPath);
        foreach (var entry in source.Entries.Values.Where(entry => VirtualPath.IsSameOrUnder(entry.Path, from)).ToList())
        {
            var copy = Retarget(entry, targetRootId, Rewrite(entry.Path, from, to));
            target.Entries[copy.Path] = copy;
        }
    }

    private void InvalidateLocked(string rootId)
    {
        buckets.Remove(rootId);
        pending.Remove(rootId);
        generations[rootId] = generations.GetValueOrDefault(rootId) + 1;
    }

    private static FileIndexEntry EntryFromActivity(FileActivity activity, bool directory)
    {
        string path = VirtualPath.Normalize(activity.TargetPath ?? activity.Path);
        string name = VirtualPath.GetName(path);
        long size = activity.SizeBytes ?? 0;
        var kind = FileKinds.Classify(name, directory);
        return new FileIndexEntry(
            activity.RootId,
            path,
            name,
            directory,
            size,
            DateTime.UtcNow,
            kind,
            directory ? null : Path.GetExtension(name),
            name.StartsWith('.'));
    }

    private static FileIndexEntry Retarget(FileIndexEntry entry, string rootId, string path)
    {
        string name = VirtualPath.GetName(path);
        var kind = FileKinds.Classify(name, entry.IsDirectory);
        return entry with
        {
            RootId = rootId,
            Path = path,
            Name = name,
            Kind = kind,
            Extension = entry.IsDirectory ? null : Path.GetExtension(name),
        };
    }

    private static string Rewrite(string path, string from, string to)
    {
        if (path.Equals(from, StringComparison.OrdinalIgnoreCase))
        {
            return to;
        }

        string suffix = path[from.Length..];
        return to == Constants.RootPath ? suffix : to + suffix;
    }

    private static bool MatchesText(FileIndexEntry entry, string text)
    {
        if (entry.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (entry.Extension is { Length: > 1 } extension
            && (extension.Contains(text, StringComparison.OrdinalIgnoreCase)
                || extension.TrimStart('.').Equals(text.TrimStart('.'), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return entry.Kind != FileKind.Other
            && entry.Kind != FileKind.Folder
            && entry.Kind.ToString().Equals(text, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> AffectedRoots(FileActivity activity)
    {
        yield return activity.RootId;

        if (activity.TargetRootId is not null
            && !activity.TargetRootId.Equals(activity.RootId, StringComparison.OrdinalIgnoreCase))
        {
            yield return activity.TargetRootId;
        }
    }

    private sealed record Bucket(
        Dictionary<string, FileIndexEntry> Entries,
        bool Truncated,
        bool Current);
}
