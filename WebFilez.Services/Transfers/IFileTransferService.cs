namespace WebFilez.Services.Transfers;

/// <summary>The byte-moving half of the file manager, used by the HTTP endpoints.</summary>
public interface IFileTransferService
{
    /// <summary>
    /// Opens one file for download. A folder is invalid. The caller needs read and download permission.
    /// The caller owns the stream. A successful open is recorded as a download.
    /// </summary>
    /// <param name="rootId">The root.</param>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The stream and its headers, or invalid, not found, or forbidden.</returns>
    Task<Result<FileDownloadDto>> OpenReadAsync(string rootId, string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems a ticket from <see cref="IFileManagerService.CreateDownloadTicketAsync"/>. Returns
    /// the zip's file name so the caller can set headers before calling <see cref="WriteZipAsync"/>.
    /// </summary>
    Result<DownloadTicket> RedeemTicket(string token);

    /// <summary>
    /// Writes a zip of the ticket's items to <paramref name="destination"/>. Every item is
    /// permission-checked again under the current caller before the first byte is written.
    /// </summary>
    Task<Result> WriteZipAsync(DownloadTicket ticket, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an uploaded file. The name is cleaned, and a blocked or disallowed extension is invalid.
    /// A folder of the same name is a conflict. An existing file is a conflict unless overwrite is set, which also needs modify permission.
    /// Rename-on-conflict stores the file under a free name instead. A large image may be resized when that option is on.
    /// A relative directory creates the missing folders under the destination.
    /// </summary>
    /// <param name="request">The destination, the name, and the bytes.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The stored file, and whether it was resized.</returns>
    Task<Result<UploadResultDto>> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default);
}