namespace WebFilez.Services.Storage;

/// <summary>Segment rules shared by every provider, so a name rejected on disk is rejected in a blob store too.</summary>
public static class StorageNames
{
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