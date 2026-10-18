namespace WebFilez.Components.Localization;

/// <summary>A language the file manager can show, named for the language menu.</summary>
public sealed record WebFilezLanguage(string Tag, string NativeName, bool IsRightToLeft);

/// <summary>Loads JSON catalogs and resolves a tag, including fallback to English.</summary>
public interface IWebFilezLocalizer
{
    WebFilezText Resolve(string? languageTag);

    IReadOnlyList<WebFilezLanguage> Languages { get; }
}
