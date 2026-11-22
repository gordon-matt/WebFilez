using System.Text.Json;

namespace WebFilez.Components;

public sealed partial class FileManager
{
    private async Task<IReadOnlyList<FileEditor>> EditorsForAsync(FileSystemItemDto item)
    {
        if (!isAttached || IsPicker)
        {
            return [];
        }

        try
        {
            string json = await interop.ListEditorsAsync();
            var registered = JsonSerializer.Deserialize(json, FileManagerEventJsonContext.Default.FileEditorArray) ?? [];
            return FileEditors.Match(registered, Editors, item.Name);
        }
        catch (JSException)
        {
            return [];
        }
    }

    private async Task OpenExternalEditorAsync(string itemRoot, FileSystemItemDto item, FileEditor editor)
    {
        if (callbackReference is null)
        {
            return;
        }

        // The ticks make each version a different URL, so the editor does not open a cached copy
        // of the file it just saved.
        string url = urls.Download(itemRoot, item.Path, inline: true) + "&v=" + item.ModifiedAt.Ticks;
        var payload = new EditorFile(
            item.Name,
            item.Path,
            itemRoot,
            url,
            FileKinds.GetMimeType(item.Name),
            Theme,
            Text.Language,
            Text["editor.opening"]);

        try
        {
            await interop.OpenEditorAsync(
                editor.Id,
                JsonSerializer.Serialize(payload, FileManagerEventJsonContext.Default.EditorFile),
                urls.Upload,
                urls.IncludeCredentials,
                callbackReference);
        }
        catch (JSException)
        {
            toasts.Error(Text["editor.openFailed"]);
            StateHasChanged();
        }
    }

    /// <summary>Confirms a save from an editor and refreshes the folder.</summary>
    /// <param name="fileName">The file the editor saved.</param>
    /// <returns>A task that completes once the listing has been refreshed.</returns>
    [JSInvokable]
    public async Task OnEditorSaved(string fileName)
    {
        toasts.Success(Text.Format("toast.saved", fileName));
        await RefreshAfterChangeAsync();
        StateHasChanged();
    }

    /// <summary>Shows an editor failure. A blank message uses the built-in save-failed text.</summary>
    /// <param name="message">The message from the editor, or <see langword="null"/>.</param>
    [JSInvokable]
    public void OnEditorFailed(string? message)
    {
        toasts.Error(string.IsNullOrWhiteSpace(message) ? Text["editor.saveFailed"] : message);
        StateHasChanged();
    }

    /// <summary>Closes the editor this file manager opened and returns focus to the listing.</summary>
    /// <returns>A task that completes once the editor has been closed.</returns>
    [JSInvokable]
    public async Task OnEditorClosed()
    {
        if (callbackReference is not null)
        {
            await interop.CloseEditorAsync(callbackReference);
        }

        if (isAttached)
        {
            await interop.FocusAsync(rootElement);
        }
    }
}
