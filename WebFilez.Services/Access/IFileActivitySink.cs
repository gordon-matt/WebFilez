namespace WebFilez.Services.Access;

/// <summary>What a successful file operation did.</summary>
public enum FileActivityKind
{
    /// <summary>A folder was created.</summary>
    CreateFolder = 0,

    /// <summary>An empty file was created.</summary>
    CreateFile = 1,

    /// <summary>A file was uploaded.</summary>
    Upload = 2,

    /// <summary>A file's contents were replaced.</summary>
    Modify = 3,

    /// <summary>An item was renamed.</summary>
    Rename = 4,

    /// <summary>An item was copied.</summary>
    Copy = 5,

    /// <summary>An item was moved.</summary>
    Move = 6,

    /// <summary>An item was deleted.</summary>
    Delete = 7,

    /// <summary>A file or a zip was downloaded.</summary>
    Download = 8,

    /// <summary>An archive was written.</summary>
    Compress = 9,

    /// <summary>An archive was extracted.</summary>
    Extract = 10,
}

/// <summary>One successful change or download.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="RootId">The root the item was in.</param>
/// <param name="Path">The item acted on.</param>
/// <param name="TargetPath">Where it ended up, for renames, copies and moves.</param>
/// <param name="TargetRootId">The destination root, when the item changed roots.</param>
/// <param name="SizeBytes">The size, when the operation knows it.</param>
public sealed record FileActivity(
    FileActivityKind Kind,
    string RootId,
    string Path,
    string? TargetPath = null,
    string? TargetRootId = null,
    long? SizeBytes = null);

/// <summary>
/// Told about every successful change and download, for audit logs or cache invalidation. The
/// default does nothing. Failures thrown here are logged and swallowed: the file operation has
/// already happened and can't be rolled back. The built-in search index is updated from the same
/// activities; replace <c>IFileIndex</c> to keep a different store in step.
/// </summary>
public interface IFileActivitySink
{
    /// <summary>Records one successful change or download. An exception here is logged by the caller and does not undo the file operation.</summary>
    /// <param name="activity">What happened.</param>
    /// <param name="cancellationToken">Cancels the record. The file operation has already finished.</param>
    /// <returns>A task that completes once the sink has accepted the activity.</returns>
    ValueTask RecordAsync(FileActivity activity, CancellationToken cancellationToken = default);
}

/// <summary>The sink used when the host registers none. It records nothing.</summary>
public sealed class NullFileActivitySink : IFileActivitySink
{
    /// <inheritdoc />
    public ValueTask RecordAsync(FileActivity activity, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}