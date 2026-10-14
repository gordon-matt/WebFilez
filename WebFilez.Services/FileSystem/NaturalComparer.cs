namespace WebFilez.Services.FileSystem;

/// <summary>
/// Orders names the way a person expects: <c>file2</c> before <c>file10</c>, because the digit
/// runs are compared as numbers rather than character by character. Plain ordinal sorting puts
/// <c>file10</c> first, which looks broken in any folder of numbered episodes or scans.
/// </summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    private NaturalComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int i = 0;
        int j = 0;

        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int comparison = CompareNumericRun(x, ref i, y, ref j);
                if (comparison != 0)
                {
                    return comparison;
                }

                continue;
            }

            int charComparison = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (charComparison != 0)
            {
                return charComparison;
            }

            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }

    /// <summary>
    /// Compares the digit runs starting at the two cursors, advancing both past them. Compared by
    /// length after skipping leading zeros, so arbitrarily long runs work without overflowing.
    /// </summary>
    private static int CompareNumericRun(string x, ref int i, string y, ref int j)
    {
        while (i < x.Length && x[i] == '0')
        {
            i++;
        }

        while (j < y.Length && y[j] == '0')
        {
            j++;
        }

        int startX = i;
        int startY = j;

        while (i < x.Length && char.IsDigit(x[i]))
        {
            i++;
        }

        while (j < y.Length && char.IsDigit(y[j]))
        {
            j++;
        }

        int lengthX = i - startX;
        int lengthY = j - startY;

        if (lengthX != lengthY)
        {
            return lengthX.CompareTo(lengthY);
        }

        return string.CompareOrdinal(x, startX, y, startY, lengthX);
    }
}
