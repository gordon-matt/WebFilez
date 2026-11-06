namespace WebFilez.Components.Localization;

/// <summary>A language the file manager can show, named for the language menu.</summary>
public sealed record WebFilezLanguage(string Tag, string NativeName, bool IsRightToLeft);

/// <summary>Loads JSON catalogs and resolves a tag, including fallback to English.</summary>
public interface IWebFilezLocalizer
{
    /// <summary>
    /// Resolves the value.
    /// </summary>
    /// <param name="languageTag">The language tag.</param>
    /// <returns>The web filez text.</returns>
    WebFilezText Resolve(string? languageTag);

    /// <summary>
    /// Gets the languages.
    /// </summary>
    IReadOnlyList<WebFilezLanguage> Languages { get; }
}
