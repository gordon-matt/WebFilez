namespace WebFilez.Services.Access;

public enum FileActivityKind
{
    CreateFolder = 0,
    CreateFile = 1,
    Upload = 2,
    Modify = 3,
    Rename = 4,
    Copy = 5,
    Move = 6,
    Delete = 7,
    Download = 8,
    Compress = 9,
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
/// Told about every successful change and download, for audit logs, search indexes or cache
/// invalidation. The default does nothing. Failures thrown here are logged and swallowed: the
/// file operation has already happened and can't be rolled back.
/// </summary>
public interface IFileActivitySink
{
    ValueTask RecordAsync(FileActivity activity, CancellationToken cancellationToken = default);
}

public sealed class NullFileActivitySink : IFileActivitySink
{
    public ValueTask RecordAsync(FileActivity activity, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}