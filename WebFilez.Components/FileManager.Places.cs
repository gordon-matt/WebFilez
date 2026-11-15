using System.Text.Json;
using WebFilez.Components.Places;

namespace WebFilez.Components;

public sealed partial class FileManager
{
    private const string FavoritesKey = "favorites";
    private const string RecentKey = "recent";
    private const string PlacesOpenKey = "places";

    private IReadOnlyList<SavedPlace> favorites = [];
    private IReadOnlyList<SavedPlace> recent = [];
    private bool placesLoaded;
    private bool placesExpanded = true;

    private IReadOnlyList<SavedPlace> VisibleFavorites => favorites.Where(IsVisiblePlace).ToList();

    private IReadOnlyList<SavedPlace> VisibleRecent => recent.Where(IsVisiblePlace).ToList();

    private bool CanToggleFavorite => SelectedRows() is [var only] && only.Item.IsDirectory;

    private bool SelectedFolderIsFavorite =>
        SelectedRows() is [var only] && only.Item.IsDirectory && IsFavorite(only.RootId, only.Item.Path);

    private bool CurrentFolderIsFavorite => rootId is not null && IsFavorite(rootId, CurrentPath);

    private async Task LoadPlacesAsync()
    {
        var storedFavorites = ParsePlaces(await interop.GetStoredAsync(FavoritesKey));
        var storedRecent = ParsePlaces(await interop.GetStoredAsync(RecentKey));
        placesExpanded = await interop.GetStoredAsync(PlacesOpenKey) is not "closed";
        var pending = recent;

        favorites = storedFavorites;
        recent = storedRecent;
        foreach (var place in pending.AsEnumerable().Reverse())
        {
            recent = PlaceBook.Remember(recent, place);
        }

        placesLoaded = true;
        if (rootId is not null && listing is not null)
        {
            await RememberRecentAsync(rootId, listing.Path);
        }
        else if (pending.Count > 0)
        {
            await SavePlacesAsync(RecentKey, recent);
        }

        StateHasChanged();
    }

    private async Task SetPlacesExpandedAsync(bool expanded)
    {
        placesExpanded = expanded;
        if (placesLoaded)
        {
            await interop.SetStoredAsync(PlacesOpenKey, expanded ? "open" : "closed");
        }
    }

    private Task ToggleSelectedFavoriteAsync()
    {
        if (SelectedRows() is not [var only] || !only.Item.IsDirectory)
        {
            return Task.CompletedTask;
        }

        return ToggleFavoriteAsync(only.RootId, only.Item.Path, only.Item.Name);
    }

    private Task ToggleCurrentFavoriteAsync() =>
        rootId is null ? Task.CompletedTask : ToggleFavoriteAsync(rootId, CurrentPath, PlaceName(rootId, CurrentPath));

    private async Task ToggleFavoriteAsync(string itemRoot, string path, string name)
    {
        if (IsFavorite(itemRoot, path))
        {
            favorites = PlaceBook.Remove(favorites, itemRoot, path);
        }
        else
        {
            favorites = PlaceBook.Add(favorites, new SavedPlace(itemRoot, path, name));
        }

        if (placesLoaded)
        {
            await SavePlacesAsync(FavoritesKey, favorites);
        }
    }

    private async Task RemoveFavoriteAsync(SavedPlace place)
    {
        favorites = PlaceBook.Remove(favorites, place.RootId, place.Path);
        if (placesLoaded)
        {
            await SavePlacesAsync(FavoritesKey, favorites);
        }
    }

    private async Task RememberRecentAsync(string itemRoot, string path)
    {
        var place = new SavedPlace(itemRoot, path, PlaceName(itemRoot, path));
        if (!IsVisiblePlace(place))
        {
            return;
        }

        recent = PlaceBook.Remember(recent, place);
        if (placesLoaded)
        {
            await SavePlacesAsync(RecentKey, recent);
        }
    }

    private bool IsFavorite(string itemRoot, string path) => PlaceBook.Contains(favorites, itemRoot, path);

    private bool IsVisiblePlace(SavedPlace place)
    {
        if (!roots.Any(root => string.Equals(root.Id, place.RootId, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!IsConfined)
        {
            return true;
        }

        return string.Equals(place.RootId, rootId, StringComparison.OrdinalIgnoreCase)
            && VirtualPath.IsSameOrUnder(place.Path, Floor);
    }

    private string PlaceName(string itemRoot, string path)
    {
        if (string.Equals(path, Constants.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return roots.FirstOrDefault(root => string.Equals(root.Id, itemRoot, StringComparison.OrdinalIgnoreCase))?.Name
                ?? itemRoot;
        }

        return VirtualPath.GetName(path);
    }

    private async Task SavePlacesAsync(string key, IReadOnlyList<SavedPlace> places) =>
        await interop.SetStoredAsync(
            key,
            JsonSerializer.Serialize(places.ToArray(), FileManagerEventJsonContext.Default.SavedPlaceArray));

    private static IReadOnlyList<SavedPlace> ParsePlaces(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(json, FileManagerEventJsonContext.Default.SavedPlaceArray) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
