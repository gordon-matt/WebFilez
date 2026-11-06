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

    /// <summary>
    /// Occurs when changed.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Gets the toasts.
    /// </summary>
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

    /// <summary>
    /// Performs the success operation.
    /// </summary>
    /// <param name="message">The message.</param>
    public void Success(string message) => Show(message, ToastLevel.Success);

    /// <summary>
    /// Performs the error operation.
    /// </summary>
    /// <param name="message">The message.</param>
    public void Error(string message) => Show(message, ToastLevel.Error, TimeSpan.FromSeconds(8));

    /// <summary>
    /// Performs the info operation.
    /// </summary>
    /// <param name="message">The message.</param>
    public void Info(string message) => Show(message, ToastLevel.Info);

    /// <summary>
    /// Performs the show failure operation.
    /// </summary>
    /// <param name="result">The result.</param>
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

    /// <summary>
    /// Performs the dismiss operation.
    /// </summary>
    /// <param name="id">The identifier.</param>
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

    /// <summary>
    /// Specifies the toast level.
    /// </summary>
    public enum ToastLevel
    {
        /// <summary>
        /// The success value.
        /// </summary>
        Success,
        /// <summary>
        /// The error value.
        /// </summary>
        Error,
        /// <summary>
        /// The info value.
        /// </summary>
        Info,
    }

    /// <summary>
    /// Represents the toast.
    /// </summary>
    /// <param name="Id">The identifier.</param>
    /// <param name="Message">The message.</param>
    /// <param name="Level">The level.</param>
    public sealed record Toast(Guid Id, string Message, ToastLevel Level);
}