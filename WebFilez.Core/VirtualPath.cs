namespace WebFilez;

/// <summary>
/// Normalisation and containment helpers for <em>virtual</em> paths: the root-relative, always
/// forward-slashed paths that appear in URLs and API requests. Every path that arrives from a
/// browser goes through <see cref="Normalize"/> before it is mapped onto the disk.
/// </summary>
public static class VirtualPath
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>
    /// Canonicalises a caller-supplied path to the form <c>/</c> or <c>/a/b/c</c>: forward
    /// slashes, single-separated, no trailing slash, with <c>.</c> and <c>..</c> segments
    /// resolved. <c>..</c> can never climb above the root; it is discarded at depth zero, so the
    /// result is always inside the root.
    /// </summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Constants.RootPath;
        }

        var segments = new List<string>();
        foreach (string span in path.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            string segment = span.Trim();
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        return segments.Count == 0
            ? Constants.RootPath
            : Constants.PathSeparator + string.Join(Constants.PathSeparator, segments);
    }

    /// <summary>The segments of a normalized path, root first. Empty for the root.</summary>
    public static string[] GetSegments(string normalizedPath) =>
        normalizedPath == Constants.RootPath || string.IsNullOrEmpty(normalizedPath)
            ? []
            : normalizedPath.Split(Constants.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Last segment, or an empty string for the root.</summary>
    public static string GetName(string normalizedPath)
    {
        if (normalizedPath == Constants.RootPath)
        {
            return string.Empty;
        }

        int index = normalizedPath.LastIndexOf(Constants.PathSeparator);
        return index < 0 ? normalizedPath : normalizedPath[(index + 1)..];
    }

    /// <summary>Parent path, or <c>null</c> when <paramref name="normalizedPath"/> is the root.</summary>
    public static string? GetParent(string normalizedPath)
    {
        if (normalizedPath == Constants.RootPath)
        {
            return null;
        }

        int index = normalizedPath.LastIndexOf(Constants.PathSeparator);
        return index <= 0 ? Constants.RootPath : normalizedPath[..index];
    }

    /// <summary>Appends one or more segments to a normalized path.</summary>
    public static string Combine(string normalizedPath, params string[] segments)
    {
        if (segments.Length == 0)
        {
            return Normalize(normalizedPath);
        }

        string tail = string.Join(Constants.PathSeparator, segments);
        return normalizedPath == Constants.RootPath
            ? Normalize(Constants.PathSeparator + tail)
            : Normalize(normalizedPath + Constants.PathSeparator + tail);
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is <paramref name="ancestor"/> itself or sits
    /// underneath it. Compares whole segments, so <c>/ab</c> is not treated as a child of
    /// <c>/a</c>. Case-insensitive to match Windows and macOS filesystem behaviour.
    /// </summary>
    public static bool IsSameOrUnder(string candidate, string ancestor) => ancestor == Constants.RootPath || candidate.Equals(ancestor, StringComparison.OrdinalIgnoreCase) || (candidate.Length > ancestor.Length
            && candidate[ancestor.Length] == Constants.PathSeparator
            && candidate.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reduces an arbitrary string to something usable as a single path segment: separators and
    /// characters illegal on Windows are replaced with <c>_</c>. Returns an empty string when
    /// nothing usable is left.
    /// </summary>
    public static string SanitizeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        char[] buffer = new char[value.Length];
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            buffer[i] = c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(c)
                ? '_'
                : c;
        }

        // Trailing dots and spaces are silently dropped by Windows, so "a." and "a" would collide.
        string result = new string(buffer).Trim().TrimEnd('.', ' ');
        return result is "." or ".." ? string.Empty : result;
    }
}