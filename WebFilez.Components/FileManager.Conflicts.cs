using WebFilez.Components.Infrastructure;
using WebFilez.Components.Shared;

namespace WebFilez.Components;

public sealed partial class FileManager
{
    private NameConflictDialog? nameConflicts;

    /// <summary>
    /// One name that is already in the destination. <see cref="AllowReplace"/> is false when a
    /// file would land on a folder, or the other way around.
    /// </summary>
    private readonly record struct NameCollision(string Key, string Name, bool AllowReplace);

    private async Task<Dictionary<string, NameConflictChoice>> DecideConflictsAsync(IReadOnlyList<NameCollision> collisions)
    {
        var decisions = new Dictionary<string, NameConflictChoice>(StringComparer.OrdinalIgnoreCase);
        if (collisions.Count == 0 || nameConflicts is null)
        {
            foreach (NameCollision collision in collisions)
            {
                decisions[collision.Key] = NameConflictChoice.Skip;
            }

            return decisions;
        }

        NameConflictChoice? rest = null;
        for (int i = 0; i < collisions.Count; i++)
        {
            NameCollision collision = collisions[i];
            if (rest is { } chosen)
            {
                decisions[collision.Key] = chosen == NameConflictChoice.Replace && !collision.AllowReplace
                    ? NameConflictChoice.Skip
                    : chosen;
                continue;
            }

            NameConflictAnswer answer = await nameConflicts.ShowAsync(collision.Name, collisions.Count - i - 1, collision.AllowReplace);
            if (answer.Choice == NameConflictChoice.Cancel)
            {
                for (int j = i; j < collisions.Count; j++)
                {
                    decisions[collisions[j].Key] = NameConflictChoice.Skip;
                }

                break;
            }

            decisions[collision.Key] = answer.Choice;
            if (answer.ApplyToRest)
            {
                rest = answer.Choice;
            }
        }

        return decisions;
    }

    private async Task TransferResolvedAsync(TransferRequest request, string verb)
    {
        var clear = new List<string>();
        var collisions = new List<NameCollision>();
        var names = await DestinationNamesAsync(request);

        foreach (string path in request.Paths)
        {
            if (IsAlreadyThere(request, path))
            {
                clear.Add(path);
                continue;
            }

            string name = VirtualPath.GetName(path);
            if (names is not null && names.ContainsKey(name))
            {
                collisions.Add(new NameCollision(path, name, AllowReplace: true));
                continue;
            }

            clear.Add(path);
        }

        var decisions = await DecideConflictsAsync(collisions);
        var replace = new List<string>();
        var rename = new List<string>();
        foreach (NameCollision collision in collisions)
        {
            switch (decisions.GetValueOrDefault(collision.Key, NameConflictChoice.Skip))
            {
                case NameConflictChoice.Replace:
                    replace.Add(collision.Key);
                    break;
                case NameConflictChoice.Rename:
                    rename.Add(collision.Key);
                    break;
            }
        }

        var combined = new List<FileOperationResultDto>();
        combined.AddRange(await TransferGroupAsync(request, clear, overwrite: false, renameOnConflict: false));
        combined.AddRange(await TransferGroupAsync(request, replace, overwrite: true, renameOnConflict: false));
        combined.AddRange(await TransferGroupAsync(request, rename, overwrite: false, renameOnConflict: true));

        // A name past the first page of the destination was not in the list we asked about.
        var unexpected = combined.Where(row => row.NameConflict).ToList();
        if (unexpected.Count > 0)
        {
            combined.RemoveAll(row => row.NameConflict);
            var extra = await DecideConflictsAsync(unexpected
                .Select(row => new NameCollision(row.Path, VirtualPath.GetName(row.Path), AllowReplace: true))
                .ToList());
            var extraReplace = extra.Where(pair => pair.Value == NameConflictChoice.Replace).Select(pair => pair.Key).ToList();
            var extraRename = extra.Where(pair => pair.Value == NameConflictChoice.Rename).Select(pair => pair.Key).ToList();
            combined.AddRange(await TransferGroupAsync(request, extraReplace, overwrite: true, renameOnConflict: false));
            combined.AddRange(await TransferGroupAsync(request, extraRename, overwrite: false, renameOnConflict: true));
        }

        if (combined.Count == 0)
        {
            return;
        }

        var bulk = BulkOperationResultDto.From(combined);
        ShowBulk(bulk, verb);
        RememberTransfer(request, bulk);
        await RefreshAfterChangeAsync();
    }

    private async Task<List<FileOperationResultDto>> TransferGroupAsync(
        TransferRequest request,
        IReadOnlyList<string> paths,
        bool overwrite,
        bool renameOnConflict)
    {
        if (paths.Count == 0)
        {
            return [];
        }

        var result = await Service.TransferAsync(request with
        {
            Paths = paths,
            Overwrite = overwrite,
            RenameOnConflict = renameOnConflict,
        });

        if (!result.IsSuccess)
        {
            return paths.Select(path => new FileOperationResultDto(path, false, result.GetDisplayMessage())).ToList();
        }

        return [.. result.Value.Results];
    }

    private async Task<Dictionary<string, bool>?> DestinationNamesAsync(TransferRequest request)
    {
        if (listing is not null
            && rootId is not null
            && request.DestinationRootId.Equals(rootId, StringComparison.OrdinalIgnoreCase)
            && request.DestinationPath.Equals(listing.Path, StringComparison.OrdinalIgnoreCase))
        {
            return NamesOf(listing.Items);
        }

        var listed = await Service.ListAsync(new ListingRequest(
            request.DestinationRootId,
            request.DestinationPath,
            ShowHidden: true,
            PageSize: 500));

        return listed.IsSuccess ? NamesOf(listed.Value.Items) : null;
    }

    /// <summary>A copy or move into the folder the item already occupies. The service duplicates a copy and ignores a move.</summary>
    private static bool IsAlreadyThere(TransferRequest request, string path) =>
        request.RootId.Equals(request.DestinationRootId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(VirtualPath.GetParent(path) ?? Constants.RootPath, request.DestinationPath, StringComparison.OrdinalIgnoreCase);

    private async Task<List<UploadInstruction>?> PlanUploadsAsync(DroppedFile[] files)
    {
        var existing = listing is null
            ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            : NamesOf(listing.Items);
        var tops = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (DroppedFile file in files)
        {
            string top = TopSegment(file);
            if (seen.Add(top) && existing.ContainsKey(top))
            {
                tops.Add(top);
            }
        }

        if (tops.Count == 0)
        {
            return null;
        }

        bool allowReplace = Can(FilePermission.Modify);
        var collisions = tops.Select(top =>
        {
            bool existingIsDirectory = existing[top];
            bool incomingIsDirectory = files.Any(file =>
                TopSegment(file).Equals(top, StringComparison.OrdinalIgnoreCase)
                && (file.RelativePath?.Contains('/') ?? false));
            return new NameCollision(top, top, allowReplace && existingIsDirectory == incomingIsDirectory);
        }).ToList();

        var decisions = await DecideConflictsAsync(collisions);
        var taken = existing.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string top in tops)
        {
            if (decisions.GetValueOrDefault(top) != NameConflictChoice.Rename)
            {
                continue;
            }

            bool incomingIsDirectory = files.Any(file =>
                TopSegment(file).Equals(top, StringComparison.OrdinalIgnoreCase)
                && (file.RelativePath?.Contains('/') ?? false));
            renamed[top] = FreeDisplayName(top, incomingIsDirectory, taken);
        }

        var plans = new List<UploadInstruction>(files.Length);
        foreach (DroppedFile file in files)
        {
            string key = file.RelativePath ?? file.Name;
            string top = TopSegment(file);
            if (!existing.ContainsKey(top))
            {
                plans.Add(new UploadInstruction(key, "upload", null));
                continue;
            }

            switch (decisions.GetValueOrDefault(top, NameConflictChoice.Skip))
            {
                case NameConflictChoice.Replace:
                    plans.Add(new UploadInstruction(key, "replace", null));
                    break;
                case NameConflictChoice.Rename when renamed.TryGetValue(top, out string? neu):
                    plans.Add(new UploadInstruction(key, key.Contains('/') ? "upload" : "rename", RewriteTop(key, neu)));
                    break;
                default:
                    plans.Add(new UploadInstruction(key, "skip", null));
                    break;
            }
        }

        return plans;
    }

    private static Dictionary<string, bool> NamesOf(IEnumerable<FileSystemItemDto> items)
    {
        var names = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (FileSystemItemDto item in items)
        {
            names.TryAdd(item.Name, item.IsDirectory);
        }

        return names;
    }

    private static string TopSegment(DroppedFile file)
    {
        string path = (file.RelativePath ?? file.Name).Replace('\\', '/');
        int slash = path.IndexOf('/');
        return slash < 0 ? path : path[..slash];
    }

    private static string RewriteTop(string relativePath, string newTop)
    {
        string path = relativePath.Replace('\\', '/');
        int slash = path.IndexOf('/');
        return slash < 0 ? newTop : newTop + path[slash..];
    }

    private static string FreeDisplayName(string name, bool isDirectory, HashSet<string> taken)
    {
        string stem = isDirectory ? name : System.IO.Path.GetFileNameWithoutExtension(name);
        string extension = isDirectory ? string.Empty : System.IO.Path.GetExtension(name);

        for (int i = 2; i < 10_000; i++)
        {
            string candidate = $"{stem} ({i}){extension}";
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }

        string fallback = $"{stem} ({Guid.NewGuid():N}){extension}";
        taken.Add(fallback);
        return fallback;
    }
}
