using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebFilez.Components;

/// <summary>A file chosen in picker mode.</summary>
/// <param name="Url">Downloads the file through the WebFilez API, with permission checks.</param>
/// <param name="PublicUrl">The file's public address, when its root has one. Usually what a CMS field wants.</param>
public sealed record SelectedFile(
    string RootId,
    string Path,
    string Name,
    long SizeBytes,
    string MimeType,
    string Url,
    string? PublicUrl);

/// <summary>A folder, as raised by <see cref="FileManager.OnNavigated"/>.</summary>
/// <param name="PublicUrl">Public address of the open folder, with a trailing slash, when its root has one.</param>
public sealed record FileLocation(string RootId, string Path, string? PublicUrl = null);

/// <summary>The <c>detail</c> of the <c>wf-selected</c> DOM event.</summary>
public sealed record FileSelection(IReadOnlyList<SelectedFile> Files);

/// <summary>The file handed to a registered editor. <paramref name="Url"/> downloads it through the API.</summary>
public sealed record EditorFile(
    string Name,
    string Path,
    string RootId,
    string Url,
    string MimeType,
    string Theme,
    string Language,
    string LoadingLabel);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(FileSelection))]
[JsonSerializable(typeof(FileLocation))]
[JsonSerializable(typeof(EditorFile))]
[JsonSerializable(typeof(FileEditor[]))]
internal sealed partial class FileManagerEventJsonContext : JsonSerializerContext;