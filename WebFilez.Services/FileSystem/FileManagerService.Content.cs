using System.Text;

namespace WebFilez.Services.FileSystem;

public sealed partial class FileManagerService
{
    public async Task<Result<TextFileContentDto>> ReadTextAsync(
        string rootId,
        string path,
        CancellationToken cancellationToken = default)
    {
        var resolved = await resolver.RequireAsync(rootId, path, FilePermission.Read, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.To<TextFileContentDto>();
        }

        var target = resolved.Value;
        var info = await target.Storage.GetAsync(target.Location, cancellationToken);
        if (info is not { IsDirectory: false })
        {
            return Result<TextFileContentDto>.NotFound($"'{target.VirtualPath}' was not found.");
        }

        long limit = options.CurrentValue.MaxEditableFileBytes;

        if (info.SizeBytes > limit)
        {
            return Result<TextFileContentDto>.Invalid(new ValidationError(
                nameof(path),
                $"This file is {FileKinds.FormatSize(info.SizeBytes)}. The editor opens files up to {FileKinds.FormatSize(limit)}."));
        }

        string content;
        try
        {
            await using var stream = await target.Storage.OpenReadAsync(target.Location, cancellationToken);

            // An extension is a hint, not a guarantee. Sniffing for NUL bytes stops the editor
            // opening a binary as mojibake and then writing that back over the original.
            if (await FileItems.LooksBinaryAsync(stream, cancellationToken))
            {
                return Result<TextFileContentDto>.Invalid(new ValidationError(
                    nameof(path), "This file appears to be binary, so it can't be edited as text."));
            }

            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            content = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not read text from {Path}", target.VirtualPath);
            return Result<TextFileContentDto>.Error("That file could not be read.");
        }

        return Result<TextFileContentDto>.Success(new TextFileContentDto(
            target.VirtualPath,
            info.Name,
            content,
            info.SizeBytes,
            info.ModifiedAt,
            target.Permissions.Has(FilePermission.Modify)));
    }

    public async Task<Result<FileSystemItemDto>> SaveTextAsync(
        SaveTextFileRequest request,
        CancellationToken cancellationToken = default)
    {
        var resolved = await resolver.RequireAsync(request.RootId, request.Path, FilePermission.Modify, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return resolved.To<FileSystemItemDto>();
        }

        var target = resolved.Value;
        var before = await target.Storage.GetAsync(target.Location, cancellationToken);
        if (before is not { IsDirectory: false })
        {
            return Result<FileSystemItemDto>.NotFound($"'{target.VirtualPath}' was not found.");
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        long size = encoding.GetByteCount(request.Content);

        if (size > options.CurrentValue.MaxEditableFileBytes)
        {
            return Result<FileSystemItemDto>.Invalid(new ValidationError(
                nameof(request.Content), "That is too much text to save from the editor."));
        }

        if (request.ExpectedModifiedAt is { } expected
            && Math.Abs((before.ModifiedAt - expected.ToUniversalTime()).TotalMilliseconds) > 1)
        {
            return Result<FileSystemItemDto>.Conflict(
                "This file was changed by someone else after you opened it. Reload it before saving.");
        }

        try
        {
            await using var body = new MemoryStream(encoding.GetBytes(request.Content));
            await target.Storage.WriteAsync(target.Location, body, overwrite: true, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not save text to {Path}", target.VirtualPath);
            return Result<FileSystemItemDto>.Error("That file could not be saved.");
        }

        await RecordAsync(
            new FileActivity(FileActivityKind.Modify, request.RootId, target.VirtualPath, SizeBytes: size),
            cancellationToken);

        var saved = await target.Storage.GetAsync(target.Location, cancellationToken)
            ?? before with { SizeBytes = size, ModifiedAt = DateTime.UtcNow };
        return Result<FileSystemItemDto>.Success(FileItems.ToItem(
            saved,
            target.Root,
            target.VirtualPath,
            target.Permissions));
    }

    public async Task<Result<DownloadTicketDto>> CreateDownloadTicketAsync(
        DownloadTicketRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Paths.Count == 0)
        {
            return Result<DownloadTicketDto>.Invalid(new ValidationError(nameof(request.Paths), "Select at least one item."));
        }

        var paths = new List<string>(request.Paths.Count);
        ResolvedPath? first = null;

        foreach (string path in request.Paths)
        {
            var resolved = await resolver.RequireAsync(
                request.RootId,
                path,
                FilePermission.Read | FilePermission.Download,
                cancellationToken);

            if (!resolved.IsSuccess)
            {
                return resolved.To<DownloadTicketDto>();
            }

            first ??= resolved.Value;
            paths.Add(resolved.Value.VirtualPath);
        }

        string stem = paths.Count == 1
            ? Path.GetFileNameWithoutExtension(VirtualPath.GetName(paths[0]))
            : VirtualPath.GetName(VirtualPath.GetParent(paths[0]) ?? Constants.RootPath);

        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = first!.Root.DisplayName;
        }

        string fileName = $"{VirtualPath.SanitizeSegment(stem)}.zip";
        string token = downloadTickets.Create(first!.Root.Id, paths, fileName);

        return Result<DownloadTicketDto>.Success(new DownloadTicketDto(token, fileName));
    }
}