namespace WebFilez.Components.Infrastructure;

/// <summary>One line of a context or dropdown menu.</summary>
public sealed record MenuEntry(string Label, string Icon, Func<Task> OnSelect, bool IsDanger = false, string? Shortcut = null)
{
    public bool IsSeparator { get; private init; }

    public static MenuEntry Separator { get; } = new(string.Empty, string.Empty, () => Task.CompletedTask) { IsSeparator = true };
}