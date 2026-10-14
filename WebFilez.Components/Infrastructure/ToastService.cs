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

    public event Action? Changed;

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

    public void Success(string message) => Show(message, ToastLevel.Success);

    public void Error(string message) => Show(message, ToastLevel.Error, TimeSpan.FromSeconds(8));

    public void Info(string message) => Show(message, ToastLevel.Info);

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

    public enum ToastLevel
    {
        Success,
        Error,
        Info,
    }

    public sealed record Toast(Guid Id, string Message, ToastLevel Level);
}
