namespace WebFilez.Services.Storage;

/// <summary>Segment rules shared by every provider, so a name rejected on disk is rejected in a blob store too.</summary>
public static class StorageNames
{
    /// <summary>
    /// Whether a single path segment is illegal, hidden, a temporary upload, or excluded.
    /// A hidden name is rejected only when the root does not show hidden files.
    /// </summary>
    /// <param name="root">The root the segment would be created in.</param>
    /// <param name="segment">One path segment, not a full path.</param>
    /// <returns><see langword="true"/> when the name must not be mapped.</returns>
    public static bool IsRejected(FileRoot root, string segment)
    {
        char[] invalid = Path.GetInvalidFileNameChars();

        return segment.IndexOfAny(invalid) >= 0
            || (OperatingSystem.IsWindows() && segment.Contains(':'))
            || (!root.ShowHiddenFiles && segment.StartsWith('.'))
            || segment.EndsWith(Constants.TempFileSuffix, StringComparison.OrdinalIgnoreCase)
            || ExcludeMatcher.For(root).IsExcluded(segment);
    }
}
