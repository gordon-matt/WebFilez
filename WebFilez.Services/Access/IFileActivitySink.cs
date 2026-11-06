namespace WebFilez.Services.Access;

/// <summary>
/// Specifies the file activity kind.
/// </summary>
public enum FileActivityKind
{
    /// <summary>
    /// The create folder value.
    /// </summary>
    CreateFolder = 0,
    /// <summary>
    /// The create file value.
    /// </summary>
    CreateFile = 1,
    /// <summary>
    /// The upload value.
    /// </summary>
    Upload = 2,
    /// <summary>
    /// The modify value.
    /// </summary>
    Modify = 3,
    /// <summary>
    /// The rename value.
    /// </summary>
    Rename = 4,
    /// <summary>
    /// The copy value.
    /// </summary>
    Copy = 5,
    /// <summary>
    /// The move value.
    /// </summary>
    Move = 6,
    /// <summary>
    /// The delete value.
    /// </summary>
    Delete = 7,
    /// <summary>
    /// The download value.
    /// </summary>
    Download = 8,
    /// <summary>
    /// The compress value.
    /// </summary>
    Compress = 9,
    /// <summary>
    /// The extract value.
    /// </summary>
    Extract = 10,
}

/// <param name="Path">The item acted on.</param>
/// <param name="TargetPath">Where it ended up, for renames, copies and moves.</param>
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
    /// <summary>
    /// Performs the record operation.
    /// </summary>
    /// <param name="activity">The activity.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask RecordAsync(FileActivity activity, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the null file activity sink.
/// </summary>
public sealed class NullFileActivitySink : IFileActivitySink
{
    /// <summary>
    /// Performs the record operation.
    /// </summary>
    /// <param name="activity">The activity.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public ValueTask RecordAsync(FileActivity activity, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}