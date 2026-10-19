using System.Text.Json;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using WebFilez.Components.Infrastructure;
using WebFilez.Components.Localization;
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
    private enum SearchScope
    {
        Folder,
        Root,
        All,
    }

    private (ListingViewMode Mode, string Icon, string Label)[] ViewModes =>
    [
        (ListingViewMode.Tiles, "grid", Text["view.tiles"]),
        (ListingViewMode.List, "list", Text["view.list"]),
        (ListingViewMode.Compact, "compact", Text["view.compact"]),
    ];

    private (KindFilter Filter, string Icon, string Label)[] KindFilters =>
    [
        (KindFilter.All, "folder", Text["kind.all"]),
        (KindFilter.Images, "image", Text["kind.images"]),
        (KindFilter.Documents, "file-text", Text["kind.documents"]),
        (KindFilter.Media, "music", Text["kind.media"]),
        (KindFilter.Archives, "archive", Text["kind.archives"]),
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
    private string? appliedKind;
    private bool kindLocked;
    private string? loadError;
    private (string? Root, string? Path)? appliedParameters;

    private ListingViewMode viewMode;
    private ListingSortField sortBy = ListingSortField.Name;
    private bool descending;
    private bool showHidden;
    private string filter = string.Empty;
    private KindFilter kindFilter;
    private int pageSize = 500;
    private SearchScope searchScope;
    private SearchResultDto? searchResults;
    private int searchVersion;
    private string? searchError;
    private string? activeKey;
    private string? dragRootId;
    private bool dragCanMove;

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
    private string? previewRoot;
    private IReadOnlyList<string>? dragPaths;

    private FolderTree? tree;
    private TextEditorDialog? editor;
    private PromptDialog? prompt;
    private ConfirmDialog? confirm;

    [Inject] private IFileManagerService Service { get; set; } = default!;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Inject] private WebFilezApiAddress ApiAddress { get; set; } = default!;

    [Inject] private IServiceProvider Services { get; set; } = default!;

    /// <summary>The root to open. Defaults to the first root the caller can browse.</summary>
    [Parameter] public string? Root { get; set; }

    /// <summary>The folder to open within <see cref="Root"/>.</summary>
    [Parameter] public string? Path { get; set; }

    /// <summary><c>light</c>, <c>dark</c>, <c>auto</c> (follow the OS) or <c>bootstrap</c> (follow the page's Bootstrap 5.3 colours).</summary>
    [Parameter] public string Theme { get; set; } = Constants.Themes.Light;

    /// <summary>Initial layout: <c>tiles</c>, <c>list</c> or <c>compact</c>. The user's last choice wins once made.</summary>
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

    /// <summary>
    /// Fixes the listing to one kind: <c>images</c>, <c>documents</c>, <c>media</c>, <c>archives</c>,
    /// <c>folders</c> (or <c>folder</c>), or <c>all</c>. Folders stay visible for every kind except
    /// <c>folders</c>, which shows directories only. While this is set the type buttons are hidden.
    /// </summary>
    [Parameter] public string? Kind { get; set; }

    [Parameter] public string SelectLabel { get; set; } = "Select";

    [Parameter] public bool ShowTree { get; set; } = true;

    /// <summary>Hides every action that changes files. The server's permissions still apply either way.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public string? Class { get; set; }

    /// <summary>IETF language tag, such as <c>fr-CA</c>. Empty follows the saved choice, then the UI culture.</summary>
    [Parameter] public string? Language { get; set; }

    /// <summary>
    /// Which registered editors this file manager offers, and for which files.
    /// <c>images:.jpg,.png|annotate:.webp</c> uses those editors for those types.
    /// An id with no accept list keeps the types it registered. Empty offers every registered editor.
    /// </summary>
    [Parameter] public string? Editors { get; set; }

    /// <summary>Raised by the Select button in picker mode.</summary>
    [Parameter] public EventCallback<IReadOnlyList<SelectedFile>> OnSelected { get; set; }

    [Parameter] public EventCallback<FileLocation> OnNavigated { get; set; }

    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private string CurrentPath => listing?.Path ?? VirtualPath.Normalize(Path);

    private string? CurrentPublicUrl => urls.Public(listing?.PublicUrl);

    private bool IsPicker => SelectionMode is Constants.SelectionModes.Single or Constants.SelectionModes.Multiple;

    private IWebFilezLocalizer Locales => Services.GetService<IWebFilezLocalizer>() ?? WebFilezLocaleCatalog.Shared;

    private WebFilezText text = WebFilezText.English;

    private WebFilezText Text => text;

    private string? appliedLanguageParameter;

    private bool ShowingSearch => searchScope != SearchScope.Folder && !string.IsNullOrWhiteSpace(filter);

    private IReadOnlyList<ListingEntry> SelectedRows() => Rows().Where(row => selection.Contains(row.Key)).ToList();

    private IReadOnlyList<FileSystemItemDto> SelectedItems
    {
        get
        {
            var rows = SelectedRows();
            return rows.Select(row => row.RootId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1
                ? []
                : rows.Select(row => row.Item).ToList();
        }
    }

    private string? ActionRootId
    {
        get
        {
            var roots = SelectedRows().Select(row => row.RootId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return roots.Count switch
            {
                0 => rootId,
                1 => roots[0],
                _ => null,
            };
        }
    }

    private string DetailRootId => SelectedRows() is [var only] ? only.RootId : rootId ?? string.Empty;

    private long SelectedBytes => SelectedRows().Where(row => !row.Item.IsDirectory).Sum(row => row.Item.SizeBytes);

    private FileSystemItemDto? DetailItem => SelectedRows() is [var only] ? only.Item : null;

    private IReadOnlySet<string> CutPaths
    {
        get
        {
            if (clipboard is not { IsCut: true } clip)
            {
                return EmptySet;
            }

            return Rows()
                .Where(row => row.RootId == clip.RootId && clip.Paths.Contains(row.Item.Path))
                .Select(row => row.Key)
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    private static readonly HashSet<string> EmptySet = [];

    private IReadOnlyList<FileSystemItemDto> PreviewableItems =>
        Rows().Where(row => row.RootId == (previewRoot ?? rootId) && FileKinds.IsMedia(row.Item.Kind)).Select(row => row.Item).ToList();

    private bool CanShowHidden => roots.Any(r => r.Id == rootId && r.CanShowHiddenFiles);

    private bool CanConfirmSelection =>
        selection.Count > 0
        && (SelectionMode == Constants.SelectionModes.Multiple || selection.Count == 1)
        && SelectedItems.All(IsSelectable);

    private string PickerHint => string.IsNullOrWhiteSpace(Accept)
        ? Text[SelectionMode == Constants.SelectionModes.Multiple ? "status.chooseFiles" : "status.chooseFile"]
        : Text.Format(
            "status.chooseTyped",
            Text[SelectionMode == Constants.SelectionModes.Multiple ? "status.files" : "status.aFile"],
            Accept);

    protected override void OnInitialized()
    {
        interop = new WebFilezInterop(JS);
        urls = new WebFilezUrls(Navigation, ApiAddress);
        viewMode = ParseView(View);
        text = Locales.Resolve(string.IsNullOrWhiteSpace(Language) ? CultureInfo.CurrentUICulture.Name : Language);
        appliedLanguageParameter = string.IsNullOrWhiteSpace(Language) ? null : Language;
    }

    protected override async Task OnParametersSetAsync()
    {
        ApplyHostLanguage();
        bool kindChanged = UpdateKind();

        // Only a change the host made moves the component. Re-rendering with the same Root and
        // Path must not throw the user back to where they started.
        if (appliedParameters == (Root, Path))
        {
            if (kindChanged && rootId is not null && listing is not null)
            {
                await RefreshViewAsync();
            }

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
            loadError = Text["error.noFolders"];
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

        if (string.IsNullOrWhiteSpace(Language) && await interop.GetStoredAsync("lang") is { Length: > 0 } storedLanguage)
        {
            text = Locales.Resolve(storedLanguage);
            StateHasChanged();
        }
    }

    private void ApplyHostLanguage()
    {
        if (string.IsNullOrWhiteSpace(Language) || appliedLanguageParameter == Language)
        {
            return;
        }

        appliedLanguageParameter = Language;
        text = Locales.Resolve(Language);
    }

    private async Task SetLanguageAsync(string tag)
    {
        text = Locales.Resolve(tag);
        if (string.IsNullOrWhiteSpace(Language))
        {
            await interop.SetStoredAsync("lang", text.Language);
        }
    }

    // ------------------------------------------------------------------ navigation

    private async Task NavigateAsync(string targetRoot, string path)
    {
        if (ShowingSearch)
        {
            await LeaveSearchAsync();
        }

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
            var location = new FileLocation(listing.RootId, listing.Path, urls.Public(listing.PublicUrl));
            await OnNavigated.InvokeAsync(location);
            await DispatchAsync(Constants.DomEvents.Navigated, JsonSerializer.Serialize(
                location, FileManagerEventJsonContext.Default.FileLocation));
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

            // A folder listing must not drop a selection made in search results.
            if (!ShowingSearch)
            {
                selection.IntersectWith(listing.Items.Select(i => i.Path));
            }
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

    private async Task OnFilterChangedAsync()
    {
        if (searchScope == SearchScope.Folder)
        {
            await LoadAsync();
            return;
        }

        await RunSearchAsync();
    }

    private async Task OnScopeChangedAsync(ChangeEventArgs change)
    {
        if (Enum.TryParse(change.Value?.ToString(), out SearchScope scope))
        {
            await SetSearchScopeAsync(scope);
        }
    }

    private async Task SetSearchScopeAsync(SearchScope scope)
    {
        searchScope = scope;
        searchResults = null;
        searchError = null;
        searchVersion++;
        selection.Clear();
        rangeAnchor = null;
        activeKey = null;

        if (scope == SearchScope.Folder || string.IsNullOrWhiteSpace(filter))
        {
            await LoadAsync();
            return;
        }

        await RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        int version = ++searchVersion;
        string query = filter.Trim();
        if (query.Length == 0)
        {
            searchResults = null;
            searchError = null;
            return;
        }

        var result = await Service.SearchAsync(new SearchRequest(
            query,
            searchScope == SearchScope.All ? null : rootId,
            Kind: kindFilter,
            ShowHidden: showHidden,
            SortBy: sortBy,
            Descending: descending));

        if (version != searchVersion)
        {
            return;
        }

        if (result.IsSuccess)
        {
            searchResults = result.Value;
            searchError = null;
            selection.IntersectWith(Rows().Select(row => row.Key));
            return;
        }

        searchResults = null;
        searchError = result.GetDisplayMessage();
    }

    private async Task ClearSearchAsync()
    {
        await LeaveSearchAsync();
        selection.Clear();
        rangeAnchor = null;
        activeKey = null;
        await LoadAsync();
    }

    private async Task LeaveSearchAsync()
    {
        filter = string.Empty;
        searchScope = SearchScope.Folder;
        searchResults = null;
        searchError = null;
        searchVersion++;
        await Task.CompletedTask;
    }

    private Task RefreshViewAsync() => ShowingSearch ? RunSearchAsync() : LoadAsync();

    private async Task SetKindFilterAsync(KindFilter value)
    {
        kindFilter = value;
        await RefreshViewAsync();
    }

    private async Task SortByAsync(ListingSortField field)
    {
        // Choosing the active field again flips direction, which is what a column header implies.
        descending = sortBy == field && !descending;
        sortBy = field;
        await RefreshViewAsync();
    }

    private async Task ToggleHiddenAsync()
    {
        showHidden = !showHidden;
        await RefreshViewAsync();
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

    private async Task OnItemClickAsync((ListingEntry Entry, MouseEventArgs Args) e)
    {
        var (entry, args) = e;
        contextMenu = null;
        activeKey = entry.Key;

        if (args.ShiftKey && rangeAnchor is not null && SelectionMode != Constants.SelectionModes.Single)
        {
            SelectRange(rangeAnchor, entry.Key);
        }
        else if ((args.CtrlKey || args.MetaKey) && SelectionMode != Constants.SelectionModes.Single)
        {
            if (!selection.Remove(entry.Key))
            {
                selection.Add(entry.Key);
            }

            rangeAnchor = entry.Key;
        }
        else
        {
            selection.Clear();
            selection.Add(entry.Key);
            rangeAnchor = entry.Key;
        }

        await LoadDetailAsync();
    }

    private void SelectRange(string from, string to)
    {
        var rows = Rows();
        int start = rows.FindIndex(row => row.Key == from);
        int end = rows.FindIndex(row => row.Key == to);
        if (start < 0 || end < 0)
        {
            return;
        }

        selection.Clear();
        int first = Math.Min(start, end);
        int last = Math.Max(start, end);
        for (int index = first; index <= last; index++)
        {
            var row = rows[index];
            if (IsPicker && !IsSelectable(row.Item))
            {
                continue;
            }

            selection.Add(row.Key);
        }
    }

    private async Task ToggleSelectAsync((ListingEntry Entry, MouseEventArgs Args) e)
    {
        activeKey = e.Entry.Key;
        if (e.Args.ShiftKey && rangeAnchor is not null && SelectionMode != Constants.SelectionModes.Single)
        {
            SelectRange(rangeAnchor, e.Entry.Key);
        }
        else if (!selection.Remove(e.Entry.Key) && (!IsPicker || IsSelectable(e.Entry.Item)))
        {
            selection.Add(e.Entry.Key);
            rangeAnchor = e.Entry.Key;
        }
        else
        {
            rangeAnchor = e.Entry.Key;
        }

        await LoadDetailAsync();
    }

    private async Task SelectAllAsync(bool select)
    {
        if (select && !ShowingSearch && listing is { } current && current.TotalCount > current.Items.Count
            && SelectionMode != Constants.SelectionModes.Single)
        {
            pageSize = Math.Min(current.TotalCount, 10_000);
            await LoadAsync();
        }

        selection.Clear();
        if (select && SelectionMode != Constants.SelectionModes.Single)
        {
            foreach (var row in Rows())
            {
                if (!IsPicker || IsSelectable(row.Item))
                {
                    selection.Add(row.Key);
                }
            }

            if (Rows() is { Count: > 0 } rows)
            {
                rangeAnchor ??= rows[0].Key;
                activeKey ??= rows[0].Key;
            }
        }

        await LoadDetailAsync();
    }

    private void ClearSelection()
    {
        // Only clicks that reach the background get here; item clicks stop propagation.
        selection.Clear();
        rangeAnchor = null;
        activeKey = null;
        detail = null;
        contextMenu = null;
    }

    private async Task LoadDetailAsync()
    {
        var rows = SelectedRows();
        if (!isInfoOpen || rows.Count > 1)
        {
            detail = null;
            return;
        }

        if (rows.Count == 1)
        {
            var result = await Service.GetDetailAsync(rows[0].RootId, rows[0].Item.Path);
            detail = result.IsSuccess ? result.Value : null;
            return;
        }

        if (rootId is null || listing is null)
        {
            detail = null;
            return;
        }

        string path = listing.Path;
        var folder = await Service.GetDetailAsync(rootId, path);
        detail = folder.IsSuccess ? folder.Value : null;
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

    private async Task ActivateAsync(ListingEntry entry)
    {
        var item = entry.Item;
        if (item.IsDirectory)
        {
            await NavigateAsync(entry.RootId, item.Path);
            return;
        }

        if (IsPicker)
        {
            if (IsSelectable(item))
            {
                selection.Clear();
                selection.Add(entry.Key);
                activeKey = entry.Key;
                await ConfirmSelectionAsync();
            }

            return;
        }

        await OpenFileAsync(entry.RootId, item);
    }

    private async Task OpenFileAsync(string itemRoot, FileSystemItemDto item)
    {
        if (CanOn(item, FilePermission.Modify))
        {
            var editors = await EditorsForAsync(item);
            if (editors.Count > 0)
            {
                await OpenExternalEditorAsync(itemRoot, item, editors[0]);
                return;
            }
        }

        if (FileKinds.IsEditable(item.Kind) && CanOn(item, FilePermission.Read))
        {
            await EditAsync(item, itemRoot);
        }
        else if (FileKinds.CanPreview(item.Kind) && CanOn(item, FilePermission.Read))
        {
            previewItem = item;
            previewRoot = itemRoot;
        }
        else if (CanOn(item, FilePermission.Download))
        {
            await DownloadAsync(itemRoot, [item]);
        }
    }

    private async Task EditAsync(FileSystemItemDto item, string? itemRoot = null)
    {
        string target = itemRoot ?? ActionRootId ?? rootId ?? string.Empty;
        if (editor is not null && target.Length > 0)
        {
            await editor.OpenAsync(target, item.Path);
        }
    }

    private async Task ClosePreviewAsync()
    {
        previewItem = null;
        await interop.FocusAsync(rootElement);
    }

    private Task<bool> ConfirmDiscardAsync() =>
        confirm?.ShowAsync(Text["confirm.discard.title"], Text["confirm.discard.message"], Text["dialog.discard"], isDestructive: true)
        ?? Task.FromResult(true);

    // ------------------------------------------------------------------- picker

    private async Task ConfirmSelectionAsync()
    {
        if (!CanConfirmSelection || rootId is null)
        {
            return;
        }

        var files = SelectedRows()
            .Select(row => new SelectedFile(
                row.RootId,
                row.Item.Path,
                row.Item.Name,
                row.Item.SizeBytes,
                FileKinds.GetMimeType(row.Item.Name),
                urls.Download(row.RootId, row.Item.Path),
                urls.Public(row.Item.PublicUrl)))
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

    /// <summary>
    /// Applies <see cref="Kind"/> when the host changes it. Returns whether a listing already on
    /// screen has to be loaded again. The first open loads through <see cref="OpenAsync"/>.
    /// </summary>
    private bool UpdateKind()
    {
        if (string.Equals(appliedKind, Kind, StringComparison.Ordinal))
        {
            return false;
        }

        appliedKind = Kind;
        if (string.IsNullOrWhiteSpace(Kind) || !TryParseKind(Kind, out var parsed))
        {
            kindLocked = false;
            return false;
        }

        kindLocked = true;
        bool changed = kindFilter != parsed;
        kindFilter = parsed;
        return changed && appliedParameters is not null;
    }

    private static bool TryParseKind(string value, out KindFilter filter)
    {
        if (value.Equals("folder", StringComparison.OrdinalIgnoreCase))
        {
            filter = KindFilter.Folders;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out filter);
    }

    private static ListingViewMode ParseView(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out ListingViewMode mode) ? mode : ListingViewMode.Tiles;

    private List<ListingEntry> Rows()
    {
        if (ShowingSearch && searchResults is not null)
        {
            return searchResults.Hits.Select(hit => new ListingEntry(KeyFor(hit.RootId, hit.Path), hit.RootId, ToItem(hit), hit.Location)).ToList();
        }

        if (listing is null || rootId is null)
        {
            return [];
        }

        return listing.Items.Select(item => new ListingEntry(item.Path, rootId, item)).ToList();
    }

    private string SelectionKey(string itemRoot, string path) => ShowingSearch ? KeyFor(itemRoot, path) : path;

    private static string KeyFor(string itemRoot, string path) => itemRoot + "|" + path;

    private static FileSystemItemDto ToItem(SearchHitDto hit) => new(
        hit.Name,
        hit.Path,
        hit.IsDirectory,
        hit.SizeBytes,
        hit.ModifiedAt,
        hit.Kind,
        hit.Extension,
        hit.IsHidden,
        hit.Permissions,
        hit.HasThumbnail,
        hit.PublicUrl);

    private async Task MoveSelectionAsync(string combo)
    {
        var rows = Rows();
        if (rows.Count == 0)
        {
            return;
        }

        bool extend = combo.StartsWith("shift+", StringComparison.Ordinal) && SelectionMode != Constants.SelectionModes.Single;
        bool moveOnly = combo.StartsWith("ctrl+", StringComparison.Ordinal);
        string key = combo[(combo.LastIndexOf('+') + 1)..];
        int current = activeKey is null ? -1 : rows.FindIndex(row => row.Key == activeKey);
        int next = key switch
        {
            "Home" => 0,
            "End" => rows.Count - 1,
            "ArrowUp" or "ArrowLeft" => current < 0 ? 0 : Math.Max(0, current - 1),
            "ArrowDown" or "ArrowRight" => current < 0 ? 0 : Math.Min(rows.Count - 1, current + 1),
            _ => current,
        };

        if (next < 0)
        {
            return;
        }

        var target = rows[next];
        activeKey = target.Key;
        if (moveOnly)
        {
            await FocusActiveAsync();
            return;
        }

        if (extend && rangeAnchor is not null)
        {
            SelectRange(rangeAnchor, target.Key);
        }
        else
        {
            selection.Clear();
            if (!IsPicker || IsSelectable(target.Item) || target.Item.IsDirectory)
            {
                selection.Add(target.Key);
            }

            rangeAnchor = target.Key;
        }

        await LoadDetailAsync();
        await FocusActiveAsync();
    }

    private async Task ToggleActiveAsync(bool extend)
    {
        var rows = Rows();
        var active = rows.FirstOrDefault(row => row.Key == activeKey);
        if (active is null || SelectionMode == Constants.SelectionModes.Single)
        {
            return;
        }

        if (extend && rangeAnchor is not null)
        {
            SelectRange(rangeAnchor, active.Key);
        }
        else if (!selection.Remove(active.Key) && (!IsPicker || IsSelectable(active.Item)))
        {
            selection.Add(active.Key);
            rangeAnchor = active.Key;
        }

        await LoadDetailAsync();
    }

    private async Task FocusActiveAsync()
    {
        if (isAttached && activeKey is not null)
        {
            await interop.FocusItemAsync(listingElement, activeKey);
        }
    }

    private sealed record Clipboard(string RootId, HashSet<string> Paths, bool IsCut);

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Prerendering disposes the component without ever attaching; only detach what an
            // interactive render installed.
            if (isAttached)
            {
                if (callbackReference is not null)
                {
                    await interop.CloseEditorAsync(callbackReference);
                }

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
