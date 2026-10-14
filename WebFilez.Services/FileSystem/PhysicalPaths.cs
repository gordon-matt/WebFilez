namespace WebFilez.Services.FileSystem;

/// <summary>Comparisons on physical (OS) paths.</summary>
internal static class PhysicalPaths
{
    /// <summary>
    /// Windows and macOS default to case-insensitive filesystems; Linux does not. Using the
    /// wrong comparison would either miss a real collision or invent one.
    /// </summary>
    public static StringComparison Comparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static StringComparer Comparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public static bool AreEqual(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            Comparison);

    /// <summary>
    /// Whether <paramref name="candidate"/> is <paramref name="ancestor"/> or sits below it.
    /// Compared with a trailing separator so <c>/data/foo2</c> is not treated as a child of
    /// <c>/data/foo</c>.
    /// </summary>
    public static bool IsWithin(string candidate, string ancestor)
    {
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        string normalizedAncestor = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ancestor));

        if (string.Equals(normalizedCandidate, normalizedAncestor, Comparison))
        {
            return true;
        }

        return normalizedCandidate.StartsWith(normalizedAncestor + Path.DirectorySeparatorChar, Comparison);
    }

    /// <summary>
    /// Returns the given file name if nothing by that name exists in <paramref name="directory"/>,
    /// otherwise appends " (2)", " (3)" and so on, keeping the extension.
    /// </summary>
    public static string FindFreeName(string directory, string name, bool isDirectory)
    {
        if (!File.Exists(Path.Combine(directory, name)) && !Directory.Exists(Path.Combine(directory, name)))
        {
            return name;
        }

        string stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        string extension = isDirectory ? string.Empty : Path.GetExtension(name);

        for (int i = 2; i < 10_000; i++)
        {
            string candidate = $"{stem} ({i}){extension}";
            string full = Path.Combine(directory, candidate);

            if (!File.Exists(full) && !Directory.Exists(full))
            {
                return candidate;
            }
        }

        return $"{stem} ({Guid.NewGuid():N}){extension}";
    }
}
