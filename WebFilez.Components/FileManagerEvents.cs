using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebFilez.Components;

/// <summary>A file chosen in picker mode.</summary>
/// <param name="RootId">The root the file belongs to.</param>
/// <param name="Path">The virtual path.</param>
/// <param name="Name">The file name.</param>
/// <param name="SizeBytes">The size in bytes.</param>
/// <param name="MimeType">The media type.</param>
/// <param name="Url">Downloads the file through the WebFilez API, with permission checks. For the manager, not for content.</param>
/// <param name="PublicUrl">The address to store in content, when the file has one. Stable unless the host opted into expiring URLs.</param>
public sealed record SelectedFile(
    string RootId,
    string Path,
    string Name,
    long SizeBytes,
    string MimeType,
    string Url,
    string? PublicUrl);

/// <summary>A folder, as raised by <see cref="FileManager.OnNavigated"/>.</summary>
/// <param name="RootId">The root.</param>
/// <param name="Path">The virtual path of the open folder.</param>
/// <param name="PublicUrl">Public address of the open folder, with a trailing slash, when its root has one.</param>
public sealed record FileLocation(string RootId, string Path, string? PublicUrl = null);

/// <summary>The <c>detail</c> of the <c>wf-selected</c> DOM event.</summary>
public sealed record FileSelection(IReadOnlyList<SelectedFile> Files);

/// <summary>The file handed to a registered editor. <paramref name="Url"/> downloads it through the API.</summary>
/// <param name="Name">The file name.</param>
/// <param name="Path">The virtual path.</param>
/// <param name="RootId">The root.</param>
/// <param name="Url">The download address.</param>
/// <param name="MimeType">The media type.</param>
/// <param name="Theme">The file manager's theme.</param>
/// <param name="Language">The file manager's language tag.</param>
/// <param name="LoadingLabel">The text shown while the editor loads.</param>
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
[JsonSerializable(typeof(Places.SavedPlace[]))]
internal sealed partial class FileManagerEventJsonContext : JsonSerializerContext;