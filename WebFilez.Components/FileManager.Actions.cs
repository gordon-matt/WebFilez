using Microsoft.AspNetCore.Components.Web;
using WebFilez.Components.Infrastructure;
using WebFilez.Components.Parts;

namespace WebFilez.Components;

public sealed partial class FileManager
{
    // ---------------------------------------------------------------- context menus

    private async Task ShowItemMenuAsync((FileSystemItemDto Item, MouseEventArgs Args) e)
    {
        openMenu = null;

        // Right-clicking outside the selection replaces it: the menu must act on what the user
        // just pointed at.
        if (!selection.Contains(e.Item.Path))
        {
            selection.Clear();
            selection.Add(e.Item.Path);
            rangeAnchor = e.Item.Path;
            await LoadDetailAsync();
        }

        menuX = e.Args.ClientX;
        menuY = e.Args.ClientY;
        contextMenu = BuildSelectionMenu(e.Item);
    }

    private void ShowBackgroundMenu(MouseEventArgs args)
    {
        openMenu = null;
        selection.Clear();
        detail = null;
        menuX = args.ClientX;
        menuY = args.ClientY;
        contextMenu = BuildBackgroundMenu();
    }

    private List<MenuEntry> BuildSelectionMenu(FileSystemItemDto item)
    {
        var entries = new List<MenuEntry>();
        bool single = selection.Count == 1;

        if (single && item.IsDirectory)
        {
            entries.Add(new("Open", "folder-open", () => NavigateAsync(rootId!, item.Path)));
        }
        else if (single)
        {
            if (IsPicker && IsSelectable(item))
            {
                entries.Add(new(SelectLabel, "check-circle", ConfirmSelectionAsync));
            }

            if (FileKinds.CanPreview(item.Kind) && CanOn(item, FilePermission.Read))
            {
                entries.Add(new("Preview", "eye", () => { previewItem = item; return Task.CompletedTask; }));
            }

            if (FileKinds.IsEditable(item.Kind) && CanOn(item, FilePermission.Read))
            {
                entries.Add(new(CanOn(item, FilePermission.Modify) ? "Edit" : "View source", "file-code", () => EditAsync(item)));
            }
        }

        if (CanOnSelection(FilePermission.Download))
        {
            entries.Add(new(single && !item.IsDirectory ? "Download" : "Download as zip", "download", DownloadSelectionAsync));
        }

        if (single && item.PublicUrl is not null)
        {
            entries.Add(new("Copy public URL", "link", () => CopyUrlAsync(item)));
        }

        entries.Add(MenuEntry.Separator);

        if (CanOnSelection(FilePermission.Read) && Can(FilePermission.Create))
        {
            entries.Add(new("Copy", "copy", () => { SetClipboard(isCut: false); return Task.CompletedTask; }, Shortcut: "Ctrl+C"));
        }

        if (CanOnSelection(FilePermission.Move | FilePermission.Delete))
        {
            entries.Add(new("Cut", "scissors", () => { SetClipboard(isCut: true); return Task.CompletedTask; }, Shortcut: "Ctrl+X"));
        }

        if (clipboard is not null && item.IsDirectory && single && CanOn(item, FilePermission.Create))
        {
            entries.Add(new("Paste into folder", "clipboard", () => PasteAsync(item.Path)));
        }

        if (CanOnSelection(FilePermission.Read) && Can(FilePermission.Create))
        {
            entries.Add(new("Duplicate", "copy", DuplicateAsync));
        }

        if (single && CanOn(item, FilePermission.Rename))
        {
            entries.Add(new("Rename", "pencil", RenameAsync, Shortcut: "F2"));
        }

        entries.Add(MenuEntry.Separator);

        if (CanOnSelection(FilePermission.Read) && Can(FilePermission.Create | FilePermission.Compress))
        {
            entries.Add(new("Compress to zip", "package", CompressAsync));
        }

        if (single && item.Kind == FileKind.Archive && CanOn(item, FilePermission.Read) && Can(FilePermission.Create | FilePermission.Extract))
        {
            entries.Add(new("Extract here", "extract", () => ExtractAsync(item)));
        }

        entries.Add(new("Details", "info", ShowDetailsAsync));

        if (CanOnSelection(FilePermission.Delete))
        {
            entries.Add(MenuEntry.Separator);
            entries.Add(new("Delete", "trash", DeleteAsync, IsDanger: true, Shortcut: "Del"));
        }

        return entries;
    }

    private List<MenuEntry> BuildBackgroundMenu()
    {
        var entries = new List<MenuEntry>();

        if (Can(FilePermission.Create))
        {
            entries.Add(new("New folder", "folder-plus", CreateFolderAsync));
            entries.Add(new("New text file", "file-plus", CreateTextFileAsync));
            entries.Add(new("Upload files…", "upload", () => PickFilesAsync(false)));
            entries.Add(new("Upload folder…", "folder-up", () => PickFilesAsync(true)));
        }

        if (clipboard is not null && Can(FilePermission.Create))
        {
            entries.Add(MenuEntry.Separator);
            entries.Add(new($"Paste {Format.Plural(clipboard.Paths.Count, "item")}", "clipboard", () => PasteAsync(), Shortcut: "Ctrl+V"));
        }

        entries.Add(MenuEntry.Separator);

        if (SelectionMode != Constants.SelectionModes.Single)
        {
            entries.Add(new("Select all", "check-circle", () => SelectAllAsync(true), Shortcut: "Ctrl+A"));
        }

        entries.Add(new("Refresh", "refresh", LoadAsync));
        entries.Add(new("Folder details", "info", ShowDetailsAsync));

        return entries;
    }

    private async Task OnContextActionAsync(MenuEntry entry)
    {
        contextMenu = null;
        await entry.OnSelect();
    }

    private async Task ShowDetailsAsync()
    {
        isInfoOpen = true;
        await LoadDetailAsync();
    }

    // ---------------------------------------------------------------------- creating

    private async Task CreateFolderAsync()
    {
        if (prompt is null || rootId is null)
        {
            return;
        }

        string? name = await prompt.ShowAsync("New folder", "Folder name", "New folder", "Create");
        if (name is null)
        {
            return;
        }

        var result = await Service.CreateDirectoryAsync(new CreateDirectoryRequest(rootId, CurrentPath, name));
        if (!result.IsSuccess)
        {
            toasts.ShowFailure(result);
            return;
        }

        toasts.Success($"Created {result.Value.Name}.");
        await RefreshAfterChangeAsync();
        selection.Clear();
        selection.Add(result.Value.Path);
    }

    private async Task CreateTextFileAsync()
    {
        if (prompt is null || rootId is null)
        {
            return;
        }

        string? name = await prompt.ShowAsync("New text file", "File name", "untitled.txt", "Create", selectStemOnly: true);
        if (name is null)
        {
            return;
        }

        var result = await Service.CreateFileAsync(new CreateFileRequest(rootId, CurrentPath, name));
        if (!result.IsSuccess)
        {
            toasts.ShowFailure(result);
            return;
        }

        await LoadAsync();

        // Straight into the editor: an empty file is never the actual goal.
        await EditAsync(result.Value);
    }

    // ------------------------------------------------------------------- downloading

    private Task DownloadSelectionAsync() => DownloadAsync(SelectedItems);

    private async Task DownloadAsync(IReadOnlyList<FileSystemItemDto> items)
    {
        if (items.Count == 0 || rootId is null)
        {
            return;
        }

        // A single file streams directly. Anything else is zipped, because a browser can't save
        // several files from one click.
        if (items is [{ IsDirectory: false } file])
        {
            await interop.DownloadAsync(urls.Download(rootId, file.Path));
            return;
        }

        // The selection goes through a ticket rather than the URL, so thousands of paths still
        // fit and the browser streams the zip straight to disk.
        var ticket = await Service.CreateDownloadTicketAsync(new DownloadTicketRequest(rootId, [.. items.Select(i => i.Path)]));
        if (!ticket.IsSuccess)
        {
            toasts.ShowFailure(ticket);
            return;
        }

        await interop.DownloadAsync(urls.Zip(ticket.Value.Token));
    }

    private async Task CopyUrlAsync(FileSystemItemDto item)
    {
        if (item.PublicUrl is null)
        {
            return;
        }

        // A copied link is pasted elsewhere, so it always needs the full address.
        string absolute = new Uri(new Uri(Navigation.BaseUri), urls.Public(item.PublicUrl)).ToString();
        if (await interop.CopyTextAsync(absolute))
        {
            toasts.Success("Copied the URL.");
        }
        else
        {
            toasts.Error("The browser didn't allow copying to the clipboard.");
        }
    }

    // ------------------------------------------------------------------- clipboard

    private void SetClipboard(bool isCut)
    {
        if (selection.Count == 0 || rootId is null)
        {
            return;
        }

        clipboard = new Clipboard(rootId, [.. selection], isCut);
        toasts.Info($"{Format.Plural(selection.Count, "item")} ready to paste.");
    }

    private async Task PasteAsync(string? destination = null)
    {
        if (clipboard is null || rootId is null)
        {
            return;
        }

        var current = clipboard;
        var result = await Service.TransferAsync(new TransferRequest(
            current.RootId,
            [.. current.Paths],
            rootId,
            destination ?? CurrentPath,
            IsCopy: !current.IsCut));

        if (!result.IsSuccess)
        {
            toasts.ShowFailure(result);
            return;
        }

        toasts.ShowBulkResult(result.Value, current.IsCut ? "Moved" : "Copied");

        // A cut is consumed by the paste; a copy stays so it can be pasted into several folders.
        if (current.IsCut)
        {
            clipboard = null;
        }

        await RefreshAfterChangeAsync();
    }

    private async Task DuplicateAsync()
    {
        if (selection.Count == 0 || rootId is null)
        {
            return;
        }

        var result = await Service.TransferAsync(new TransferRequest(rootId, [.. selection], rootId, CurrentPath, IsCopy: true));
        if (!result.IsSuccess)
        {
            toasts.ShowFailure(result);
            return;
        }

        toasts.ShowBulkResult(result.Value, "Duplicated");
        await RefreshAfterChangeAsync();
    }

    // ---------------------------------------------------------------------- dragging

    private void OnDragStart(FileSystemItemDto item)
    {
        if (!selection.Contains(item.Path))
        {
            selection.Clear();
            selection.Add(item.Path);
            rangeAnchor = item.Path;
        }

        dragPaths = [.. selection];
    }

    /// <summary>
    /// Moves dragged items into a folder, or copies them when Ctrl is held, as in Explorer.
    /// Ignores drops that didn't start in this file manager, such as files from the desktop,
    /// which the upload drop zone handles instead.
    /// </summary>
    private async Task DropOntoAsync(string targetRoot, string targetPath, DragEventArgs args)
    {
        var paths = dragPaths;
        dragPaths = null;

        if (paths is null || paths.Count == 0 || rootId is null)
        {
            return;
        }

        bool sameRoot = targetRoot == rootId;
        if (sameRoot && paths.Any(p => VirtualPath.IsSameOrUnder(targetPath, p) || VirtualPath.GetParent(p) == targetPath))
        {
            return;
        }

        bool isCopy = args.CtrlKey || args.MetaKey || !sameRoot;
        if (!isCopy && !CanOnSelection(FilePermission.Move | FilePermission.Delete))
        {
            toasts.Error("You don't have permission to move these items.");
            return;
        }

        var result = await Service.TransferAsync(new TransferRequest(rootId, paths, targetRoot, targetPath, isCopy));
        if (!result.IsSuccess)
        {
            toasts.ShowFailure(result);
            return;
        }

        toasts.ShowBulkResult(result.Value, isCopy ? "Copied" : "Moved");
        await RefreshAfterChangeAsync();
    }

    // ---------------------------------------------------------------------- changing

    private async Task RenameAsync()
    {
        var item = SelectedItems is [var only] ? only : null;
        if (item is null || prompt is null || rootId is null)
        {
            return;
        }

        string? name = await prompt.ShowAsync("Rename", "New name", item.Name, "Rename", selectStemOnly: !item.IsDirectory);
        if (name is null || name == item.Name)
        {
            return;
        }

        var result = await Service.RenameAsync(new RenameRequest(rootId, item.Path, name));
        if (!result.IsSuccess)
        {
            toasts.ShowFailure(result);
            return;
        }

        toasts.Success($"Renamed to {result.Value.Name}.");
        await RefreshAfterChangeAsync();
        selection.Clear();
        selection.Add(result.Value.Path);
    }

    private async Task DeleteAsync()
    {
        var items = SelectedItems;
        if (items.Count == 0 || confirm is null || rootId is null)
        {
            return;
        }

        string what = items is [var only] ? $"\"{only.Name}\"" : Format.Plural(items.Count, "item");
        bool confirmed = await confirm.ShowAsync(
            $"Delete {what}?",
            items.Any(i => i.IsDirectory)
                ? "Folders are deleted with everything inside them. This can't be undone."
                : "This can't be undone.",
            "Delete",
            isDestructive: true);

        if (!confirmed)
        {
            return;
        }

        var result = await Service.DeleteAsync(new DeleteRequest(rootId, [.. items.Select(i => i.Path)]));
        if (!result.IsSuccess)
        {
            toasts.ShowFailure(result);
            return;
        }

        toasts.ShowBulkResult(result.Value, "Deleted");
        selection.Clear();
        await RefreshAfterChangeAsync();
    }

    private async Task CompressAsync()
    {
        var items = SelectedItems;
        if (items.Count == 0 || prompt is null || rootId is null)
        {
            return;
        }

        string suggested = items is [var only]
            ? $"{(only.IsDirectory ? only.Name : System.IO.Path.GetFileNameWithoutExtension(only.Name))}.zip"
            : $"{(listing?.Path == Constants.RootPath ? listing.RootName : VirtualPath.GetName(CurrentPath))}.zip";

        string? name = await prompt.ShowAsync("Compress", "Archive name", suggested, "Create", selectStemOnly: true);
        if (name is null)
        {
            return;
        }

        var format = name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)
            ? ArchiveFormat.TarGz
            : name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ? ArchiveFormat.Tar : ArchiveFormat.Zip;

        var result = await Service.CompressAsync(new CompressRequest(rootId, [.. items.Select(i => i.Path)], CurrentPath, name, format));
        if (!result.IsSuccess)
        {
            toasts.ShowFailure(result);
            return;
        }

        toasts.Success($"Created {result.Value.Name}.");
        await RefreshAfterChangeAsync();
        selection.Clear();
        selection.Add(result.Value.Path);
    }

    private async Task ExtractAsync(FileSystemItemDto item)
    {
        if (rootId is null)
        {
            return;
        }

        var result = await Service.ExtractAsync(new ExtractRequest(rootId, item.Path));

        if (result.Status == ResultStatus.Conflict && confirm is not null
            && await confirm.ShowAsync("Replace files?", result.GetDisplayMessage(), "Replace", isDestructive: true,
                detail: "Extracting will overwrite the existing files."))
        {
            result = await Service.ExtractAsync(new ExtractRequest(rootId, item.Path, Overwrite: true));
        }

        if (!result.IsSuccess)
        {
            if (result.Status != ResultStatus.Conflict)
            {
                toasts.ShowFailure(result);
            }

            return;
        }

        toasts.ShowBulkResult(result.Value, "Extracted");
        await RefreshAfterChangeAsync();
    }

    /// <summary>Reloads the listing and the tree branch for the current folder after a change.</summary>
    private async Task RefreshAfterChangeAsync()
    {
        await LoadAsync();

        if (tree is not null && rootId is not null)
        {
            await tree.RefreshAsync(rootId, CurrentPath);
        }
    }
}
