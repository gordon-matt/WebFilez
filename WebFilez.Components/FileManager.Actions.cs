using Microsoft.AspNetCore.Components.Web;
using WebFilez.Components.Infrastructure;

namespace WebFilez.Components;

public sealed partial class FileManager
{
    // ---------------------------------------------------------------- context menus

    private async Task ShowItemMenuAsync((ListingEntry Entry, MouseEventArgs Args) e)
    {
        openMenu = null;

        // Right-clicking outside the selection replaces it: the menu must act on what the user
        // just pointed at.
        if (!selection.Contains(e.Entry.Key))
        {
            selection.Clear();
            selection.Add(e.Entry.Key);
            rangeAnchor = e.Entry.Key;
            activeKey = e.Entry.Key;
            await LoadDetailAsync();
        }

        menuX = e.Args.ClientX;
        menuY = e.Args.ClientY;
        contextMenu = await BuildSelectionMenu(e.Entry);
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

    private async Task<List<MenuEntry>> BuildSelectionMenu(ListingEntry entry)
    {
        var item = entry.Item;
        var entries = new List<MenuEntry>();
        bool single = selection.Count == 1;

        if (single && item.IsDirectory)
        {
            entries.Add(new(Text["menu.open"], "folder-open", () => NavigateAsync(entry.RootId, item.Path)));
        }
        else if (single)
        {
            if (IsPicker && IsSelectable(item))
            {
                entries.Add(new(SelectLabel, "check-circle", ConfirmSelectionAsync));
            }

            if (!IsPicker && CanOn(item, FilePermission.Modify))
            {
                foreach (var editor in await EditorsForAsync(item))
                {
                    entries.Add(new(Text.Format("menu.editWith", editor.Name), "pencil", () => OpenExternalEditorAsync(entry.RootId, item, editor)));
                }
            }

            if (FileKinds.CanPreview(item.Kind) && CanOn(item, FilePermission.Read))
            {
                entries.Add(new(Text["menu.preview"], "eye", () => { previewItem = item; previewRoot = entry.RootId; return Task.CompletedTask; }));
            }

            if (FileKinds.IsEditable(item.Kind) && CanOn(item, FilePermission.Read))
            {
                entries.Add(new(CanOn(item, FilePermission.Modify) ? Text["menu.edit"] : Text["menu.viewSource"], "file-code", () => EditAsync(item, entry.RootId)));
            }
        }

        if (CanOnSelection(FilePermission.Download))
        {
            entries.Add(new(single && !item.IsDirectory ? Text["menu.download"] : Text["menu.downloadZip"], "download", DownloadSelectionAsync));
        }

        if (single && item.PublicUrl is not null)
        {
            entries.Add(new(Text["menu.copyUrl"], "link", () => CopyUrlAsync(item)));
        }

        entries.Add(MenuEntry.Separator);

        if (CanOnSelection(FilePermission.Read) && Can(FilePermission.Create))
        {
            entries.Add(new(Text["menu.copy"], "copy", () => { SetClipboard(isCut: false); return Task.CompletedTask; }, Shortcut: "Ctrl+C"));
        }

        if (CanOnSelection(FilePermission.Move | FilePermission.Delete))
        {
            entries.Add(new(Text["menu.cut"], "scissors", () => { SetClipboard(isCut: true); return Task.CompletedTask; }, Shortcut: "Ctrl+X"));
        }

        if (clipboard is not null && item.IsDirectory && single && CanOn(item, FilePermission.Create))
        {
            entries.Add(new(Text["menu.pasteInto"], "clipboard", () => PasteAsync(item.Path, entry.RootId)));
        }

        if (CanOnSelection(FilePermission.Read) && Can(FilePermission.Create))
        {
            entries.Add(new(Text["menu.duplicate"], "copy", DuplicateAsync));
        }

        if (single && CanOn(item, FilePermission.Rename))
        {
            entries.Add(new(Text["menu.rename"], "pencil", RenameAsync, Shortcut: "F2"));
        }

        entries.Add(MenuEntry.Separator);

        if (CanOnSelection(FilePermission.Read) && Can(FilePermission.Create | FilePermission.Compress))
        {
            entries.Add(new(Text["menu.compress"], "package", CompressAsync));
        }

        if (single && item.Kind == FileKind.Archive && CanOn(item, FilePermission.Read) && Can(FilePermission.Create | FilePermission.Extract))
        {
            entries.Add(new(Text["menu.extract"], "extract", () => ExtractAsync(entry)));
        }

        entries.Add(new(Text["menu.details"], "info", ShowDetailsAsync));

        if (CanOnSelection(FilePermission.Delete))
        {
            entries.Add(MenuEntry.Separator);
            entries.Add(new(Text["menu.delete"], "trash", DeleteAsync, IsDanger: true, Shortcut: "Del"));
        }

        return entries;
    }

    private List<MenuEntry> BuildBackgroundMenu()
    {
        var entries = new List<MenuEntry>();

        if (Can(FilePermission.Create))
        {
            entries.Add(new(Text["menu.newFolder"], "folder-plus", CreateFolderAsync));
            entries.Add(new(Text["menu.newTextFile"], "file-plus", CreateTextFileAsync));
            entries.Add(new(Text["menu.uploadFiles"], "upload", () => PickFilesAsync(false)));
            entries.Add(new(Text["menu.uploadFolder"], "folder-up", () => PickFilesAsync(true)));
        }

        if (clipboard is not null && Can(FilePermission.Create))
        {
            entries.Add(MenuEntry.Separator);
            entries.Add(new(Text.Format("menu.paste", Text.Plural("count.items", clipboard.Paths.Count)), "clipboard", () => PasteAsync(), Shortcut: "Ctrl+V"));
        }

        entries.Add(MenuEntry.Separator);

        if (SelectionMode != Constants.SelectionModes.Single)
        {
            entries.Add(new(Text["menu.selectAll"], "check-circle", () => SelectAllAsync(true), Shortcut: "Ctrl+A"));
        }

        entries.Add(new(Text["menu.refresh"], "refresh", RefreshAfterChangeAsync));
        entries.Add(new(Text["menu.folderDetails"], "info", ShowDetailsAsync));

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

        string? name = await prompt.ShowAsync(Text["prompt.newFolder.title"], Text["prompt.newFolder.label"], Text["prompt.newFolder.value"], Text["dialog.create"]);
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

        toasts.Success(Text.Format("toast.created", result.Value.Name));
        await RefreshAfterChangeAsync();
        selection.Clear();
        string createdKey = SelectionKey(rootId, result.Value.Path);
        selection.Add(createdKey);
        activeKey = createdKey;
    }

    private async Task CreateTextFileAsync()
    {
        if (prompt is null || rootId is null)
        {
            return;
        }

        string? name = await prompt.ShowAsync(Text["prompt.newFile.title"], Text["prompt.newFile.label"], "untitled.txt", Text["dialog.create"], selectStemOnly: true);
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

    private Task DownloadSelectionAsync() => ActionRootId is { } itemRoot ? DownloadAsync(itemRoot, SelectedItems) : Task.CompletedTask;

    private async Task DownloadAsync(string itemRoot, IReadOnlyList<FileSystemItemDto> items)
    {
        if (items.Count == 0 || itemRoot.Length == 0)
        {
            return;
        }

        // A single file streams directly. Anything else is zipped, because a browser can't save
        // several files from one click.
        if (items is [{ IsDirectory: false } file])
        {
            await interop.DownloadAsync(urls.Download(itemRoot, file.Path));
            return;
        }

        // The selection goes through a ticket rather than the URL, so thousands of paths still
        // fit and the browser streams the zip straight to disk.
        var ticket = await Service.CreateDownloadTicketAsync(new DownloadTicketRequest(itemRoot, [.. items.Select(i => i.Path)]));
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
            toasts.Success(Text["toast.copiedUrl"]);
        }
        else
        {
            toasts.Error(Text["toast.copyDenied"]);
        }
    }

    // ------------------------------------------------------------------- clipboard

    private void SetClipboard(bool isCut)
    {
        var rows = SelectedRows();
        if (rows.Count == 0 || ActionRootId is null)
        {
            return;
        }

        clipboard = new Clipboard(ActionRootId, [.. rows.Select(row => row.Item.Path)], isCut);
        toasts.Info(Text.Plural("toast.ready", rows.Count));
    }

    private async Task PasteAsync(string? destination = null, string? destinationRoot = null)
    {
        if (clipboard is null || rootId is null)
        {
            return;
        }

        var current = clipboard;
        string targetRoot = destinationRoot ?? rootId;
        var result = await Service.TransferAsync(new TransferRequest(
            current.RootId,
            [.. current.Paths],
            targetRoot,
            destination ?? CurrentPath,
            IsCopy: !current.IsCut));

        if (!result.IsSuccess)
        {
            toasts.ShowFailure(result);
            return;
        }

        ShowBulk(result.Value, current.IsCut ? "moved" : "copied");

        // A cut is consumed by the paste; a copy stays so it can be pasted into several folders.
        if (current.IsCut)
        {
            clipboard = null;
        }

        await RefreshAfterChangeAsync();
    }

    private async Task DuplicateAsync()
    {
        var rows = SelectedRows();
        if (rows.Count == 0 || ActionRootId is null)
        {
            return;
        }

        string destination = CurrentPath;
        if (ShowingSearch)
        {
            var parents = rows.Select(row => VirtualPath.GetParent(row.Item.Path) ?? Constants.RootPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (parents.Count == 1)
            {
                destination = parents[0];
            }
        }

        var result = await Service.TransferAsync(new TransferRequest(ActionRootId, [.. rows.Select(row => row.Item.Path)], ActionRootId, destination, IsCopy: true));
        if (!result.IsSuccess)
        {
            toasts.ShowFailure(result);
            return;
        }

        ShowBulk(result.Value, "duplicated");
        await RefreshAfterChangeAsync();
    }

    // ---------------------------------------------------------------------- dragging

    private void OnDragStart(ListingEntry entry)
    {
        if (!selection.Contains(entry.Key))
        {
            selection.Clear();
            selection.Add(entry.Key);
            rangeAnchor = entry.Key;
            activeKey = entry.Key;
        }

        var rows = SelectedRows();
        bool sameRoot = rows.Count > 0 && rows.All(row => string.Equals(row.RootId, entry.RootId, StringComparison.OrdinalIgnoreCase));
        var dragged = sameRoot ? rows : [entry];
        dragPaths = dragged.Select(row => row.Item.Path).ToList();
        dragRootId = entry.RootId;
        dragCanMove = dragged.All(row => CanOn(row.Item, FilePermission.Move | FilePermission.Delete));
    }

    /// <summary>
    /// Moves dragged items into a folder, or copies them when Ctrl is held, as in Explorer.
    /// Ignores drops that didn't start in this file manager, such as files from the desktop,
    /// which the upload drop zone handles instead.
    /// </summary>
    private async Task DropOntoAsync(string targetRoot, string targetPath, DragEventArgs args)
    {
        var paths = dragPaths;
        string? sourceRoot = dragRootId;
        dragPaths = null;
        dragRootId = null;

        if (paths is null || paths.Count == 0 || sourceRoot is null)
        {
            return;
        }

        bool sameRoot = targetRoot == sourceRoot;
        if (sameRoot && paths.Any(p => VirtualPath.IsSameOrUnder(targetPath, p) || VirtualPath.GetParent(p) == targetPath))
        {
            return;
        }

        bool isCopy = args.CtrlKey || args.MetaKey || !sameRoot;
        if (!isCopy && !dragCanMove)
        {
            toasts.Error(Text["toast.noMove"]);
            return;
        }

        var result = await Service.TransferAsync(new TransferRequest(sourceRoot, paths, targetRoot, targetPath, isCopy));
        if (!result.IsSuccess)
        {
            toasts.ShowFailure(result);
            return;
        }

        ShowBulk(result.Value, isCopy ? "copied" : "moved");
        await RefreshAfterChangeAsync();
    }

    // ---------------------------------------------------------------------- changing

    private async Task RenameAsync()
    {
        var item = SelectedItems is [var only] ? only : null;
        if (item is null || prompt is null || ActionRootId is not { } itemRoot)
        {
            return;
        }

        string? name = await prompt.ShowAsync(Text["prompt.rename.title"], Text["prompt.rename.label"], item.Name, Text["dialog.rename"], selectStemOnly: !item.IsDirectory);
        if (name is null || name == item.Name)
        {
            return;
        }

        var result = await Service.RenameAsync(new RenameRequest(itemRoot, item.Path, name));
        if (!result.IsSuccess)
        {
            toasts.ShowFailure(result);
            return;
        }

        toasts.Success(Text.Format("toast.renamed", result.Value.Name));
        await RefreshAfterChangeAsync();
        selection.Clear();
        string renamedKey = SelectionKey(itemRoot, result.Value.Path);
        selection.Add(renamedKey);
        activeKey = renamedKey;
    }

    private async Task DeleteAsync()
    {
        var items = SelectedItems;
        if (items.Count == 0 || confirm is null || ActionRootId is not { } itemRoot)
        {
            return;
        }

        string what = items is [var only] ? $"\"{only.Name}\"" : Text.Plural("count.items", items.Count);
        bool confirmed = await confirm.ShowAsync(
            Text.Format("confirm.delete.title", what),
            items.Any(i => i.IsDirectory) ? Text["confirm.delete.folder"] : Text["confirm.delete.file"],
            Text["dialog.delete"],
            isDestructive: true);

        if (!confirmed)
        {
            return;
        }

        var result = await Service.DeleteAsync(new DeleteRequest(itemRoot, [.. items.Select(i => i.Path)]));
        if (!result.IsSuccess)
        {
            toasts.ShowFailure(result);
            return;
        }

        ShowBulk(result.Value, "deleted");
        selection.Clear();
        await RefreshAfterChangeAsync();
    }

    private async Task CompressAsync()
    {
        var rows = SelectedRows();
        var items = SelectedItems;
        if (items.Count == 0 || prompt is null || ActionRootId is not { } itemRoot)
        {
            return;
        }

        string destination = CurrentPath;
        if (ShowingSearch)
        {
            var parents = rows.Select(row => VirtualPath.GetParent(row.Item.Path) ?? Constants.RootPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (parents.Count == 1)
            {
                destination = parents[0];
            }
        }

        string suggested = items is [var only]
            ? $"{(only.IsDirectory ? only.Name : System.IO.Path.GetFileNameWithoutExtension(only.Name))}.zip"
            : $"{(listing?.Path == Constants.RootPath ? listing.RootName : VirtualPath.GetName(destination))}.zip";

        string? name = await prompt.ShowAsync(Text["prompt.compress.title"], Text["prompt.compress.label"], suggested, Text["dialog.create"], selectStemOnly: true);
        if (name is null)
        {
            return;
        }

        var format = name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)
            ? ArchiveFormat.TarGz
            : name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ? ArchiveFormat.Tar : ArchiveFormat.Zip;

        var result = await Service.CompressAsync(new CompressRequest(itemRoot, [.. items.Select(i => i.Path)], destination, name, format));
        if (!result.IsSuccess)
        {
            toasts.ShowFailure(result);
            return;
        }

        toasts.Success(Text.Format("toast.archiveCreated", result.Value.Name));
        await RefreshAfterChangeAsync();
        selection.Clear();
        string archiveKey = SelectionKey(itemRoot, result.Value.Path);
        selection.Add(archiveKey);
        activeKey = archiveKey;
    }

    private async Task ExtractAsync(ListingEntry entry)
    {
        var result = await Service.ExtractAsync(new ExtractRequest(entry.RootId, entry.Item.Path));

        if (result.Status == ResultStatus.Conflict && confirm is not null
            && await confirm.ShowAsync(Text["confirm.replace.title"], result.GetDisplayMessage(), Text["dialog.replace"], isDestructive: true,
                detail: Text["confirm.replace.extract"]))
        {
            result = await Service.ExtractAsync(new ExtractRequest(entry.RootId, entry.Item.Path, Overwrite: true));
        }

        if (!result.IsSuccess)
        {
            if (result.Status != ResultStatus.Conflict)
            {
                toasts.ShowFailure(result);
            }

            return;
        }

        ShowBulk(result.Value, "extracted");
        await RefreshAfterChangeAsync();
    }

    /// <summary>Reloads the listing and, while a search is showing, the matches as well.</summary>
    private async Task RefreshAfterChangeAsync()
    {
        await LoadAsync();
        if (ShowingSearch)
        {
            await RunSearchAsync();
        }

        if (tree is not null && rootId is not null)
        {
            await tree.RefreshAsync(rootId, CurrentPath);
        }
    }

    private void ShowBulk(BulkOperationResultDto result, string verb)
    {
        if (result.FailedCount == 0)
        {
            toasts.Success(Text.Plural($"toast.{verb}", result.SucceededCount));
            return;
        }

        var first = result.Results.First(operation => !operation.Succeeded);
        string reason = $"{VirtualPath.GetName(VirtualPath.Normalize(first.Path))}: {first.Error}";
        toasts.Error(result.SucceededCount == 0
            ? Text.Format($"toast.{verb}.none", reason)
            : Text.Format($"toast.{verb}.partial", result.SucceededCount, result.FailedCount, reason));
    }
}