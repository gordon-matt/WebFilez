namespace WebFilez.Components.History;

/// <summary>
/// Undo and redo of the changes made in this file manager. Delete is not on the stack: it asks
/// for confirmation, and putting the bytes back would need a trash this component does not have.
/// </summary>
internal sealed class EditHistory
{
    public const int Limit = 50;

    // Lists, so the undo history can drop its oldest action past the limit. Stack<T> only removes the newest one.
    // Redo stays within the limit on its own: it only receives actions taken off undo.
    private readonly List<HistoryAction> undo = [];
    private readonly List<HistoryAction> redo = [];

    public bool CanUndo => undo.Count > 0;

    public bool CanRedo => redo.Count > 0;

    public void Push(HistoryAction action)
    {
        undo.Add(action);
        if (undo.Count > Limit)
        {
            undo.RemoveAt(0);
        }

        redo.Clear();
    }

    public HistoryAction? TryUndo()
    {
        if (undo.Count == 0)
        {
            return null;
        }

        var action = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        redo.Add(action);
        return action;
    }

    public HistoryAction? TryRedo()
    {
        if (redo.Count == 0)
        {
            return null;
        }

        var action = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        undo.Add(action);
        return action;
    }

    /// <summary>Puts the action <see cref="TryUndo"/> just handed out back, because applying it failed.</summary>
    public void RevertUndo()
    {
        if (redo.Count == 0)
        {
            return;
        }

        undo.Add(redo[^1]);
        redo.RemoveAt(redo.Count - 1);
    }

    /// <summary>Puts the action <see cref="TryRedo"/> just handed out back, because applying it failed.</summary>
    public void RevertRedo()
    {
        if (undo.Count == 0)
        {
            return;
        }

        redo.Add(undo[^1]);
        undo.RemoveAt(undo.Count - 1);
    }
}
