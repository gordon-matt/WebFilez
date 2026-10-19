using System.IO.Compression;
using Microsoft.Extensions.Options;
using WebFilez.Services.Search;
using WebFilez.Services.Storage;

namespace WebFilez.Services.Transfers;

public sealed class FileTransferService(
    ILogger<FileTransferService> logger,
    PathResolver resolver,
    FileNamePolicy fileNamePolicy,
    ImageResizer imageResizer,
    IFileActivitySink activitySink,
    IFileIndex index,
    DownloadTicketService downloadTickets,
    IFileUrlResolver urlResolver,
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
        var info = await target.Storage.GetAsync(target.Location, cancellationToken);

        if (info is { IsDirectory: true })
        {
            return Result<FileDownloadDto>.Invalid(new ValidationError(
                nameof(path), "That is a folder. Download it as a zip instead."));
        }

        if (info is null)
        {
            return Result<FileDownloadDto>.NotFound($"'{target.VirtualPath}' was not found.");
        }

        Stream stream;
        try
        {
            stream = await target.Storage.OpenReadAsync(target.Location, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not open {Path} for download", target.VirtualPath);
            return Result<FileDownloadDto>.Error("That file could not be opened.");
        }

        await RecordAsync(
            new FileActivity(FileActivityKind.Download, rootId, target.VirtualPath, SizeBytes: info.SizeBytes),
            cancellationToken);

        return Result<FileDownloadDto>.Success(new FileDownloadDto(
            stream,
            info.Name,
            FileKinds.GetMimeType(info.Name),
            info.SizeBytes,
            info.ModifiedAt,
            FileItems.BuildETag(info.SizeBytes, info.ModifiedAt)));
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
                var info = await target.Storage.GetAsync(target.Location, cancellationToken);
                if (info is { IsDirectory: true })
                {
                    string prefix = VirtualPath.GetName(target.VirtualPath) is { Length: > 0 } name ? name : target.Root.DisplayName;
                    int trim = target.VirtualPath == Constants.RootPath ? 1 : target.VirtualPath.Length + 1;

                    await foreach (var (file, fileVirtual) in FileItems.EnumerateFilesAsync(
                        resolver, target, FilePermission.Read | FilePermission.Download, cancellationToken))
                    {
                        if (!target.Storage.TryMap(target.Root, fileVirtual, out var fileLocation))
                        {
                            continue;
                        }

                        totalBytes += await AddFileAsync(archive, target.Storage, fileLocation, file, $"{prefix}/{fileVirtual[trim..]}", cancellationToken);
                        await output.FlushAsync(cancellationToken);
                    }
                }
                else if (info is not null)
                {
                    totalBytes += await AddFileAsync(archive, target.Storage, target.Location, info, info.Name, cancellationToken);
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

        if (!folder.Storage.TryMap(folder.Root, finalVirtual, out var finalLocation))
        {
            return Result<UploadResultDto>.Invalid(new ValidationError(nameof(request.FileName), "That name can't be used here."));
        }

        var occupying = await folder.Storage.GetAsync(finalLocation, cancellationToken);
        if (occupying is { IsDirectory: true })
        {
            return Result<UploadResultDto>.Conflict($"A folder named '{name.Value}' already exists here.");
        }

        if (occupying is not null)
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

        // Spool outside the root, so a half-received upload is never listed and the same path
        // works for a disk folder and a blob container.
        string temporary = Path.Combine(Path.GetTempPath(), $"webfilez-{Guid.NewGuid():N}{Constants.TempFileSuffix}");
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
            await folder.Storage.CommitLocalFileAsync(finalLocation, temporary, request.Overwrite, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Upload of {Name} to {Path} failed", name.Value, folder.VirtualPath);
            TryDelete(temporary);

            return Result<UploadResultDto>.Error($"'{name.Value}' could not be uploaded.");
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        var info = await folder.Storage.GetAsync(finalLocation, cancellationToken)
            ?? new StorageEntry(name.Value, false, 0, DateTime.UtcNow, DateTime.UtcNow, false, false);
        await RecordAsync(new FileActivity(FileActivityKind.Upload, folder.Root.Id, finalVirtual, SizeBytes: info.SizeBytes), cancellationToken);

        var permissions = await resolver.GetPermissionsAsync(folder.Root, finalVirtual, cancellationToken);
        return Result<UploadResultDto>.Success(new UploadResultDto(
            await FileItems.ToItemAsync(
                info,
                folder.Root,
                finalVirtual,
                permissions,
                urlResolver,
                options.CurrentValue.AllowExpiringUrls,
                cancellationToken),
            resized));
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

        if (await current.Value.Storage.GetAsync(current.Value.Location, cancellationToken) is not { IsDirectory: true })
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
            if (!current.Value.Storage.TryMap(current.Value.Root, childVirtual, out var childLocation))
            {
                return Result<ResolvedPath>.Invalid(new ValidationError(nameof(request.RelativeDirectory), $"'{folderName.Value}' can't be used as a folder name."));
            }

            if (await current.Value.Storage.GetAsync(childLocation, cancellationToken) is { IsDirectory: false })
            {
                return Result<ResolvedPath>.Conflict($"A file named '{folderName.Value}' is in the way.");
            }

            await current.Value.Storage.CreateDirectoryAsync(childLocation, cancellationToken);

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

    private async Task<long> AddFileAsync(
        ZipArchive archive,
        IStorageProvider storage,
        StorageLocation location,
        StorageEntry file,
        string entryName,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var source = await storage.OpenReadAsync(location, cancellationToken);

            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            entry.LastWriteTime = new DateTimeOffset(DateTime.SpecifyKind(file.ModifiedAt, DateTimeKind.Utc));

            await using var target = await entry.OpenAsync(cancellationToken);
            await source.CopyToAsync(target, cancellationToken);

            return file.SizeBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // One locked file shouldn't abort a whole folder download.
            logger.LogWarning(ex, "Skipped {Path} while building a zip", location.VirtualPath);
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

        try
        {
            index.Apply(activity);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Search index failed for {Kind} of {Path}", activity.Kind, activity.Path);
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