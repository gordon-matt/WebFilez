namespace WebFilez.Components;

/// <summary>An editor a host page has registered, and the files it offers to open.</summary>
/// <param name="Id">The id passed to <c>registerEditor</c>.</param>
/// <param name="Name">The label shown as “Edit with {name}”.</param>
/// <param name="Accept">An HTML accept list, such as <c>.jpg,.png,image/webp</c>.</param>
public sealed record FileEditor(string Id, string Name, string Accept);

/// <summary>
/// Decides which registered editor opens a file. The page registers editors in JavaScript, each
/// with the types it can handle. <see cref="FileManager.Editors"/> can give one file manager a
/// different mapping, as <c>images:.jpg,.png|annotate:.webp</c>.
/// </summary>
public static class FileEditors
{
    /// <summary>
    /// Splits a mapping into editor ids and, when a colon is present, the accept list that
    /// replaces the one the editor registered. An id on its own keeps its own list.
    /// </summary>
    public static IReadOnlyList<(string Id, string? Accept)> Parse(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
        {
            return [];
        }

        var bindings = new List<(string Id, string? Accept)>();
        foreach (string entry in configuration.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = entry.IndexOf(':');
            string id = (colon < 0 ? entry : entry[..colon]).Trim();
            if (id.Length == 0)
            {
                continue;
            }

            string? accept = colon < 0 ? null : entry[(colon + 1)..].Trim();
            bindings.Add((id, string.IsNullOrEmpty(accept) ? null : accept));
        }

        return bindings;
    }

    /// <summary>
    /// Editors that claim <paramref name="fileName"/>, in the order they should be offered.
    /// An empty <paramref name="configuration"/> uses every registered editor and its own accept
    /// list. A configuration limits the file manager to the editors it names, in that order, and
    /// each accept list replaces the one the editor registered.
    /// </summary>
    public static IReadOnlyList<FileEditor> Match(
        IReadOnlyList<FileEditor> registered,
        string? configuration,
        string fileName)
    {
        var bindings = Parse(configuration);
        if (bindings.Count == 0)
        {
            return registered.Where(editor => Claims(editor.Accept, fileName)).ToList();
        }

        var byId = registered.ToDictionary(editor => editor.Id, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<FileEditor>();

        foreach (var (id, accept) in bindings)
        {
            if (!seen.Add(id) || !byId.TryGetValue(id, out var editor))
            {
                continue;
            }

            string effective = accept ?? editor.Accept;
            if (Claims(effective, fileName))
            {
                matches.Add(accept is null ? editor : editor with { Accept = effective });
            }
        }

        return matches;
    }

    private static bool Claims(string accept, string fileName) =>
        accept.Length > 0 && FileKinds.IsAccepted(fileName, accept);
}
