namespace WebFilez.Services.Transfers;

/// <summary>The byte-moving half of the file manager, used by the HTTP endpoints.</summary>
public interface IFileTransferService
{
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

    Task<Result<UploadResultDto>> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default);
}
