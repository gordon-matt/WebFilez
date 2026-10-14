using System.IO.Compression;
using Microsoft.Extensions.Options;

namespace WebFilez.Services.Transfers;

public sealed class FileTransferService(
    ILogger<FileTransferService> logger,
    PathResolver resolver,
    FileNamePolicy fileNamePolicy,
    ImageResizer imageResizer,
    IFileActivitySink activitySink,
    DownloadTicketService downloadTickets,
    IOptionsMonitor<WebFilezOptions> options) : IFileTransferService
{
    public async Task<Result<FileDownloadDto>> OpenReadAsync(
        string rootId,
        string path,
        CancellationToken cancellationToken = default)
    {
        var resolved = await resolver.RequireAsync(rootId, path, FilePermission.Read | FilePermission.Download, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.To<FileDownloadDto>();
        }

        var target = resolved.Value;

        if (Directory.Exists(target.PhysicalPath))
        {
            return Result<FileDownloadDto>.Invalid(new ValidationError(
                nameof(path), "That is a folder. Download it as a zip instead."));
        }

        if (!File.Exists(target.PhysicalPath))
        {
            return Result<FileDownloadDto>.NotFound($"'{target.VirtualPath}' was not found.");
        }

        var info = new FileInfo(target.PhysicalPath);
        FileStream stream;

        try
        {
            stream = FileItems.OpenSequentialRead(target.PhysicalPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not open {Physical} for download", target.PhysicalPath);
            return Result<FileDownloadDto>.Error("That file could not be opened.");
        }

        await RecordAsync(
            new FileActivity(FileActivityKind.Download, rootId, target.VirtualPath, SizeBytes: info.Length),
            cancellationToken);

        return Result<FileDownloadDto>.Success(new FileDownloadDto(
            stream,
            info.Name,
            FileKinds.GetMimeType(info.Name),
            info.Length,
            info.LastWriteTimeUtc,
            FileItems.BuildETag(info)));
    }

    public Result<DownloadTicket> RedeemTicket(string token) =>
        downloadTickets.Redeem(token) is { } ticket
            ? Result<DownloadTicket>.Success(ticket)
            : Result<DownloadTicket>.NotFound("That download link has expired. Start the download again.");

    public async Task<Result> WriteZipAsync(
        DownloadTicket ticket,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        var targets = new List<ResolvedPath>(ticket.Paths.Count);

        // Everything is checked before a single byte is written, because once the response body
        // has started the download can no longer become an error page.
        foreach (string path in ticket.Paths)
        {
            var resolved = await resolver.RequireAsync(ticket.RootId, path, FilePermission.Read | FilePermission.Download, cancellationToken);
            if (!resolved.IsSuccess)
            {
                return resolved.ToVoid();
            }

            targets.Add(resolved.Value);
        }

        long totalBytes = 0;

        await using var output = new SyncWriteBufferStream(destination);

        await using (var archive = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken))
        {
            foreach (var target in targets)
            {
                if (Directory.Exists(target.PhysicalPath))
                {
                    string prefix = VirtualPath.GetName(target.VirtualPath) is { Length: > 0 } name ? name : target.Root.DisplayName;
                    int trim = target.VirtualPath == Constants.RootPath ? 1 : target.VirtualPath.Length + 1;

                    await foreach (var (file, fileVirtual) in FileItems.EnumerateFilesAsync(
                        resolver, target, FilePermission.Read | FilePermission.Download, cancellationToken))
                    {
                        totalBytes += await AddFileAsync(archive, file.FullName, $"{prefix}/{fileVirtual[trim..]}", cancellationToken);
                        await output.FlushAsync(cancellationToken);
                    }
                }
                else if (File.Exists(target.PhysicalPath))
                {
                    totalBytes += await AddFileAsync(archive, target.PhysicalPath, Path.GetFileName(target.PhysicalPath), cancellationToken);
                    await output.FlushAsync(cancellationToken);
                }
            }
        }

        await output.FlushAsync(cancellationToken);

        await RecordAsync(
            new FileActivity(FileActivityKind.Download, ticket.RootId, targets[0].VirtualPath, SizeBytes: totalBytes),
            cancellationToken);

        return Result.Success();
    }

    public async Task<Result<UploadResultDto>> UploadAsync(
        UploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = fileNamePolicy.Apply(request.FileName, isDirectory: false, nameof(request.FileName));
        if (!name.IsSuccess)
        {
            return name.To<UploadResultDto>();
        }

        var directory = await ResolveUploadDirectoryAsync(request, cancellationToken);
        if (!directory.IsSuccess)
        {
            return directory.To<UploadResultDto>();
        }

        var folder = directory.Value;
        string finalVirtual = VirtualPath.Combine(folder.VirtualPath, name.Value);

        if (!PathResolver.TryMapToDisk(folder.Root, finalVirtual, out string finalPath, out _))
        {
            return Result<UploadResultDto>.Invalid(new ValidationError(nameof(request.FileName), "That name can't be used here."));
        }

        if (Directory.Exists(finalPath))
        {
            return Result<UploadResultDto>.Conflict($"A folder named '{name.Value}' already exists here.");
        }

        if (File.Exists(finalPath))
        {
            if (!request.Overwrite)
            {
                return Result<UploadResultDto>.Conflict($"'{name.Value}' already exists here.");
            }

            var existing = await resolver.RequireAsync(folder.Root, finalVirtual, FilePermission.Modify, cancellationToken);
            if (!existing.IsSuccess)
            {
                return existing.To<UploadResultDto>();
            }
        }

        // Each upload gets its own temp name, so two people uploading the same file name at once
        // can't corrupt each other's transfer, and a half-received file is never listed.
        string temporary = Path.Combine(folder.PhysicalPath, $"{name.Value}.{Guid.NewGuid():N}{Constants.TempFileSuffix}");
        long limit = options.CurrentValue.MaxUploadBytes;
        bool resized;

        try
        {
            long received = await CopyWithLimitAsync(request.Content, temporary, limit, cancellationToken);
            if (received < 0)
            {
                TryDelete(temporary);
                return Result<UploadResultDto>.Invalid(new ValidationError(
                    nameof(request.Content),
                    $"'{name.Value}' is larger than the {FileKinds.FormatSize(limit)} upload limit."));
            }

            resized = await imageResizer.TryShrinkAsync(temporary, name.Value, cancellationToken);
            File.Move(temporary, finalPath, overwrite: request.Overwrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Upload of {Name} to {Physical} failed", name.Value, folder.PhysicalPath);
            TryDelete(temporary);

            return Result<UploadResultDto>.Error($"'{name.Value}' could not be uploaded.");
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        var info = new FileInfo(finalPath);
        await RecordAsync(new FileActivity(FileActivityKind.Upload, folder.Root.Id, finalVirtual, SizeBytes: info.Length), cancellationToken);

        var permissions = await resolver.GetPermissionsAsync(folder.Root, finalVirtual, cancellationToken);
        return Result<UploadResultDto>.Success(new UploadResultDto(FileItems.ToItem(info, folder.Root, finalVirtual, permissions), resized));
    }

    /// <summary>
    /// Resolves the folder an upload lands in, creating the subfolders of a dropped folder as
    /// needed. Create is checked at each level before a folder is made.
    /// </summary>
    private async Task<Result<ResolvedPath>> ResolveUploadDirectoryAsync(UploadRequest request, CancellationToken cancellationToken)
    {
        var current = await resolver.RequireAsync(request.RootId, request.Path, FilePermission.Create, cancellationToken);
        if (!current.IsSuccess)
        {
            return current;
        }

        if (!Directory.Exists(current.Value.PhysicalPath))
        {
            return Result<ResolvedPath>.NotFound($"'{current.Value.VirtualPath}' was not found.");
        }

        foreach (string segment in VirtualPath.GetSegments(VirtualPath.Normalize(request.RelativeDirectory)))
        {
            var folderName = fileNamePolicy.Apply(segment, isDirectory: true, nameof(request.RelativeDirectory));
            if (!folderName.IsSuccess)
            {
                return folderName.To<ResolvedPath>();
            }

            string childVirtual = VirtualPath.Combine(current.Value.VirtualPath, folderName.Value);
            if (!PathResolver.TryMapToDisk(current.Value.Root, childVirtual, out string childPhysical, out _))
            {
                return Result<ResolvedPath>.Invalid(new ValidationError(nameof(request.RelativeDirectory), $"'{folderName.Value}' can't be used as a folder name."));
            }

            if (File.Exists(childPhysical))
            {
                return Result<ResolvedPath>.Conflict($"A file named '{folderName.Value}' is in the way.");
            }

            Directory.CreateDirectory(childPhysical);

            current = await resolver.RequireAsync(current.Value.Root, childVirtual, FilePermission.Create, cancellationToken);
            if (!current.IsSuccess)
            {
                return current;
            }
        }

        return current;
    }

    /// <summary>Copies the request body to disk, returning -1 as soon as it passes the limit.</summary>
    private static async Task<long> CopyWithLimitAsync(Stream source, string path, long limit, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
        });

        byte[] buffer = new byte[81920];
        long total = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > limit)
            {
                return -1;
            }

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return total;
    }

    private async Task<long> AddFileAsync(ZipArchive archive, string physicalPath, string entryName, CancellationToken cancellationToken)
    {
        try
        {
            await using var source = FileItems.OpenSequentialRead(physicalPath);

            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            entry.LastWriteTime = File.GetLastWriteTime(physicalPath);

            await using var target = await entry.OpenAsync(cancellationToken);
            await source.CopyToAsync(target, cancellationToken);

            return source.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // One locked file shouldn't abort a whole folder download.
            logger.LogWarning(ex, "Skipped {Physical} while building a zip", physicalPath);
            return 0;
        }
    }

    private async Task RecordAsync(FileActivity activity, CancellationToken cancellationToken)
    {
        try
        {
            await activitySink.RecordAsync(activity, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Activity sink failed for {Kind} of {Path}", activity.Kind, activity.Path);
        }
    }

    private void TryDelete(string physicalPath)
    {
        try
        {
            if (File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not remove temporary file {Physical}", physicalPath);
        }
    }
}
