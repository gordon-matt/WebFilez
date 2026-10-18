using System.Text.Json;
using WebFilez.Components.Localization;

namespace WebFilez.Tests.Components;

public class LocaleTests
{
    private static readonly string[] RequiredTags =
    [
        "en", "zh-Hans", "zh-Hant", "es-MX", "es-ES", "hi", "pt-BR", "pt-PT", "ja",
        "fr-FR", "fr-CA", "de-DE", "de-AT", "ko", "vi", "id", "ar", "he",
    ];

    [Fact]
    public void ShippedCatalogsShareTheEnglishKeys()
    {
        var english = Read("en");
        Assert.DoesNotContain(english.Keys, key => key.StartsWith('@'));

        foreach (string tag in RequiredTags)
        {
            var catalog = Read(tag);
            var missing = english.Keys.Except(catalog.Keys, StringComparer.Ordinal).ToList();
            Assert.True(missing.Count == 0, $"{tag} is missing {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void MissingKeysFallBackToEnglish()
    {
        using var directory = new TempLocaleDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "qa.json"), """{ "toolbar.new": "QNew" }""");

        var text = new WebFilezLocaleCatalog(new WebFilezLocalizationOptions { Directory = directory.Path }).Resolve("qa-ZZ");

        Assert.Equal("qa", text.Language);
        Assert.Equal("QNew", text["toolbar.new"]);
        Assert.Equal("Images", text["kind.images"]);
        Assert.False(text.IsRightToLeft);
    }

    [Fact]
    public void EmptyOverlayValuesKeepTheBuiltInText()
    {
        using var directory = new TempLocaleDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "fr-FR.json"), """{ "toolbar.new": "", "kind.all": "Tout overlay" }""");

        var text = new WebFilezLocaleCatalog(new WebFilezLocalizationOptions { Directory = directory.Path }).Resolve("fr-FR");

        Assert.Equal("Tout overlay", text["kind.all"]);
        Assert.NotEqual("toolbar.new", text["toolbar.new"]);
        Assert.NotEqual(string.Empty, text["toolbar.new"]);
    }

    [Theory]
    [InlineData("ar", true)]
    [InlineData("he", true)]
    [InlineData("en", false)]
    [InlineData("fr-CA", false)]
    public void RightToLeftFollowsTheCulture(string tag, bool rightToLeft) =>
        Assert.Equal(rightToLeft, WebFilezLocaleCatalog.Shared.Resolve(tag).IsRightToLeft);

    [Fact]
    public void PluralsFollowTheLanguage()
    {
        var arabic = WebFilezLocaleCatalog.Shared.Resolve("ar");
        var french = WebFilezLocaleCatalog.Shared.Resolve("fr-FR");
        var english = WebFilezLocaleCatalog.Shared.Resolve("en");

        Assert.NotEqual(arabic.Plural("count.items", 1), arabic.Plural("count.items", 2));
        Assert.NotEqual(arabic.Plural("count.items", 2), arabic.Plural("count.items", 5));
        Assert.Contains("élément", french.Plural("count.items", 0));
        Assert.DoesNotContain("éléments", french.Plural("count.items", 0));
        Assert.Contains("éléments", french.Plural("count.items", 2));
        Assert.NotEqual(english.Plural("count.items", 1), english.Plural("count.items", 2));
        Assert.Equal("New", english["toolbar.new"]);
    }

    private static Dictionary<string, string> Read(string tag)
    {
        string path = Path.Combine(LocalesDirectory(), tag + ".json");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"Could not read {tag}.json.");
    }

    private static string LocalesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "WebFilez.Components", "Locales");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find WebFilez.Components/Locales.");
    }

    private sealed class TempLocaleDirectory : IDisposable
    {
        public TempLocaleDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "webfilez-locales-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
