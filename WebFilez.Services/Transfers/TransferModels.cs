namespace WebFilez.Services.Transfers;

/// <param name="RelativeDirectory">
/// Subfolder path under <paramref name="Path"/> for a file dropped as part of a folder, such as
/// <c>holiday/day1</c>. Missing folders are created.
/// </param>
public sealed record UploadRequest(
    string RootId,
    string? Path,
    string FileName,
    Stream Content,
    string? RelativeDirectory = null,
    bool Overwrite = false);

/// <summary>An open file ready to stream. The caller owns and disposes <see cref="Content"/>.</summary>
public sealed record FileDownloadDto(
    Stream Content,
    string FileName,
    string MimeType,
    long Length,
    DateTime ModifiedAt,
    string ETag);

public sealed record ThumbnailDto(byte[] Bytes, string MimeType, string ETag);
