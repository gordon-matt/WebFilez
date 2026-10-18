using System.Globalization;

namespace WebFilez.Components.Localization;

/// <summary>
/// The strings for one language. The indexer returns English when this catalog has no value for
/// a key, and the key itself only when English has no value either.
/// </summary>
public sealed class WebFilezText
{
    public static WebFilezText English { get; } = WebFilezLocaleCatalog.Shared.Resolve("en");

    private readonly IReadOnlyList<Dictionary<string, string>> chain;

    internal WebFilezText(string language, CultureInfo culture, bool isRightToLeft, IReadOnlyList<Dictionary<string, string>> chain)
    {
        Language = language;
        Culture = culture;
        IsRightToLeft = isRightToLeft;
        this.chain = chain;
    }

    public string Language { get; }

    public CultureInfo Culture { get; }

    public bool IsRightToLeft { get; }

    public string this[string key] => Get(key);

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
