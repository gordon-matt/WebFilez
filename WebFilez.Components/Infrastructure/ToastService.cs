namespace WebFilez.Components.Infrastructure;

/// <summary>
/// Transient notifications. Each file manager owns one, so the components need no DI
/// registration and two file managers on a page keep their toasts apart.
/// </summary>
public sealed class ToastService : IDisposable
{
    private readonly Lock gate = new();
    private readonly List<Toast> toasts = [];
    private readonly Dictionary<Guid, Timer> timers = [];

    /// <summary>Raised when a toast is shown or dismissed.</summary>
    public event Action? Changed;

    /// <summary>The toasts currently on screen, oldest first.</summary>
    public IReadOnlyList<Toast> Toasts
    {
        get
        {
            lock (gate)
            {
                return [.. toasts];
            }
        }
    }

    /// <summary>Shows a success that disappears after four seconds.</summary>
    /// <param name="message">The text.</param>
    public void Success(string message) => Show(message, ToastLevel.Success);

    /// <summary>Shows a failure that stays for eight seconds.</summary>
    /// <param name="message">The text.</param>
    public void Error(string message) => Show(message, ToastLevel.Error, TimeSpan.FromSeconds(8));

    /// <summary>Shows a notice that disappears after four seconds.</summary>
    /// <param name="message">The text.</param>
    public void Info(string message) => Show(message, ToastLevel.Info);

    /// <summary>Shows the result's display message as a failure.</summary>
    /// <param name="result">A failed result.</param>
    public void ShowFailure(Ardalis.Result.IResult result) => Error(result.GetDisplayMessage());

    /// <summary>Summarises a bulk operation, naming the first failure so the user knows why.</summary>
    public void ShowBulkResult(BulkOperationResultDto result, string verb)
    {
        string noun = result.SucceededCount == 1 ? "item" : "items";

        if (result.FailedCount == 0)
        {
            Success($"{verb} {result.SucceededCount} {noun}.");
            return;
        }

        var first = result.Results.First(r => !r.Succeeded);
        string reason = $"{VirtualPath.GetName(VirtualPath.Normalize(first.Path))}: {first.Error}";

        Error(result.SucceededCount == 0
            ? $"Nothing was {verb.ToLowerInvariant()}. {reason}"
            : $"{verb} {result.SucceededCount} {noun}; {result.FailedCount} failed. {reason}");
    }

    /// <summary>Removes one toast.</summary>
    /// <param name="id">The toast id.</param>
    public void Dismiss(Guid id)
    {
        lock (gate)
        {
            toasts.RemoveAll(t => t.Id == id);
            if (timers.Remove(id, out var timer))
            {
                timer.Dispose();
            }
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Releases the resources used by this instance.
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            foreach (var timer in timers.Values)
            {
                timer.Dispose();
            }

            timers.Clear();
            toasts.Clear();
        }
    }

    private void Show(string message, ToastLevel level, TimeSpan? duration = null)
    {
        var toast = new Toast(Guid.NewGuid(), message, level);

        lock (gate)
        {
            toasts.Add(toast);
            timers[toast.Id] = new Timer(_ => Dismiss(toast.Id), null, duration ?? TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);
        }

        Changed?.Invoke();
    }

    /// <summary>How a toast is styled.</summary>
    public enum ToastLevel
    {
        /// <summary>A completed action.</summary>
        Success,

        /// <summary>A failure.</summary>
        Error,

        /// <summary>A notice.</summary>
        Info,
    }

    /// <summary>One toast.</summary>
    /// <param name="Id">The id used to dismiss it.</param>
    /// <param name="Message">The text.</param>
    /// <param name="Level">How it is styled.</param>
    public sealed record Toast(Guid Id, string Message, ToastLevel Level);
}
