namespace WebFilez.Components.Localization;

/// <summary>
/// Where extra language files live. Each file is named with an IETF tag, such as <c>fr-CA.json</c>,
/// and contains a flat object of string keys. Keys you leave out fall back to English.
/// </summary>
public sealed class WebFilezLocalizationOptions
{
    /// <summary>
    /// A folder of <c>{tag}.json</c> files. Files overlay the built-in catalog for that tag, and a
    /// tag with no built-in file becomes a new language. A path that does not exist is ignored.
    /// </summary>
    public string? Directory { get; set; }
}
