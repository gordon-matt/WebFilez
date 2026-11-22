namespace WebFilez.Services.Transfers;

/// <summary>One file to store, read from a stream the caller owns.</summary>
/// <param name="RootId">The root.</param>
/// <param name="Path">The folder. <see langword="null"/> is the root.</param>
/// <param name="FileName">The file name. It is cleaned before it is stored.</param>
/// <param name="Content">The bytes. The caller disposes the stream.</param>
/// <param name="RelativeDirectory">
/// Subfolder path under <paramref name="Path"/> for a file dropped as part of a folder, such as
/// <c>holiday/day1</c>. Missing folders are created.
/// </param>
/// <param name="Overwrite">Whether an existing file is replaced. Replacing also requires modify permission.</param>
/// <param name="RenameOnConflict">Whether a free name is chosen when the name exists and <paramref name="Overwrite"/> is false.</param>
public sealed record UploadRequest(
    string RootId,
    string? Path,
    string FileName,
    Stream Content,
    string? RelativeDirectory = null,
    bool Overwrite = false,
    bool RenameOnConflict = false);

/// <summary>An open file ready to stream. The caller owns and disposes <see cref="Content"/>.</summary>
public sealed record FileDownloadDto(
    Stream Content,
    string FileName,
    string MimeType,
    long Length,
    DateTime ModifiedAt,
    string ETag);

/// <summary>A generated thumbnail. The bytes are WebP.</summary>
/// <param name="Bytes">The image.</param>
/// <param name="MimeType">The media type, <c>image/webp</c>.</param>
/// <param name="ETag">A validator that changes when the source file or the size changes.</param>
public sealed record ThumbnailDto(byte[] Bytes, string MimeType, string ETag);