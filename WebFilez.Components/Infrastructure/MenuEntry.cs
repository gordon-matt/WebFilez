namespace WebFilez.Components.Infrastructure;

/// <summary>One line of a context or dropdown menu.</summary>
public sealed record MenuEntry(string Label, string Icon, Func<Task> OnSelect, bool IsDanger = false, string? Shortcut = null)
{
    /// <summary>
    /// Gets or sets a value indicating whether this instance is separator.
    /// </summary>
    public bool IsSeparator { get; private init; }

    /// <summary>
    /// Gets the separator.
    /// </summary>
    public static MenuEntry Separator { get; } = new(string.Empty, string.Empty, () => Task.CompletedTask) { IsSeparator = true };
}