using System.Globalization;

namespace WebFilez.Components.Localization;

/// <summary>
/// The strings for one language. The indexer returns English when this catalog has no value for
/// a key, and the key itself only when English has no value either.
/// </summary>
public sealed class WebFilezText
{
    /// <summary>The English catalog, used when a translation is missing.</summary>
    public static WebFilezText English { get; } = WebFilezLocaleCatalog.Shared.Resolve("en");

    private readonly IReadOnlyList<Dictionary<string, string>> chain;

    internal WebFilezText(string language, CultureInfo culture, bool isRightToLeft, IReadOnlyList<Dictionary<string, string>> chain)
    {
        Language = language;
        Culture = culture;
        IsRightToLeft = isRightToLeft;
        this.chain = chain;
    }

    /// <summary>The tag that was resolved, such as <c>fr-CA</c>.</summary>
    public string Language { get; }

    /// <summary>The culture used to format numbers and dates.</summary>
    public CultureInfo Culture { get; }

    /// <summary>Whether the catalog asks for a right-to-left layout.</summary>
    public bool IsRightToLeft { get; }

    /// <summary>The text for a key. A missing key falls back, and the key itself is returned when English has none.</summary>
    /// <param name="key">The catalog key.</param>
    /// <returns>The translated text, or the key.</returns>
    public string this[string key] => Get(key);

    /// <summary>The text for a key. A missing key falls back, and the key itself is returned when English has none.</summary>
    /// <param name="key">The catalog key.</param>
    /// <returns>The translated text, or the key.</returns>
    public string Get(string key)
    {
        foreach (var catalog in chain)
        {
            if (catalog.TryGetValue(key, out var value) && value.Length > 0)
            {
                return value;
            }
        }

        return key;
    }

    /// <summary>Formats the text for a key with <paramref name="args"/>. A format that does not match the arguments returns the template unchanged.</summary>
    /// <param name="key">The catalog key.</param>
    /// <param name="args">The values substituted into the template.</param>
    /// <returns>The formatted text, or the template when the format is wrong.</returns>
    public string Format(string key, params object?[] args)
    {
        string template = Get(key);
        try
        {
            return string.Format(Culture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <summary>
    /// Picks <c>{key}.one</c>, <c>{key}.other</c> and the other CLDR categories this language uses.
    /// A missing category uses <c>.other</c>, then English.
    /// </summary>
    /// <param name="key">The catalog key, without the category suffix.</param>
    /// <param name="count">The number that selects the category and is formatted into the text.</param>
    /// <returns>The chosen phrase.</returns>
    public string Plural(string key, int count)
    {
        foreach (string category in Categories(count))
        {
            string candidate = key + "." + category;
            if (Has(candidate))
            {
                return Format(candidate, count);
            }
        }

        return Format(key, count);
    }

    private bool Has(string key)
    {
        foreach (var catalog in chain)
        {
            if (catalog.TryGetValue(key, out var value) && value.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<string> Categories(int count)
    {
        switch (PrimaryLanguage())
        {
            case "ar":
                if (count == 0)
                {
                    yield return "zero";
                }
                else if (count == 1)
                {
                    yield return "one";
                }
                else if (count == 2)
                {
                    yield return "two";
                }
                else if (count % 100 is >= 3 and <= 10)
                {
                    yield return "few";
                }
                else if (count % 100 is >= 11 and <= 99)
                {
                    yield return "many";
                }

                break;

            case "he":
                if (count == 1)
                {
                    yield return "one";
                }
                else if (count == 2)
                {
                    yield return "two";
                }
                else if (count != 0 && count % 10 == 0)
                {
                    yield return "many";
                }

                break;

            case "fr":
            case "hi":
                if (count is 0 or 1)
                {
                    yield return "one";
                }

                break;

            case "pl":
                if (count == 1)
                {
                    yield return "one";
                }
                else if (count % 10 is >= 2 and <= 4 && count % 100 is not (>= 12 and <= 14))
                {
                    yield return "few";
                }
                else
                {
                    yield return "many";
                }

                break;

            case "ru":
                if (count % 10 == 1 && count % 100 != 11)
                {
                    yield return "one";
                }
                else if (count % 10 is >= 2 and <= 4 && count % 100 is not (>= 12 and <= 14))
                {
                    yield return "few";
                }
                else
                {
                    yield return "many";
                }

                break;

            case "zh":
            case "ja":
            case "ko":
            case "vi":
            case "id":
                break;

            default:
                if (count == 1)
                {
                    yield return "one";
                }

                break;
        }

        yield return "other";
    }

    private string PrimaryLanguage()
    {
        int dash = Language.IndexOf('-');
        return dash < 0 ? Language : Language[..dash];
    }
}
