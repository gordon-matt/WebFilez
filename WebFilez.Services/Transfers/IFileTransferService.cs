namespace WebFilez.Services.Transfers;

/// <summary>The byte-moving half of the file manager, used by the HTTP endpoints.</summary>
public interface IFileTransferService
{
    /// <summary>
    /// Opens the read.
    /// </summary>
    /// <param name="rootId">The root identifier.</param>
    /// <param name="path">The path.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is the outcome for the file download.</returns>
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
    /// Uploads the value.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is the outcome for the upload result.</returns>
    Task<Result<UploadResultDto>> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default);
}