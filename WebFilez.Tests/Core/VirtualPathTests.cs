namespace WebFilez.Tests.Core;

public class VirtualPathTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("  ", "/")]
    [InlineData("/", "/")]
    [InlineData("a/b", "/a/b")]
    [InlineData("//a///b//", "/a/b")]
    [InlineData(@"a\b\c", "/a/b/c")]
    [InlineData("/a/./b/../c", "/a/c")]
    [InlineData("/../../etc/passwd", "/etc/passwd")]
    [InlineData("/a/../../..", "/")]
    public void Normalize_CanonicalisesAndNeverClimbsAboveTheRoot(string? input, string expected) =>
        Assert.Equal(expected, VirtualPath.Normalize(input));

    [Theory]
    [InlineData("/a/b", "/a", true)]
    [InlineData("/a", "/a", true)]
    [InlineData("/a/b", "/", true)]
    [InlineData("/ab", "/a", false)]
    [InlineData("/a", "/a/b", false)]
    public void IsSameOrUnder_ComparesWholeSegments(string candidate, string ancestor, bool expected) =>
        Assert.Equal(expected, VirtualPath.IsSameOrUnder(candidate, ancestor));

    [Fact]
    public void GetParent_StopsAtTheRoot()
    {
        Assert.Equal("/a", VirtualPath.GetParent("/a/b"));
        Assert.Equal("/", VirtualPath.GetParent("/a"));
        Assert.Null(VirtualPath.GetParent("/"));
    }

    [Fact]
    public void Combine_AppendsSegments() =>
        Assert.Equal("/a/b/c", VirtualPath.Combine("/a", "b", "c"));

    [Theory]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("..", "")]
    [InlineData("a/b", "a_b")]
    [InlineData(@"..\..\x", ".._.._x")]
    [InlineData("name.", "name")]
    public void SanitizeSegment_ReplacesSeparatorsAndDropsDotOnlyNames(string input, string expected) =>
        Assert.Equal(expected, VirtualPath.SanitizeSegment(input));
}