namespace WebFilez.Tests.Core;

public class FileKindsTests
{
    [Theory]
    [InlineData("photo.JPG", "image/*", true)]
    [InlineData("photo.jpg", "image/png", false)]
    [InlineData("report.pdf", "image/*,.pdf", true)]
    [InlineData("notes.txt", ".pdf, .docx", false)]
    [InlineData("anything.bin", null, true)]
    [InlineData("anything.bin", "", true)]
    public void IsAccepted_FollowsHtmlAcceptLists(string fileName, string? accept, bool expected) =>
        Assert.Equal(expected, FileKinds.IsAccepted(fileName, accept));

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("application/pdf", true)]
    [InlineData("text/plain", true)]
    [InlineData("image/svg+xml", false)]
    [InlineData("text/html", false)]
    [InlineData("application/octet-stream", false)]
    public void IsInlineSafe_RefusesTypesThatCanRunScript(string mimeType, bool expected) =>
        Assert.Equal(expected, FileKinds.IsInlineSafe(mimeType));

    [Fact]
    public void Classify_UsesTheExtension()
    {
        Assert.Equal(FileKind.Image, FileKinds.Classify("a.webp", isDirectory: false));
        Assert.Equal(FileKind.Archive, FileKinds.Classify("a.zip", isDirectory: false));
        Assert.Equal(FileKind.Folder, FileKinds.Classify("a.zip", isDirectory: true));
        Assert.Equal(FileKind.Other, FileKinds.Classify("a.unknown", isDirectory: false));
    }
}
