using WebFilez.Services.Search;

namespace WebFilez.Services.FileSystem;

public sealed partial class FileManagerService
{
    public async Task<Result<SearchResultDto>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        string query = request.Query?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            return Result<SearchResultDto>.Invalid(new ValidationError(nameof(request.Query), "Enter something to search for."));
        }

        if (query.Length > 200)
        {
            return Result<SearchResultDto>.Invalid(new ValidationError(nameof(request.Query), "That search is too long."));
        }

        if (request.Path is not null && string.IsNullOrWhiteSpace(request.RootId))
        {
            return Result<SearchResultDto>.Invalid(new ValidationError(nameof(request.RootId), "Searching one folder needs a root."));
        }

        var roots = await rootProvider.GetRootsAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.RootId))
        {
            var match = roots.FirstOrDefault(root => root.Id.Equals(request.RootId, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return Result<SearchResultDto>.NotFound($"'{request.RootId}' was not found.");
            }

            roots = [match];
        }

        string? prefix = string.IsNullOrWhiteSpace(request.Path) ? null : VirtualPath.Normalize(request.Path);
        int maxResults = Math.Clamp(request.MaxResults <= 0 ? 200 : request.MaxResults, 1, 1_000);
        var hits = new List<SearchHitDto>();
        bool truncated = false;

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rootPermissions = await resolver.GetPermissionsAsync(root, Constants.RootPath, cancellationToken);
            if (!rootPermissions.HasAny(FilePermission.Browse | FilePermission.Read))
            {
                continue;
            }

            var indexed = await EnsureIndexedAsync(root, cancellationToken);
            if (!indexed.IsSuccess)
            {
                return indexed.To<SearchResultDto>();
            }

            if (index.IsTruncated(root.Id))
            {
                truncated = true;
            }

            // Pull a window wider than the page so a permission check can still fill MaxResults.
            const int candidateLimit = 2_000;
            var matches = index.Query(new FileIndexQuery(
                root.Id,
                query,
                prefix,
                request.Kind,
                request.ShowHidden && root.ShowHiddenFiles,
                candidateLimit));

            if (matches.Count >= candidateLimit)
            {
                truncated = true;
            }

            foreach (var entry in matches)
            {
                var permissions = await resolver.GetPermissionsAsync(root, entry.Path, cancellationToken);
                if (!permissions.HasAny(FilePermission.Browse | FilePermission.Read))
                {
                    continue;
                }

                string parent = VirtualPath.GetParent(entry.Path) ?? Constants.RootPath;
                string location = parent == Constants.RootPath ? root.DisplayName : root.DisplayName + parent;

                hits.Add(new SearchHitDto(
                    root.Id,
                    root.DisplayName,
                    entry.Path,
                    entry.Name,
                    location,
                    entry.IsDirectory,
                    entry.SizeBytes,
                    entry.ModifiedAt,
                    entry.Kind,
                    entry.Extension,
                    entry.IsHidden,
                    permissions,
                    HasThumbnail: !entry.IsDirectory && FileKinds.SupportsThumbnail(entry.Name) && permissions.Has(FilePermission.Read),
                    PublicUrl: FileItems.BuildPublicUrl(root, entry.Path, entry.IsDirectory)));
            }
        }

        var ordered = Sort(hits.Select(ToSortable), request.SortBy, request.Descending).ToList();
        var byPath = hits.ToDictionary(hit => hit.RootId + "\n" + hit.Path, StringComparer.OrdinalIgnoreCase);
        if (ordered.Count > maxResults)
        {
            truncated = true;
        }

        var sortedHits = ordered.Take(maxResults).Select(item => byPath[item.Path]).ToList();

        return Result<SearchResultDto>.Success(new SearchResultDto(query, request.RootId, prefix, sortedHits, truncated));
    }

    private async Task<Result> EnsureIndexedAsync(FileRoot root, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3 && !index.IsCurrent(root.Id); attempt++)
        {
            int generation = index.BeginRebuild(root.Id);
            List<FileIndexEntry> entries;
            bool truncated;

            try
            {
                (entries, truncated) = await ScanRootAsync(root, cancellationToken);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, "Denied by the operating system while indexing {Root}", root.Id);
                return Result.Error("The server does not have permission to read that folder.");
            }
            catch (IOException ex)
            {
                logger.LogError(ex, "I/O failure indexing {Root}", root.Id);
                return Result.Error("That folder could not be searched.");
            }

            if (index.TryCommit(root.Id, generation, entries, truncated) && index.IsCurrent(root.Id))
            {
                return Result.Success();
            }
        }

        return index.IsCurrent(root.Id)
            ? Result.Success()
            : Result.Error("The search index changed while it was being built. Try again.");
    }

    private async Task<(List<FileIndexEntry> Entries, bool Truncated)> ScanRootAsync(
        FileRoot root,
        CancellationToken cancellationToken)
    {
        int cap = Math.Max(1, options.CurrentValue.MaxSearchIndexEntries);
        var resolved = await resolver.RequireAsync(root.Id, Constants.RootPath, FilePermission.Browse, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return ([], false);
        }

        var excludes = ExcludeMatcher.For(root);
        var entries = new List<FileIndexEntry>(capacity: 64);
        var pending = new Stack<string>();
        pending.Push(Constants.RootPath);
        bool truncated = false;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = pending.Pop();
            var folder = await resolver.RequireAsync(root.Id, path, FilePermission.Browse, cancellationToken);
            if (!folder.IsSuccess)
            {
                continue;
            }

            await foreach (var child in folder.Value.Storage.ListAsync(folder.Value.Location, cancellationToken))
            {
                if (!FileItems.IsVisible(child, root, showHidden: true, excludes))
                {
                    continue;
                }

                string childPath = VirtualPath.Combine(path, child.Name);
                var permissions = await resolver.GetPermissionsAsync(root, childPath, cancellationToken);
                if (!permissions.HasAny(FilePermission.Browse | FilePermission.Read))
                {
                    continue;
                }

                var kind = FileKinds.Classify(child.Name, child.IsDirectory);
                entries.Add(new FileIndexEntry(
                    root.Id,
                    childPath,
                    child.Name,
                    child.IsDirectory,
                    child.SizeBytes,
                    child.ModifiedAt,
                    kind,
                    child.IsDirectory ? null : Path.GetExtension(child.Name),
                    child.IsHidden));

                if (entries.Count >= cap)
                {
                    truncated = true;
                    return (entries, truncated);
                }

                if (child.IsDirectory && permissions.Has(FilePermission.Browse))
                {
                    pending.Push(childPath);
                }
            }
        }

        return (entries, truncated);
    }

    /// <summary>Search hits go through the listing sort, which only needs the fields it compares.</summary>
    private static FileSystemItemDto ToSortable(SearchHitDto hit) => new(
        hit.Name,
        hit.RootId + "\n" + hit.Path,
        hit.IsDirectory,
        hit.SizeBytes,
        hit.ModifiedAt,
        hit.Kind,
        hit.Extension,
        hit.IsHidden,
        hit.Permissions,
        hit.HasThumbnail,
        hit.PublicUrl);
}
