using System.Globalization;
using System.Text.Json;

namespace WebFilez.Components.Localization;

/// <summary>
/// Built-in catalogs are embedded next to this assembly. A host adds a language by dropping
/// <c>{tag}.json</c> in <see cref="WebFilezLocalizationOptions.Directory"/>, or in
/// <c>WebFilez/Locales</c> beside the app. Missing keys fall back through parent tags to English.
/// </summary>
public sealed class WebFilezLocaleCatalog : IWebFilezLocalizer
{
    private const string ResourcePrefix = "WebFilez.Components.Locales.";
    private const string RtlKey = "@rtl";

    public static WebFilezLocaleCatalog Shared { get; } = new(new WebFilezLocalizationOptions());

    private readonly Dictionary<string, Dictionary<string, string>> catalogs = new(StringComparer.OrdinalIgnoreCase);

    public WebFilezLocaleCatalog(WebFilezLocalizationOptions options)
    {
        LoadEmbedded();
        LoadDirectory(options.Directory);
        LoadDirectory(Path.Combine(AppContext.BaseDirectory, "WebFilez", "Locales"));
    }

    public IReadOnlyList<WebFilezLanguage> Languages { get; private set; } = [];

    public WebFilezText Resolve(string? languageTag)
    {
        string requested = string.IsNullOrWhiteSpace(languageTag) ? "en" : languageTag.Trim().Replace('_', '-');
        var chain = new List<Dictionary<string, string>>();
        string? matched = null;

        foreach (string tag in FallbackTags(requested))
        {
            if (!catalogs.TryGetValue(tag, out var catalog))
            {
                continue;
            }

            matched ??= tag;
            chain.Add(catalog);
        }

        if (catalogs.TryGetValue("en", out var english) && chain.All(catalog => !ReferenceEquals(catalog, english)))
        {
            chain.Add(english);
        }

        string language = matched ?? "en";
        var culture = TryCulture(language) ?? CultureInfo.InvariantCulture;
        bool rtl = culture.TextInfo.IsRightToLeft || chain.Any(catalog =>
            catalog.TryGetValue(RtlKey, out var flag) && flag.Equals("true", StringComparison.OrdinalIgnoreCase));

        return new WebFilezText(language, culture, rtl, chain);
    }

    private void LoadEmbedded()
    {
        var assembly = typeof(WebFilezLocaleCatalog).Assembly;
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string tag = name[ResourcePrefix.Length..^".json".Length];
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is not null)
            {
                Merge(tag, stream, replace: false);
            }
        }

        RebuildLanguages();
    }

    private void LoadDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
        {
            string tag = Path.GetFileNameWithoutExtension(file);
            if (!IsLanguageTag(tag))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(file);
                Merge(tag, stream, replace: true);
            }
            catch (IOException)
            {
                // A file the host is still writing should not take the file manager down.
            }
            catch (JsonException)
            {
                // One broken catalog is skipped; its keys keep their built-in or English text.
            }
        }

        RebuildLanguages();
    }

    private void Merge(string tag, Stream stream, bool replace)
    {
        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
        if (parsed is null)
        {
            return;
        }

        if (!catalogs.TryGetValue(tag, out var target))
        {
            target = new Dictionary<string, string>(StringComparer.Ordinal);
            catalogs[tag] = target;
        }

        foreach (var (key, value) in parsed)
        {
            if (string.IsNullOrEmpty(key) || (!replace && target.ContainsKey(key)))
            {
                continue;
            }

            if (string.IsNullOrEmpty(value) && !key.StartsWith('@'))
            {
                continue;
            }

            target[key] = value;
        }
    }

    private void RebuildLanguages()
    {
        Languages = catalogs.Keys
            .OrderBy(tag => tag.Equals("en", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(tag => NativeName(tag), StringComparer.CurrentCultureIgnoreCase)
            .Select(tag =>
            {
                var culture = TryCulture(tag);
                bool rtl = culture?.TextInfo.IsRightToLeft == true
                    || (catalogs.TryGetValue(tag, out var catalog)
                        && catalog.TryGetValue(RtlKey, out var flag)
                        && flag.Equals("true", StringComparison.OrdinalIgnoreCase));
                return new WebFilezLanguage(tag, NativeName(tag), rtl);
            })
            .ToList();
    }

    private static IEnumerable<string> FallbackTags(string tag)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (yielded.Add(tag))
        {
            yield return tag;
        }

        var culture = TryCulture(tag);
        if (culture is not null)
        {
            while (culture.Parent is { Name.Length: > 0 } parent)
            {
                if (yielded.Add(parent.Name))
                {
                    yield return parent.Name;
                }

                culture = parent;
            }
        }
        else
        {
            int dash = tag.LastIndexOf('-');
            while (dash > 0)
            {
                string parent = tag[..dash];
                if (yielded.Add(parent))
                {
                    yield return parent;
                }

                dash = parent.LastIndexOf('-');
            }
        }

        if (yielded.Add("en"))
        {
            yield return "en";
        }
    }

    private static string NativeName(string tag) => TryCulture(tag)?.NativeName ?? tag;

    private static CultureInfo? TryCulture(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static bool IsLanguageTag(string tag) =>
        tag.Length is >= 2 and <= 16
        && tag.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
}
