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

    public async ValueTask DetachAsync(ElementReference root) =>
        await (await Module).InvokeVoidAsync("detach", root);

    public async ValueTask PickFilesAsync(ElementReference root, bool directories, string? accept) =>
        await (await Module).InvokeVoidAsync("pickFiles", root, directories, accept);

    public async ValueTask UploadPendingAsync(ElementReference root, string url, string rootId, string path, bool overwrite, bool includeCredentials) =>
        await (await Module).InvokeVoidAsync("uploadPending", root, url, rootId, path, overwrite, includeCredentials);

    public async ValueTask CancelUploadAsync(ElementReference root, long uploadId) =>
        await (await Module).InvokeVoidAsync("cancelUpload", root, uploadId);

    public async ValueTask DownloadAsync(string url) =>
        await (await Module).InvokeVoidAsync("download", url);

    public async ValueTask<bool> CopyTextAsync(string text) =>
        await (await Module).InvokeAsync<bool>("copyText", text);

    public async ValueTask FocusAsync(ElementReference element, bool selectStem = false) =>
        await (await Module).InvokeVoidAsync("focus", element, selectStem);

    public async ValueTask FitToViewportAsync(ElementReference element) =>
        await (await Module).InvokeVoidAsync("fitToViewport", element);

    public async ValueTask<string?> GetStoredAsync(string key) =>
        await (await Module).InvokeAsync<string?>("getStored", key);

    public async ValueTask SetStoredAsync(string key, string value) =>
        await (await Module).InvokeVoidAsync("setStored", key, value);

    /// <summary>
    /// Raises a bubbling DOM event on the root, for hosts that aren't Blazor. The detail is
    /// passed as JSON text so the payload survives trimming in WebAssembly.
    /// </summary>
    public async ValueTask DispatchAsync(ElementReference root, string eventName, string detailJson) =>
        await (await Module).InvokeVoidAsync("dispatch", root, eventName, detailJson);

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
