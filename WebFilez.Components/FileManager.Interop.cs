using WebFilez.Components.Infrastructure;
using WebFilez.Components.Parts;

namespace WebFilez.Components;

/// <summary>Callbacks from <c>webfilez.js</c>: keyboard shortcuts and uploads.</summary>
public sealed partial class FileManager
{
    private readonly HashSet<long> cancelledUploads = [];

    private Task PickFilesAsync(bool directories) => !Can(FilePermission.Create)
            ? Task.CompletedTask
            : interop.PickFilesAsync(rootElement, directories, IsPicker ? Accept : null).AsTask();

    /// <summary>Runs a keyboard shortcut from the browser. Escape closes a menu before it clears the selection.</summary>
    /// <param name="combo">The key combination, such as <c>ctrl+c</c> or <c>F2</c>.</param>
    /// <returns>A task that completes when the shortcut has been applied.</returns>
    [JSInvokable]
    public async Task OnShortcut(string combo)
    {
        // Escape closes whatever is open before touching the selection, so one press does the
        // least surprising thing rather than several at once.
        if (combo == "Escape")
        {
            if (contextMenu is not null || openMenu is not null || isLanguageOpen)
            {
                contextMenu = null;
                openMenu = null;
                isLanguageOpen = false;
            }
            else
            {
                selection.Clear();
                detail = null;
            }

            StateHasChanged();
            return;
        }

        if (IsMovement(combo))
        {
            if (contextMenu is null && openMenu is null)
            {
                await MoveSelectionAsync(combo);
            }

            StateHasChanged();
            return;
        }

        if (combo is "Space" or "shift+Space")
        {
            if (contextMenu is null && openMenu is null)
            {
                await ToggleActiveAsync(combo.StartsWith("shift+", StringComparison.Ordinal));
            }

            StateHasChanged();
            return;
        }

        switch (combo)
        {
            case "ctrl+a" when SelectionMode != Constants.SelectionModes.Single:
                await SelectAllAsync(true);
                break;

            case "shift+ctrl+i" when SelectionMode != Constants.SelectionModes.Single:
                await InvertSelectionAsync();
                break;

            case "Delete" when CanOnSelection(FilePermission.Delete):
                await DeleteAsync();
                break;

            case "F2" when selection.Count == 1 && CanOnSelection(FilePermission.Rename):
                await RenameAsync();
                break;

            case "ctrl+c" when CanOnSelection(FilePermission.Read) && Can(FilePermission.Create):
                SetClipboard(isCut: false);
                break;

            case "ctrl+x" when CanOnSelection(FilePermission.Move | FilePermission.Delete):
                SetClipboard(isCut: true);
                break;

            case "ctrl+v" when clipboard is not null && Can(FilePermission.Create):
                await PasteAsync();
                break;

            case "Enter":
                var row = Rows().FirstOrDefault(candidate => candidate.Key == activeKey);
                if (row is not null)
                {
                    await ActivateAsync(row);
                }

                break;

            case "Backspace":
                await GoUpAsync();
                break;

            case "alt+ArrowLeft":
                await GoBackAsync();
                break;

            case "alt+ArrowRight":
                await GoForwardAsync();
                break;

            case "ctrl+z" when UndoSupported:
                await UndoAsync();
                break;

            case "ctrl+y" or "shift+ctrl+z" when UndoSupported:
                await RedoAsync();
                break;
        }

        StateHasChanged();
    }

    /// <summary>
    /// Starts an upload of the dropped files. A name that already exists is asked about first.
    /// Files inside a folder that does not exist yet are not asked. Cancel skips the names not yet decided.
    /// </summary>
    /// <param name="files">The dropped files, including a relative folder when one was dropped.</param>
    /// <returns>A task that completes once the browser has been told which files to send.</returns>
    [JSInvokable]
    public async Task OnFilesDropped(DroppedFile[] files)
    {
        if (files.Length == 0 || rootId is null)
        {
            return;
        }

        if (!Can(FilePermission.Create))
        {
            toasts.Error(Text["toast.noUpload"]);
            StateHasChanged();
            return;
        }

        // Each top-level name that is already here is asked separately. Apply-to-rest covers a
        // drop of many files. Files inside a new folder are not asked: that folder does not
        // exist yet. Cancel skips the names not yet decided and still uploads the rest.
        var plans = await PlanUploadsAsync(files);
        await interop.UploadPendingAsync(rootElement, urls.Upload, rootId, CurrentPath, plans, urls.IncludeCredentials);
    }

    /// <summary>Updates the progress of one upload. A cancelled upload is ignored.</summary>
    /// <param name="id">The upload id assigned by the browser.</param>
    /// <param name="fileName">The name shown beside the progress.</param>
    /// <param name="sent">How many bytes have been sent.</param>
    /// <param name="total">The file size, in bytes.</param>
    [JSInvokable]
    public void OnUploadProgress(long id, string fileName, long sent, long total)
    {
        if (cancelledUploads.Contains(id))
        {
            return;
        }

        var upload = FindOrAddUpload(id, fileName);
        upload.BytesSent = sent;
        upload.TotalBytes = total;

        StateHasChanged();
    }

    /// <summary>Marks one upload finished. A cancelled upload is removed and not shown as a failure.</summary>
    /// <param name="id">The upload id.</param>
    /// <param name="fileName">The name shown beside the result.</param>
    /// <param name="succeeded">Whether the server stored the file.</param>
    /// <param name="error">Why it failed, or <see langword="null"/> when it succeeded.</param>
    [JSInvokable]
    public void OnUploadComplete(long id, string fileName, bool succeeded, string? error)
    {
        if (cancelledUploads.Remove(id))
        {
            return;
        }

        var upload = FindOrAddUpload(id, fileName);
        upload.IsComplete = true;
        upload.Error = succeeded ? null : error ?? Text["toast.uploadFailed"];

        StateHasChanged();
    }

    /// <summary>Shows the result of the batch and refreshes the folder. Failures stay on screen. A fully cancelled batch says nothing.</summary>
    /// <returns>A task that completes once the listing has been refreshed.</returns>
    [JSInvokable]
    public async Task OnUploadBatchComplete()
    {
        int failed = uploads.Count(u => u.Error is not null);
        int succeeded = uploads.Count - failed;

        if (uploads.Count == 0)
        {
            // Everything was cancelled; there's nothing to report.
        }
        else if (failed == 0)
        {
            toasts.Success(Text.Plural("toast.uploaded", succeeded));

            // Cleared only on full success: a failure list should stay until it has been read.
            uploads.Clear();
        }
        else
        {
            toasts.Error(Text.Format("toast.uploadBatchFailed", failed, succeeded + failed));
        }

        await RefreshAfterChangeAsync();
        StateHasChanged();
    }

    private async Task CancelUploadAsync(long id)
    {
        cancelledUploads.Add(id);
        await interop.CancelUploadAsync(rootElement, id);
        uploads.RemoveAll(u => u.Id == id);
    }

    private static bool IsMovement(string combo) =>
        combo is "ArrowUp" or "ArrowDown" or "ArrowLeft" or "ArrowRight" or "Home" or "End"
            or "shift+ArrowUp" or "shift+ArrowDown" or "shift+ArrowLeft" or "shift+ArrowRight" or "shift+Home" or "shift+End"
            or "ctrl+ArrowUp" or "ctrl+ArrowDown" or "ctrl+ArrowLeft" or "ctrl+ArrowRight" or "ctrl+Home" or "ctrl+End";

    private UploadTray.UploadProgress FindOrAddUpload(long id, string fileName)
    {
        var upload = uploads.FirstOrDefault(u => u.Id == id);
        if (upload is null)
        {
            upload = new UploadTray.UploadProgress { Id = id, FileName = fileName };
            uploads.Add(upload);
        }

        return upload;
    }

    /// <summary>A dropped or picked file, described before any bytes are sent.</summary>
    public sealed record DroppedFile(string Name, long Size, string? RelativePath);
}