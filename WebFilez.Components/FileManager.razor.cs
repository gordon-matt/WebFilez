using System.Text.Json;
using Microsoft.AspNetCore.Components.Web;
using WebFilez.Components.Http;
using WebFilez.Components.Infrastructure;
using WebFilez.Components.Parts;
using WebFilez.Components.Shared;

namespace WebFilez.Components;

/// <summary>
/// The file manager. Works as a full file manager, or as a file picker when
/// <see cref="SelectionMode"/> is <c>single</c> or <c>multiple</c>. Parameters are strings and
/// booleans so the same component can be registered as the <c>&lt;wf-file-manager&gt;</c>
/// custom element.
/// </summary>
public sealed partial class FileManager
{
    private static readonly (ListingViewMode Mode, string Icon, string Label)[] ViewModes =
    [
        (ListingViewMode.Tiles, "grid", "Tiles"),
        (ListingViewMode.List, "list", "List"),
        (ListingViewMode.Compact, "compact", "Compact"),
        (ListingViewMode.Gallery, "gallery", "Gallery"),
    ];

    private static readonly (KindFilter Filter, string Icon, string Label)[] KindFilters =
    [
        (KindFilter.All, "folder", "All"),
        (KindFilter.Images, "image", "Images"),
        (KindFilter.Documents, "file-text", "Documents"),
        (KindFilter.Media, "music", "Media"),
        (KindFilter.Archives, "archive", "Archives"),
    ];

    private readonly HashSet<string> selection = new(StringComparer.Ordinal);
    private readonly List<UploadTray.UploadProgress> uploads = [];
    private readonly ToastService toasts = new();

    private WebFilezInterop interop = default!;
    private WebFilezUrls urls = default!;
    private DotNetObjectReference<FileManager>? callbackReference;
    private ElementReference rootElement;
    private ElementReference listingElement;
    private bool isAttached;

    private IReadOnlyList<FileRootDto> roots = [];
    private string? rootId;
    private DirectoryListingDto? listing;
    private string? loadError;
    private (string? Root, string? Path)? appliedParameters;

    private ListingViewMode viewMode;
    private ListingSortField sortBy = ListingSortField.Name;
    private bool descending;
    private bool showHidden;
    private string filter = string.Empty;
    private KindFilter kindFilter;
    private int pageSize = 500;

    private string? rangeAnchor;
    private bool isTreeOpen = true;
    private bool isInfoOpen;
    private FileDetailDto? detail;
    private string? openMenu;

    private IReadOnlyList<MenuEntry>? contextMenu;
    private double menuX;
    private double menuY;

    private Clipboard? clipboard;
    private FileSystemItemDto? previewItem;
    private IReadOnlyList<string>? dragPaths;

    private FolderTree? tree;
    private TextEditorDialog? editor;
    private PromptDialog? prompt;
    private ConfirmDialog? confirm;

    [Inject] private IFileManagerService Service { get; set; } = default!;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Inject] private WebFilezApiAddress ApiAddress { get; set; } = default!;

    /// <summary>The root to open. Defaults to the first root the caller can browse.</summary>
    [Parameter] public string? Root { get; set; }

    /// <summary>The folder to open within <see cref="Root"/>.</summary>
    [Parameter] public string? Path { get; set; }

    /// <summary><c>light</c>, <c>dark</c>, <c>auto</c> (follow the OS) or <c>bootstrap</c> (follow the page's Bootstrap 5.3 colours).</summary>
    [Parameter] public string Theme { get; set; } = Constants.Themes.Light;

    /// <summary>Initial layout: <c>tiles</c>, <c>list</c>, <c>compact</c> or <c>gallery</c>. The user's last choice wins once made.</summary>
    [Parameter] public string? View { get; set; }

    /// <summary>Any CSS height. The component fills it and scrolls inside.</summary>
    [Parameter] public string Height { get; set; } = "600px";

    /// <summary>
    /// <c>none</c> for a plain file manager; <c>single</c> or <c>multiple</c> to act as a picker,
    /// with a Select button that raises <see cref="OnSelected"/> and the <c>wf-selected</c> DOM event.
    /// </summary>
    [Parameter] public string SelectionMode { get; set; } = Constants.SelectionModes.None;

    /// <summary>In picker mode, which files may be picked, as an HTML <c>accept</c> list: <c>image/*,.pdf</c>.</summary>
    [Parameter] public string? Accept { get; set; }

    [Parameter] public string SelectLabel { get; set; } = "Select";

    [Parameter] public bool ShowTree { get; set; } = true;

    /// <summary>Hides every action that changes files. The server's permissions still apply either way.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public string? Class { get; set; }

    /// <summary>Raised by the Select button in picker mode.</summary>
    [Parameter] public EventCallback<IReadOnlyList<SelectedFile>> OnSelected { get; set; }

    [Parameter] public EventCallback<FileLocation> OnNavigated { get; set; }

    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private string CurrentPath => listing?.Path ?? VirtualPath.Normalize(Path);

    private bool IsPicker => SelectionMode is Constants.SelectionModes.Single or Constants.SelectionModes.Multiple;

    private IReadOnlyList<FileSystemItemDto> SelectedItems =>
        listing?.Items.Where(i => selection.Contains(i.Path)).ToList() ?? [];

    private FileSystemItemDto? DetailItem =>
        selection.Count == 1 ? listing?.Items.FirstOrDefault(i => selection.Contains(i.Path)) : null;

    private IReadOnlySet<string> CutPaths =>
        clipboard is { IsCut: true } c && c.RootId == rootId ? c.Paths : EmptySet;

    private static readonly HashSet<string> EmptySet = [];

    private IReadOnlyList<FileSystemItemDto> PreviewableItems =>
        listing?.Items.Where(i => FileKinds.IsMedia(i.Kind)).ToList() ?? [];

    private bool CanShowHidden => roots.Any(r => r.Id == rootId && r.CanShowHiddenFiles);

    private bool CanConfirmSelection =>
        selection.Count > 0
        && (SelectionMode == Constants.SelectionModes.Multiple || selection.Count == 1)
        && SelectedItems.All(IsSelectable);

    private string PickerHint => string.IsNullOrWhiteSpace(Accept)
        ? "Choose a file"
        : $"Choose {(SelectionMode == Constants.SelectionModes.Multiple ? "files" : "a file")} ({Accept})";

    protected override void OnInitialized()
    {
        interop = new WebFilezInterop(JS);
        urls = new WebFilezUrls(Navigation, ApiAddress);
        viewMode = ParseView(View);
    }

    protected override async Task OnParametersSetAsync()
    {
        // Only a change the host made moves the component. Re-rendering with the same Root and
        // Path must not throw the user back to where they started.
        if (appliedParameters == (Root, Path))
        {
            return;
        }

        appliedParameters = (Root, Path);

        if (roots.Count == 0)
        {
            var result = await Service.GetRootsAsync();
            if (!result.IsSuccess)
            {
                loadError = result.GetDisplayMessage();
                return;
            }

            roots = result.Value;
        }

        string? requested = roots.FirstOrDefault(r => string.Equals(r.Id, Root, StringComparison.OrdinalIgnoreCase))?.Id;
        rootId = requested ?? roots.FirstOrDefault()?.Id;

        if (rootId is null)
        {
            loadError = "There are no folders available.";
            return;
        }

        await OpenAsync(rootId, VirtualPath.Normalize(Path), notify: false);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        callbackReference = DotNetObjectReference.Create(this);
        await interop.AttachAsync(rootElement, listingElement, callbackReference);
        isAttached = true;

        if (await interop.GetStoredAsync("view") is { } stored && Enum.TryParse(stored, out ListingViewMode storedMode) && storedMode != viewMode)
        {
            viewMode = storedMode;
            StateHasChanged();
        }
    }

    // ------------------------------------------------------------------ navigation

    private async Task NavigateAsync(string targetRoot, string path)
    {
        await OpenAsync(targetRoot, path, notify: true);
    }

    private async Task OpenAsync(string targetRoot, string path, bool notify)
    {
        rootId = targetRoot;
        selection.Clear();
        rangeAnchor = null;
        detail = null;
        previewItem = null;
        contextMenu = null;
        pageSize = 500;

        await LoadAsync(path);

        if (notify && listing is not null)
        {
            await OnNavigated.InvokeAsync(new FileLocation(listing.RootId, listing.Path));
            await DispatchAsync(Constants.DomEvents.Navigated, JsonSerializer.Serialize(
                new FileLocation(listing.RootId, listing.Path), FileManagerEventJsonContext.Default.FileLocation));
        }

        if (isInfoOpen)
        {
            await LoadDetailAsync();
        }
    }

    private Task LoadAsync() => LoadAsync(CurrentPath);

    private async Task LoadAsync(string path)
    {
        if (rootId is null)
        {
            return;
        }

        var result = await Service.ListAsync(new ListingRequest(
            rootId,
            path,
            string.IsNullOrWhiteSpace(filter) ? null : filter,
            kindFilter,
            sortBy,
            descending,
            showHidden,
            1,
            pageSize));

        if (result.IsSuccess)
        {
            listing = result.Value;
            loadError = null;

            // Drop selections of items that are gone, such as after a delete or move.
            selection.IntersectWith(listing.Items.Select(i => i.Path));
            return;
        }

        if (listing is null || result.Status != ResultStatus.NotFound || path == Constants.RootPath)
        {
            listing = null;
            loadError = result.GetDisplayMessage();
            return;
        }

        // The folder vanished underneath us; fall back to its nearest parent.
        toasts.Error(result.GetDisplayMessage());
        await LoadAsync(VirtualPath.GetParent(path) ?? Constants.RootPath);
    }

    private async Task GoUpAsync()
    {
        if (rootId is not null && VirtualPath.GetParent(CurrentPath) is { } parent)
        {
            await NavigateAsync(rootId, parent);
        }
    }

    private async Task LoadMoreAsync()
    {
        pageSize += 500;
        await LoadAsync();
    }

    private async Task OnFilterChangedAsync() => await LoadAsync();

    private async Task SetKindFilterAsync(KindFilter value)
    {
        kindFilter = value;
        await LoadAsync();
    }

    private async Task SortByAsync(ListingSortField field)
    {
        // Choosing the active field again flips direction, which is what a column header implies.
        descending = sortBy == field && !descending;
        sortBy = field;
        await LoadAsync();
    }

    private async Task ToggleHiddenAsync()
    {
        showHidden = !showHidden;
        await LoadAsync();
    }

    private async Task SetViewModeAsync(ListingViewMode mode)
    {
        viewMode = mode;
        await interop.SetStoredAsync("view", mode.ToString());
    }

    private async Task ToggleInfoAsync()
    {
        isInfoOpen = !isInfoOpen;
        await LoadDetailAsync();
    }

    private void ToggleMenu(string name) => openMenu = openMenu == name ? null : name;

    private async Task RunMenuAsync(Func<Task> action)
    {
        openMenu = null;
        await action();
    }

    // ------------------------------------------------------------------- selection

    private bool IsSelectable(FileSystemItemDto item) =>
        !IsPicker || (!item.IsDirectory && FileKinds.IsAccepted(item.Name, Accept));

    private async Task OnItemClickAsync((FileSystemItemDto Item, MouseEventArgs Args) e)
    {
        var (item, args) = e;
        contextMenu = null;

        if (args.ShiftKey && rangeAnchor is not null && SelectionMode != Constants.SelectionModes.Single)
        {
            SelectRange(rangeAnchor, item.Path);
        }
        else if ((args.CtrlKey || args.MetaKey) && SelectionMode != Constants.SelectionModes.Single)
        {
            if (!selection.Remove(item.Path))
            {
                selection.Add(item.Path);
            }

            rangeAnchor = item.Path;
        }
        else
        {
            selection.Clear();
            selection.Add(item.Path);
            rangeAnchor = item.Path;
        }

        await LoadDetailAsync();
    }

    private void SelectRange(string from, string to)
    {
        if (listing is null)
        {
            return;
        }

        var paths = listing.Items.Select(i => i.Path).ToList();
        int start = paths.IndexOf(from);
        int end = paths.IndexOf(to);

        if (start < 0 || end < 0)
        {
            return;
        }

        selection.Clear();
        selection.UnionWith(paths.GetRange(Math.Min(start, end), Math.Abs(end - start) + 1));
    }

    private async Task ToggleSelectAsync(FileSystemItemDto item)
    {
        if (!selection.Remove(item.Path))
        {
            selection.Add(item.Path);
        }

        rangeAnchor = item.Path;
        await LoadDetailAsync();
    }

    private async Task SelectAllAsync(bool select)
    {
        selection.Clear();

        if (select && listing is not null && SelectionMode != Constants.SelectionModes.Single)
        {
            selection.UnionWith(listing.Items.Where(i => !IsPicker || IsSelectable(i)).Select(i => i.Path));
        }

        await LoadDetailAsync();
    }

    private void ClearSelection()
    {
        // Only clicks that reach the background get here; item clicks stop propagation.
        selection.Clear();
        rangeAnchor = null;
        detail = null;
        contextMenu = null;
    }

    private async Task LoadDetailAsync()
    {
        if (!isInfoOpen || rootId is null || listing is null || selection.Count > 1)
        {
            detail = null;
            return;
        }

        string path = selection.Count == 1 ? selection.First() : listing.Path;
        var result = await Service.GetDetailAsync(rootId, path);
        detail = result.IsSuccess ? result.Value : null;
    }

    // -------------------------------------------------------------------- permissions

    private FilePermission Clamp(FilePermission permissions) =>
        ReadOnly ? permissions & FilePermissionExtensions.NonMutating : permissions;

    /// <summary>Whether the caller holds every flag in <paramref name="permission"/> on the current folder.</summary>
    private bool Can(FilePermission permission) =>
        listing is not null && Clamp(listing.Permissions).Has(permission);

    private bool CanOnSelection(FilePermission permission) =>
        SelectedItems is { Count: > 0 } items && items.All(i => Clamp(i.Permissions).Has(permission));

    private bool CanOn(FileSystemItemDto item, FilePermission permission) => Clamp(item.Permissions).Has(permission);

    private bool CanDrag(FileSystemItemDto item) => CanOn(item, FilePermission.Read);

    // ----------------------------------------------------------------------- opening

    private async Task ActivateAsync(FileSystemItemDto item)
    {
        if (item.IsDirectory)
        {
            await NavigateAsync(rootId!, item.Path);
            return;
        }

        if (IsPicker)
        {
            if (IsSelectable(item))
            {
                selection.Clear();
                selection.Add(item.Path);
                await ConfirmSelectionAsync();
            }

            return;
        }

        await OpenFileAsync(item);
    }

    private async Task OpenFileAsync(FileSystemItemDto item)
    {
        if (FileKinds.IsEditable(item.Kind) && CanOn(item, FilePermission.Read))
        {
            await EditAsync(item);
        }
        else if (FileKinds.CanPreview(item.Kind) && CanOn(item, FilePermission.Read))
        {
            previewItem = item;
        }
        else if (CanOn(item, FilePermission.Download))
        {
            await DownloadAsync([item]);
        }
    }

    private async Task EditAsync(FileSystemItemDto item)
    {
        if (editor is not null && rootId is not null)
        {
            await editor.OpenAsync(rootId, item.Path);
        }
    }

    private async Task ClosePreviewAsync()
    {
        previewItem = null;
        await interop.FocusAsync(rootElement);
    }

    private Task<bool> ConfirmDiscardAsync() =>
        confirm?.ShowAsync("Discard changes?", "Your changes to this file haven't been saved.", "Discard", isDestructive: true)
        ?? Task.FromResult(true);

    // ------------------------------------------------------------------- picker

    private async Task ConfirmSelectionAsync()
    {
        if (!CanConfirmSelection || rootId is null)
        {
            return;
        }

        var files = SelectedItems
            .Select(i => new SelectedFile(
                rootId,
                i.Path,
                i.Name,
                i.SizeBytes,
                FileKinds.GetMimeType(i.Name),
                urls.Download(rootId, i.Path),
                urls.Public(i.PublicUrl)))
            .ToList();

        await OnSelected.InvokeAsync(files);
        await DispatchAsync(Constants.DomEvents.Selected, JsonSerializer.Serialize(
            new FileSelection(files), FileManagerEventJsonContext.Default.FileSelection));
    }

    private async Task DispatchAsync(string eventName, string detailJson)
    {
        if (isAttached)
        {
            await interop.DispatchAsync(rootElement, eventName, detailJson);
        }
    }

    private static ListingViewMode ParseView(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out ListingViewMode mode) ? mode : ListingViewMode.Tiles;

    private sealed record Clipboard(string RootId, HashSet<string> Paths, bool IsCut);

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Prerendering disposes the component without ever attaching; only detach what an
            // interactive render installed.
            if (isAttached)
            {
                await interop.DetachAsync(rootElement);
            }

            await interop.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException or ObjectDisposedException or TaskCanceledException)
        {
            // The circuit or page is already gone.
        }

        callbackReference?.Dispose();
        toasts.Dispose();
    }
}
