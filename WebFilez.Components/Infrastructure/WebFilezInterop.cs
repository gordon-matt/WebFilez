namespace WebFilez.Components.Infrastructure;

/// <summary>
/// The component's JavaScript, loaded as a module on first use so hosts only need the stylesheet
/// in their page. Every call is scoped to one file manager's root element, so several can share
/// a page. Public only because the part components take it as a parameter.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class WebFilezInterop(IJSRuntime js) : IAsyncDisposable
{
    private const string ModulePath = "./_content/WebFilez.Components/webfilez.js";

    private Task<IJSObjectReference>? module;

    private Task<IJSObjectReference> Module => module ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

    /// <summary>Keyboard shortcuts on the root, and file drops on the drop zone.</summary>
    public async ValueTask AttachAsync<T>(ElementReference root, ElementReference dropZone, DotNetObjectReference<T> callback)
        where T : class =>
        await (await Module).InvokeVoidAsync("attach", root, dropZone, callback);

    /// <summary>
    /// Detaches the value.
    /// </summary>
    /// <param name="root">The root.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask DetachAsync(ElementReference root) =>
        await (await Module).InvokeVoidAsync("detach", root);

    /// <summary>
    /// Performs the pick files operation.
    /// </summary>
    /// <param name="root">The root.</param>
    /// <param name="directories">The directories.</param>
    /// <param name="accept">The accept.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask PickFilesAsync(ElementReference root, bool directories, string? accept) =>
        await (await Module).InvokeVoidAsync("pickFiles", root, directories, accept);

    /// <summary>
    /// Uploads the pending.
    /// </summary>
    /// <param name="root">The root.</param>
    /// <param name="url">The URL.</param>
    /// <param name="rootId">The root identifier.</param>
    /// <param name="path">The path.</param>
    /// <param name="overwrite">The overwrite.</param>
    /// <param name="includeCredentials">The include credentials.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask UploadPendingAsync(ElementReference root, string url, string rootId, string path, bool overwrite, bool includeCredentials) =>
        await (await Module).InvokeVoidAsync("uploadPending", root, url, rootId, path, overwrite, includeCredentials);

    /// <summary>
    /// Cancels the upload.
    /// </summary>
    /// <param name="root">The root.</param>
    /// <param name="uploadId">The upload identifier.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask CancelUploadAsync(ElementReference root, long uploadId) =>
        await (await Module).InvokeVoidAsync("cancelUpload", root, uploadId);

    /// <summary>
    /// Downloads the value.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask DownloadAsync(string url) =>
        await (await Module).InvokeVoidAsync("download", url);

    /// <summary>
    /// Copies the text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is true when the condition is met; otherwise, false.</returns>
    public async ValueTask<bool> CopyTextAsync(string text) =>
        await (await Module).InvokeAsync<bool>("copyText", text);

    /// <summary>
    /// Performs the focus operation.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="selectStem">The select stem.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask FocusAsync(ElementReference element, bool selectStem = false) =>
        await (await Module).InvokeVoidAsync("focus", element, selectStem);

    /// <summary>Moves focus to the listing row with this key and scrolls it into view.</summary>
    public async ValueTask FocusItemAsync(ElementReference root, string key) =>
        await (await Module).InvokeVoidAsync("focusItem", root, key);

    /// <summary>
    /// Performs the fit to viewport operation.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask FitToViewportAsync(ElementReference element) =>
        await (await Module).InvokeVoidAsync("fitToViewport", element);

    /// <summary>
    /// Gets the stored.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the string.</returns>
    public async ValueTask<string?> GetStoredAsync(string key) =>
        await (await Module).InvokeAsync<string?>("getStored", key);

    /// <summary>
    /// Sets the stored.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
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