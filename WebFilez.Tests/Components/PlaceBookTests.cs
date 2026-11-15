using WebFilez.Components.Places;

namespace WebFilez.Tests.Components;

public class PlaceBookTests
{
    [Fact]
    public void Add_PutsANewFolderFirst_AndIgnoresADuplicate()
    {
        var first = Place("files", "/a", "A");
        var second = Place("files", "/b", "B");

        var added = PlaceBook.Add([first], second);
        var again = PlaceBook.Add(added, new SavedPlace("files", "/B", "Other"));

        Assert.Equal(["/b", "/a"], added.Select(place => place.Path));
        Assert.Equal(2, again.Count);
        Assert.Equal("B", again[0].Name);
    }

    [Fact]
    public void Remove_DropsOnlyTheMatchingFolder()
    {
        var left = PlaceBook.Remove(
            [Place("files", "/a", "A"), Place("media", "/a", "A")],
            "files",
            "/a");

        Assert.Equal(["media"], left.Select(place => place.RootId));
    }

    [Fact]
    public void Remember_PromotesAnExistingFolder_AndDropsTheOldestPastTheLimit()
    {
        var recent = Enumerable.Range(0, PlaceBook.RecentLimit)
            .Select(index => Place("files", $"/f{index}", $"F{index}"))
            .ToList();

        var remembered = PlaceBook.Remember(recent, Place("files", "/f0", "F0"));
        var overflow = PlaceBook.Remember(recent, Place("files", "/new", "New"));

        Assert.Equal("/f0", remembered[0].Path);
        Assert.Equal(PlaceBook.RecentLimit, remembered.Count);
        Assert.Equal("/new", overflow[0].Path);
        Assert.Equal(PlaceBook.RecentLimit, overflow.Count);
        Assert.DoesNotContain(overflow, place => place.Path == $"/f{PlaceBook.RecentLimit - 1}");
    }

    private static SavedPlace Place(string rootId, string path, string name) => new(rootId, path, name);
}
