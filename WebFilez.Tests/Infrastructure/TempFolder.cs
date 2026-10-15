namespace WebFilez.Tests.Infrastructure;

/// <summary>A uniquely named folder under the temp directory, deleted on dispose.</summary>
public sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateDirectory(
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "webfilez-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>Writes a file at a slash-separated path under the folder, creating its parents.</summary>
    public string Write(string relativePath, string content = "")
    {
        string path = Resolve(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string CreateDirectory(string relativePath) => Directory.CreateDirectory(Resolve(relativePath)).FullName;

    public string Resolve(string relativePath) =>
        System.IO.Path.Combine([Path, .. relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)]);

    public bool Exists(string relativePath) => File.Exists(Resolve(relativePath)) || Directory.Exists(Resolve(relativePath));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A handle still closing; the OS temp cleanup will get it.
        }
    }
}