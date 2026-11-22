namespace WebFilez.Components.Infrastructure;

/// <summary>
/// The component's JavaScript, loaded as a module on first use so hosts only need the stylesheet
/// in their page. Every call is scoped to one file manager's root element, so several can share
/// a page. Public only because the part components take it as a parameter.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class WebFilezInterop(IJSRuntime js) : IAsyncDisposable
{
    private const string ModulePath = "/_content/WebFilez.Components/webfilez.js";

    private Task<IJSObjectReference>? module;

    private Task<IJSObjectReference> Module => module ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

    /// <summary>Keyboard shortcuts on the root, and file drops on the drop zone.</summary>
    public async ValueTask AttachAsync<T>(ElementReference root, ElementReference dropZone, DotNetObjectReference<T> callback)
        where T : class =>
        await (await Module).InvokeVoidAsync("attach", root, dropZone, callback);

    /// <summary>Removes the keyboard and drop listeners from one file manager.</summary>
    /// <param name="root">The file manager's root element.</param>
    /// <returns>A task that completes when the listeners are gone.</returns>
    public async ValueTask DetachAsync(ElementReference root) =>
        await (await Module).InvokeVoidAsync("detach", root);

    /// <summary>Opens the browser's file picker.</summary>
    /// <param name="root">The file manager's root element.</param>
    /// <param name="directories">Whether the picker selects folders.</param>
    /// <param name="accept">An HTML accept list, or <see langword="null"/> to accept every file.</param>
    /// <returns>A task that completes when the picker has been opened.</returns>
    public async ValueTask PickFilesAsync(ElementReference root, bool directories, string? accept) =>
        await (await Module).InvokeVoidAsync("pickFiles", root, directories, accept);

    /// <summary>Sends the files the drop planned, including a relative folder for each one.</summary>
    /// <param name="root">The file manager's root element.</param>
    /// <param name="url">The upload endpoint.</param>
    /// <param name="rootId">The root to store them in.</param>
    /// <param name="path">The open folder.</param>
    /// <param name="instructions">What to do with each name, or <see langword="null"/> when nothing is pending.</param>
    /// <param name="includeCredentials">Whether the request sends cookies.</param>
    /// <returns>A task that completes once the browser has started the uploads.</returns>
    public async ValueTask UploadPendingAsync(
        ElementReference root,
        string url,
        string rootId,
        string path,
        IReadOnlyList<UploadInstruction>? instructions,
        bool includeCredentials) =>
        await (await Module).InvokeVoidAsync("uploadPending", root, url, rootId, path, instructions, includeCredentials);

    /// <summary>Aborts one upload that is still sending.</summary>
    /// <param name="root">The file manager's root element.</param>
    /// <param name="uploadId">The upload id.</param>
    /// <returns>A task that completes once the browser has been told to abort.</returns>
    public async ValueTask CancelUploadAsync(ElementReference root, long uploadId) =>
        await (await Module).InvokeVoidAsync("cancelUpload", root, uploadId);

    /// <summary>Starts a browser download of a URL.</summary>
    /// <param name="url">The download or zip address.</param>
    /// <returns>A task that completes once the navigation has started.</returns>
    public async ValueTask DownloadAsync(string url) =>
        await (await Module).InvokeVoidAsync("download", url);

    /// <summary>Copies text to the clipboard.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when the browser accepted it.</returns>
    public async ValueTask<bool> CopyTextAsync(string text) =>
        await (await Module).InvokeAsync<bool>("copyText", text);

    /// <summary>Moves focus to an element.</summary>
    /// <param name="element">The element.</param>
    /// <param name="selectStem">Whether a file name's stem is selected, leaving the extension alone.</param>
    /// <returns>A task that completes once focus has moved.</returns>
    public async ValueTask FocusAsync(ElementReference element, bool selectStem = false) =>
        await (await Module).InvokeVoidAsync("focus", element, selectStem);

    /// <summary>Moves focus to the listing row with this key and scrolls it into view.</summary>
    public async ValueTask FocusItemAsync(ElementReference root, string key) =>
        await (await Module).InvokeVoidAsync("focusItem", root, key);

    /// <summary>Sizes an element so it fits the viewport.</summary>
    /// <param name="element">The element.</param>
    /// <returns>A task that completes once the size has been applied.</returns>
    public async ValueTask FitToViewportAsync(ElementReference element) =>
        await (await Module).InvokeVoidAsync("fitToViewport", element);

    /// <summary>Reads a value this file manager stored in the browser.</summary>
    /// <param name="key">The storage key.</param>
    /// <returns>The stored text, or <see langword="null"/> when there is none.</returns>
    public async ValueTask<string?> GetStoredAsync(string key) =>
        await (await Module).InvokeAsync<string?>("getStored", key);

    /// <summary>Stores a value in the browser for this file manager.</summary>
    /// <param name="key">The storage key.</param>
    /// <param name="value">The text to store.</param>
    /// <returns>A task that completes once the value is stored.</returns>
    public async ValueTask SetStoredAsync(string key, string value) =>
        await (await Module).InvokeVoidAsync("setStored", key, value);

    /// <summary>The editors the page has registered, as JSON, so the list survives trimming.</summary>
    public async ValueTask<string> ListEditorsAsync() =>
        await (await Module).InvokeAsync<string>("listEditors");

    /// <summary>Mounts a registered editor for one file. The file is JSON for the same reason.</summary>
    public async ValueTask OpenEditorAsync<T>(string id, string fileJson, string uploadUrl, bool includeCredentials, DotNetObjectReference<T> callback)
        where T : class =>
        await (await Module).InvokeVoidAsync("openEditor", id, fileJson, uploadUrl, includeCredentials, callback);

    /// <summary>Closes the editor when this file manager opened it.</summary>
    public async ValueTask CloseEditorAsync<T>(DotNetObjectReference<T> callback)
        where T : class =>
        await (await Module).InvokeVoidAsync("closeEditor", callback);

    /// <summary>
    /// Raises a bubbling DOM event on the root, for hosts that aren't Blazor. The detail is
    /// passed as JSON text so the payload survives trimming in WebAssembly.
    /// </summary>
    public async ValueTask DispatchAsync(ElementReference root, string eventName, string detailJson) =>
        await (await Module).InvokeVoidAsync("dispatch", root, eventName, detailJson);

    /// <summary>
    /// Releases the resources used by this instance.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (module is { IsCompletedSuccessfully: true })
        {
            try
            {
                await module.Result.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is already gone, and the module with it.
            }
        }
    }
}
