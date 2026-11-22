namespace WebFilez.Components.Localization;

/// <summary>A language the file manager can show, named for the language menu.</summary>
public sealed record WebFilezLanguage(string Tag, string NativeName, bool IsRightToLeft);

/// <summary>Loads JSON catalogs and resolves a tag, including fallback to English.</summary>
public interface IWebFilezLocalizer
{
    /// <summary>
    /// The strings for a language tag. A blank tag is English. A missing catalog falls back through parent tags, then English.
    /// </summary>
    /// <param name="languageTag">An IETF tag such as <c>fr-CA</c>, or <see langword="null"/>.</param>
    /// <returns>The strings, including the fallback chain.</returns>
    WebFilezText Resolve(string? languageTag);

    /// <summary>The languages a menu can offer, including ones the host added.</summary>
    IReadOnlyList<WebFilezLanguage> Languages { get; }
}
