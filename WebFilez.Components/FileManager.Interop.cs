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

    [JSInvokable]
    public async Task OnShortcut(string combo)
    {
        // Escape closes whatever is open before touching the selection, so one press does the
        // least surprising thing rather than several at once.
        if (combo == "Escape")
        {
            if (contextMenu is not null || openMenu is not null)
            {
                contextMenu = null;
                openMenu = null;
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
        }

        StateHasChanged();
    }

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

        // Overwrite is asked once for the whole batch; asking per file would be unusable for
        // the folder drops this is built to handle. Only top-level names can clash with what is
        // listed; files inside a dropped folder land in a folder that may not exist yet.
        var existing = listing?.Items.Where(i => !i.IsDirectory).Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var collisions = files.Where(f => !(f.RelativePath?.Contains('/') ?? false) && existing.Contains(f.Name)).ToList();
        bool overwrite = false;

        if (collisions.Count > 0 && confirm is not null)
        {
            if (!Can(FilePermission.Modify))
            {
                toasts.Info(collisions.Count == 1
                    ? Text.Format("confirm.uploadSkipOne", collisions[0].Name)
                    : Text.Format("confirm.uploadSkipMany", collisions.Count));
            }
            else
            {
                overwrite = await confirm.ShowAsync(
                    Text["confirm.replace.title"],
                    collisions.Count == 1
                        ? Text.Format("confirm.uploadReplaceOne", collisions[0].Name)
                        : Text.Format("confirm.uploadReplaceMany", collisions.Count),
                    Text["dialog.replace"],
                    isDestructive: true,
                    detail: Text["confirm.replace.uploadDetail"]);
            }
        }

        await interop.UploadPendingAsync(rootElement, urls.Upload, rootId, CurrentPath, overwrite, urls.IncludeCredentials);
    }

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