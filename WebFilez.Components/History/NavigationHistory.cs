namespace WebFilez.Components.History;

/// <summary>Back and forward for the folder the file manager is showing.</summary>
internal sealed class NavigationHistory
{
    public const int Limit = 50;

    // Lists, so a history past the limit can drop its oldest entry. Stack<T> only removes the newest one.
    private readonly List<BrowsePlace> back = [];
    private readonly List<BrowsePlace> forward = [];
    private BrowsePlace? current;

    public bool CanBack => back.Count > 0;

    public bool CanForward => forward.Count > 0;

    /// <summary>Drops the stacks. Used when the host moves the component to a new place.</summary>
    public void Reset(BrowsePlace place)
    {
        current = place;
        back.Clear();
        forward.Clear();
    }

    /// <summary>
    /// Records a move from <paramref name="from"/> to <paramref name="to"/>. Opening the folder
    /// that is already open is not a step, so Back stays disabled.
    /// </summary>
    public void Visit(BrowsePlace? from, BrowsePlace to)
    {
        current = to;
        if (from is null || from.Value.Equals(to))
        {
            return;
        }

        Push(back, from.Value);
        forward.Clear();
    }

    public BrowsePlace? Back()
    {
        if (current is not { } here || back.Count == 0)
        {
            return null;
        }

        Push(forward, here);
        current = Pop(back);
        return current;
    }

    public BrowsePlace? Forward()
    {
        if (current is not { } here || forward.Count == 0)
        {
            return null;
        }

        Push(back, here);
        current = Pop(forward);
        return current;
    }

    /// <summary>
    /// The folder that actually opened, when it differs from the one Back or Forward asked for
    /// because that folder no longer exists.
    /// </summary>
    public void MoveCurrent(BrowsePlace place) => current = place;

    private static void Push(List<BrowsePlace> stack, BrowsePlace place)
    {
        stack.Add(place);
        if (stack.Count > Limit)
        {
            stack.RemoveAt(0);
        }
    }

    private static BrowsePlace Pop(List<BrowsePlace> stack)
    {
        var place = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return place;
    }
}

/// <summary>One folder the navigation history can return to.</summary>
internal readonly struct BrowsePlace : IEquatable<BrowsePlace>
{
    public BrowsePlace(string rootId, string path)
    {
        RootId = rootId;
        Path = path;
    }

    public string RootId { get; }

    public string Path { get; }

    public bool Equals(BrowsePlace other) =>
        string.Equals(RootId, other.RootId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is BrowsePlace other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(RootId),
        StringComparer.OrdinalIgnoreCase.GetHashCode(Path));
}
