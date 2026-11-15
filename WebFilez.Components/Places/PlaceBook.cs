namespace WebFilez.Components.Places;

/// <summary>A folder remembered as a favorite or a recent place.</summary>
/// <param name="RootId">The root that holds the folder.</param>
/// <param name="Path">The folder's path inside that root.</param>
/// <param name="Name">The label shown in the list, captured when the folder was remembered.</param>
public sealed record SavedPlace(string RootId, string Path, string Name);

/// <summary>
/// The favorites and recent lists. Newest entry first. A list, because a full recent list drops
/// its oldest entry, and <see cref="System.Collections.Generic.Stack{T}"/> only removes the newest.
/// </summary>
internal static class PlaceBook
{
    public const int RecentLimit = 20;

    public static bool Contains(IReadOnlyList<SavedPlace> places, string rootId, string path) =>
        places.Any(place => Same(place, rootId, path));

    public static IReadOnlyList<SavedPlace> Add(IReadOnlyList<SavedPlace> places, SavedPlace place)
    {
        if (Contains(places, place.RootId, place.Path))
        {
            return places;
        }

        var next = new List<SavedPlace>(places.Count + 1) { place };
        next.AddRange(places);
        return next;
    }

    public static IReadOnlyList<SavedPlace> Remove(IReadOnlyList<SavedPlace> places, string rootId, string path) =>
        places.Where(place => !Same(place, rootId, path)).ToList();

    /// <summary>Puts the folder first and forgets the oldest once <see cref="RecentLimit"/> is passed.</summary>
    public static IReadOnlyList<SavedPlace> Remember(IReadOnlyList<SavedPlace> recent, SavedPlace place)
    {
        var next = new List<SavedPlace>(recent.Count + 1) { place };
        next.AddRange(recent.Where(existing => !Same(existing, place.RootId, place.Path)));
        return next.Count <= RecentLimit ? next : next.Take(RecentLimit).ToList();
    }

    private static bool Same(SavedPlace place, string rootId, string path) =>
        string.Equals(place.RootId, rootId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(place.Path, path, StringComparison.OrdinalIgnoreCase);
}
