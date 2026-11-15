namespace WebFilez.Components.History;

/// <summary>One reversible change. Undo and redo call the same service the original gesture did.</summary>
internal abstract class HistoryAction
{
    public abstract Task<HistoryOutcome> UndoAsync(IFileManagerService service, CancellationToken cancellationToken = default);

    public abstract Task<HistoryOutcome> RedoAsync(IFileManagerService service, CancellationToken cancellationToken = default);
}

/// <summary>Whether a history step applied. <see cref="Blocked"/> means the item changed and was left alone.</summary>
internal readonly record struct HistoryOutcome(bool Succeeded, string? Error, bool Blocked)
{
    public static HistoryOutcome Ok() => new(true, null, false);

    public static HistoryOutcome Fail(string? error) => new(false, error, false);

    public static HistoryOutcome Refuse() => new(false, null, true);

    public static HistoryOutcome From(IResult result) =>
        result.IsSuccessStatus() ? Ok() : Fail(result.GetDisplayMessage());

    public static HistoryOutcome FromBulk(Result<BulkOperationResultDto> result)
    {
        if (!result.IsSuccess)
        {
            return Fail(result.GetDisplayMessage());
        }

        if (result.Value.IsCompleteSuccess)
        {
            return Ok();
        }

        return Fail(result.Value.Results.FirstOrDefault(row => !row.Succeeded)?.Error);
    }
}

/// <summary>A file or folder that was created. Undo removes it only while it is still empty.</summary>
internal sealed class CreateAction(string rootId, string parentPath, string name, bool isDirectory, string createdPath) : HistoryAction
{
    public override async Task<HistoryOutcome> UndoAsync(IFileManagerService service, CancellationToken cancellationToken)
    {
        if (isDirectory)
        {
            var children = await service.ListAsync(
                new ListingRequest(rootId, createdPath, ShowHidden: true, PageSize: 1),
                cancellationToken);
            if (!children.IsSuccess)
            {
                return HistoryOutcome.From(children);
            }

            // Files added after the folder was created are not on the undo stack. Deleting the
            // folder would take them with it, so the folder stays until it is empty again.
            if (children.Value.TotalCount > 0)
            {
                return HistoryOutcome.Refuse();
            }
        }
        else
        {
            var detail = await service.GetDetailAsync(rootId, createdPath, cancellationToken);
            if (!detail.IsSuccess)
            {
                return HistoryOutcome.From(detail);
            }

            if (detail.Value.SizeBytes > 0)
            {
                return HistoryOutcome.Refuse();
            }
        }

        return HistoryOutcome.FromBulk(await service.DeleteAsync(new DeleteRequest(rootId, [createdPath]), cancellationToken));
    }

    public override async Task<HistoryOutcome> RedoAsync(IFileManagerService service, CancellationToken cancellationToken)
    {
        if (isDirectory)
        {
            var created = await service.CreateDirectoryAsync(new CreateDirectoryRequest(rootId, parentPath, name), cancellationToken);
            if (!created.IsSuccess)
            {
                return HistoryOutcome.From(created);
            }

            createdPath = created.Value.Path;
            return HistoryOutcome.Ok();
        }

        var file = await service.CreateFileAsync(new CreateFileRequest(rootId, parentPath, name), cancellationToken);
        if (!file.IsSuccess)
        {
            return HistoryOutcome.From(file);
        }

        createdPath = file.Value.Path;
        return HistoryOutcome.Ok();
    }
}

/// <summary>A rename. The paths are updated after each step because the name policy may adjust them.</summary>
internal sealed class RenameAction(string rootId, string fromPath, string fromName, string toPath, string toName) : HistoryAction
{
    public override async Task<HistoryOutcome> UndoAsync(IFileManagerService service, CancellationToken cancellationToken)
    {
        var renamed = await service.RenameAsync(new RenameRequest(rootId, toPath, fromName), cancellationToken);
        if (!renamed.IsSuccess)
        {
            return HistoryOutcome.From(renamed);
        }

        fromPath = renamed.Value.Path;
        fromName = renamed.Value.Name;
        return HistoryOutcome.Ok();
    }

    public override async Task<HistoryOutcome> RedoAsync(IFileManagerService service, CancellationToken cancellationToken)
    {
        var renamed = await service.RenameAsync(new RenameRequest(rootId, fromPath, toName), cancellationToken);
        if (!renamed.IsSuccess)
        {
            return HistoryOutcome.From(renamed);
        }

        toPath = renamed.Value.Path;
        toName = renamed.Value.Name;
        return HistoryOutcome.Ok();
    }
}

/// <summary>One item in a copy or move. <see cref="ResultPath"/> is where it landed, which a duplicate does not share with the source.</summary>
internal sealed class TransferredItem(string sourcePath, string resultPath)
{
    public string SourcePath { get; } = sourcePath;

    public string ResultPath { get; set; } = resultPath;
}

/// <summary>A copy, duplicate, or move. Undo of a copy deletes the new items. Undo of a move puts them back.</summary>
internal sealed class TransferAction(
    string sourceRootId,
    string destinationRootId,
    string destinationPath,
    bool isCopy,
    List<TransferredItem> items) : HistoryAction
{
    public override Task<HistoryOutcome> UndoAsync(IFileManagerService service, CancellationToken cancellationToken) =>
        isCopy ? DeleteCopiesAsync(service, cancellationToken) : MoveBackAsync(service, cancellationToken);

    public override async Task<HistoryOutcome> RedoAsync(IFileManagerService service, CancellationToken cancellationToken)
    {
        var moved = await service.TransferAsync(
            new TransferRequest(sourceRootId, items.Select(item => item.SourcePath).ToList(), destinationRootId, destinationPath, isCopy),
            cancellationToken);
        var outcome = HistoryOutcome.FromBulk(moved);
        if (!moved.IsSuccess)
        {
            return outcome;
        }

        if (!outcome.Succeeded)
        {
            await RollBackPartialAsync(service, moved.Value, cancellationToken);
            return outcome;
        }

        foreach (var row in moved.Value.Results)
        {
            if (!row.Succeeded || row.ResultPath is null)
            {
                continue;
            }

            var item = items.FirstOrDefault(candidate =>
                string.Equals(candidate.SourcePath, row.Path, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
            {
                item.ResultPath = row.ResultPath;
            }
        }

        return HistoryOutcome.Ok();
    }

    private async Task<HistoryOutcome> DeleteCopiesAsync(IFileManagerService service, CancellationToken cancellationToken)
    {
        // A result path equal to its source is the original. Deleting it would remove the file
        // the copy was taken from.
        var paths = items
            .Where(item => !string.Equals(item.SourcePath, item.ResultPath, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ResultPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0)
        {
            return HistoryOutcome.Ok();
        }

        return HistoryOutcome.FromBulk(await service.DeleteAsync(new DeleteRequest(destinationRootId, paths), cancellationToken));
    }

    private async Task<HistoryOutcome> MoveBackAsync(IFileManagerService service, CancellationToken cancellationToken)
    {
        var groups = items
            .GroupBy(item => VirtualPath.GetParent(item.SourcePath) ?? Constants.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var done = new List<IGrouping<string, TransferredItem>>();

        foreach (var group in groups)
        {
            var moved = await service.TransferAsync(
                new TransferRequest(
                    destinationRootId,
                    group.Select(item => item.ResultPath).ToList(),
                    sourceRootId,
                    group.Key,
                    IsCopy: false),
                cancellationToken);
            var outcome = HistoryOutcome.FromBulk(moved);
            if (!outcome.Succeeded)
            {
                foreach (var finished in done)
                {
                    await service.TransferAsync(
                        new TransferRequest(
                            sourceRootId,
                            finished.Select(item => item.SourcePath).ToList(),
                            destinationRootId,
                            destinationPath,
                            IsCopy: false),
                        cancellationToken);
                }

                return outcome;
            }

            done.Add(group);
        }

        return HistoryOutcome.Ok();
    }

    private async Task RollBackPartialAsync(IFileManagerService service, BulkOperationResultDto result, CancellationToken cancellationToken)
    {
        var written = result.Results.Where(row => row.Succeeded && !string.IsNullOrEmpty(row.ResultPath)).ToList();
        if (written.Count == 0)
        {
            return;
        }

        if (isCopy)
        {
            await service.DeleteAsync(
                new DeleteRequest(destinationRootId, written.Select(row => row.ResultPath!).ToList()),
                cancellationToken);
            return;
        }

        var groups = written.GroupBy(
            row =>
            {
                var item = items.FirstOrDefault(candidate =>
                    string.Equals(candidate.SourcePath, row.Path, StringComparison.OrdinalIgnoreCase));
                return VirtualPath.GetParent(item?.SourcePath ?? row.Path) ?? Constants.RootPath;
            },
            StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            await service.TransferAsync(
                new TransferRequest(
                    destinationRootId,
                    group.Select(row => row.ResultPath!).ToList(),
                    sourceRootId,
                    group.Key,
                    IsCopy: false),
                cancellationToken);
        }
    }
}

/// <summary>
/// One save from the text editor. The bytes are checked before they are replaced, so a save that
/// happened somewhere else is left alone. The timestamp guard is skipped because an earlier undo
/// has already written the file and moved that timestamp.
/// </summary>
internal sealed class TextEditAction(string rootId, string path, string before, string after) : HistoryAction
{
    public override Task<HistoryOutcome> UndoAsync(IFileManagerService service, CancellationToken cancellationToken) =>
        ReplaceAsync(service, expected: after, replacement: before, cancellationToken);

    public override Task<HistoryOutcome> RedoAsync(IFileManagerService service, CancellationToken cancellationToken) =>
        ReplaceAsync(service, expected: before, replacement: after, cancellationToken);

    private async Task<HistoryOutcome> ReplaceAsync(
        IFileManagerService service,
        string expected,
        string replacement,
        CancellationToken cancellationToken)
    {
        var current = await service.ReadTextAsync(rootId, path, cancellationToken);
        if (!current.IsSuccess)
        {
            return HistoryOutcome.From(current);
        }

        if (!string.Equals(current.Value.Content, expected, StringComparison.Ordinal))
        {
            return HistoryOutcome.Refuse();
        }

        var saved = await service.SaveTextAsync(new SaveTextFileRequest(rootId, path, replacement), cancellationToken);
        return HistoryOutcome.From(saved);
    }
}
