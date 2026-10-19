namespace WebFilez.Components.Http;

/// <summary>
/// The confinement token the component attaches to the API calls it makes itself. Scoped, so one
/// file manager does not hand its token to another.
/// </summary>
internal sealed class FileScopeToken
{
    public string? Value { get; set; }
}
