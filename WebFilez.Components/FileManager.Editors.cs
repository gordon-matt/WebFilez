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

    [JSInvokable]
    public async Task OnEditorSaved(string fileName)
    {
        toasts.Success(Text.Format("toast.saved", fileName));
        await RefreshAfterChangeAsync();
        StateHasChanged();
    }

    [JSInvokable]
    public void OnEditorFailed(string? message)
    {
        toasts.Error(string.IsNullOrWhiteSpace(message) ? Text["editor.saveFailed"] : message);
        StateHasChanged();
    }

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
