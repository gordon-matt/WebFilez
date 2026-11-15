using WebFilez.Components.History;

namespace WebFilez.Components;

/// <summary>One text save: the bytes that were replaced, and the bytes that replaced them.</summary>
public sealed record TextEdit(string RootId, string Path, string PreviousContent, string Content);

public sealed partial class FileManager
{
    private const int MaxUndoTextChars = 256 * 1024;

    private readonly NavigationHistory navigation = new();
    private readonly EditHistory edits = new();

    private bool CanGoBack => navigation.CanBack;

    private bool CanGoForward => navigation.CanForward;

    private bool CanUndo => edits.CanUndo;

    private bool CanRedo => edits.CanRedo;

    private async Task GoBackAsync()
    {
        if (navigation.Back() is not { } place)
        {
            return;
        }

        await OpenHistoryPlaceAsync(place);
    }

    private async Task GoForwardAsync()
    {
        if (navigation.Forward() is not { } place)
        {
            return;
        }

        await OpenHistoryPlaceAsync(place);
    }

    private async Task OpenHistoryPlaceAsync(BrowsePlace place)
    {
        if (ShowingSearch)
        {
            await LeaveSearchAsync();
        }

        await OpenAsync(place.RootId, place.Path, notify: true);
        if (rootId is not null && listing is not null)
        {
            navigation.MoveCurrent(new BrowsePlace(rootId, listing.Path));
        }
    }

    private void RememberVisit(BrowsePlace? leaving)
    {
        if (rootId is null || listing is null)
        {
            return;
        }

        navigation.Visit(leaving, new BrowsePlace(rootId, listing.Path));
    }

    private async Task UndoAsync()
    {
        if (edits.TryUndo() is not { } action)
        {
            return;
        }

        var outcome = await action.UndoAsync(Service);
        if (!outcome.Succeeded)
        {
            edits.RevertUndo();
            ShowHistoryFailure(outcome);
            return;
        }

        toasts.Success(Text["toast.undone"]);
        await RefreshAfterChangeAsync();
    }

    private async Task RedoAsync()
    {
        if (edits.TryRedo() is not { } action)
        {
            return;
        }

        var outcome = await action.RedoAsync(Service);
        if (!outcome.Succeeded)
        {
            edits.RevertRedo();
            ShowHistoryFailure(outcome);
            return;
        }

        toasts.Success(Text["toast.redone"]);
        await RefreshAfterChangeAsync();
    }

    private void ShowHistoryFailure(HistoryOutcome outcome) =>
        toasts.Error(outcome.Blocked ? Text["toast.historyBlocked"] : outcome.Error ?? Text["toast.historyBlocked"]);

    private void RememberCreate(string itemRoot, FileSystemItemDto created, bool isDirectory)
    {
        string parent = VirtualPath.GetParent(created.Path) ?? Constants.RootPath;
        edits.Push(new CreateAction(itemRoot, parent, created.Name, isDirectory, created.Path));
    }

    private void RememberRename(string itemRoot, FileSystemItemDto before, FileSystemItemDto after) =>
        edits.Push(new RenameAction(itemRoot, before.Path, before.Name, after.Path, after.Name));

    private void RememberTransfer(TransferRequest request, BulkOperationResultDto result)
    {
        var transferred = new List<TransferredItem>();
        foreach (var row in result.Results)
        {
            if (!row.Succeeded || string.IsNullOrEmpty(row.ResultPath))
            {
                continue;
            }

            if (string.Equals(row.Path, row.ResultPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            transferred.Add(new TransferredItem(row.Path, row.ResultPath));
        }

        if (transferred.Count == 0)
        {
            return;
        }

        edits.Push(new TransferAction(
            request.RootId,
            request.DestinationRootId,
            request.DestinationPath,
            request.IsCopy,
            transferred));
    }

    private async Task OnTextSavedAsync(TextEdit edit)
    {
        if (edit.PreviousContent.Length <= MaxUndoTextChars && edit.Content.Length <= MaxUndoTextChars)
        {
            edits.Push(new TextEditAction(edit.RootId, edit.Path, edit.PreviousContent, edit.Content));
        }

        await RefreshAfterChangeAsync();
    }
}
