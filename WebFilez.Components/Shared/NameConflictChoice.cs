namespace WebFilez.Components.Shared;

/// <summary>What to do with one item whose name is already taken.</summary>
public enum NameConflictChoice
{
    /// <summary>Stop asking and skip this item and the ones not yet decided.</summary>
    Cancel,

    /// <summary>Overwrite the existing item.</summary>
    Replace,

    /// <summary>Keep the existing item and write this one under a free name.</summary>
    Rename,

    /// <summary>Leave the existing item and do not write this one.</summary>
    Skip,
}

/// <summary>The button the user pressed, and whether it covers the remaining conflicts.</summary>
public readonly record struct NameConflictAnswer(NameConflictChoice Choice, bool ApplyToRest);
