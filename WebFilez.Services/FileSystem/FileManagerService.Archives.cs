using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;

namespace WebFilez.Services.FileSystem;

public sealed partial class FileManagerService
{
    public async Task<Result<FileSystemItemDto>> CompressAsync(
        CompressRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Paths.Count == 0)
        {
            return Result<FileSystemItemDto>.Invalid(new ValidationError(nameof(request.Paths), "Select at least one item to compress."));
        }

        var (archiveType, compression, extension) = ResolveFormat(request.Format);

        var name = fileNamePolicy.Apply(BuildArchiveName(request, extension), isDirectory: false, nameof(request.ArchiveName));
        if (!name.IsSuccess)
        {
            return name.To<FileSystemItemDto>();
        }

        var destination = await resolver.RequireAsync(
            request.RootId,
            request.Path,
            FilePermission.Create | FilePermission.Compress,
            cancellationToken);

        if (!destination.IsSuccess)
        {
            return destination.To<FileSystemItemDto>();
        }

        var created = await ResolveNewChildAsync(destination.Value, name.Value, cancellationToken);
        if (!created.IsSuccess)
        {
            return created.To<FileSystemItemDto>();
        }

        var targets = new List<ResolvedPath>(request.Paths.Count);
        foreach (string path in request.Paths)
        {
            var resolved = await resolver.RequireAsync(request.RootId, path, FilePermission.Read, cancellationToken);
            if (!resolved.IsSuccess)
            {
                return resolved.To<FileSystemItemDto>();
            }

            targets.Add(resolved.Value);
        }

        string archivePhysical = created.Value.PhysicalPath;
        string temporary = archivePhysical + Constants.TempFileSuffix;

        try
        {
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = WriterFactory.OpenWriter(output, archiveType, new WriterOptions(compression) { LeaveStreamOpen = true }))
            {
                foreach (var target in targets)
                {
                    if (Directory.Exists(target.PhysicalPath))
                    {
                        string prefix = VirtualPath.GetName(target.VirtualPath);
                        if (prefix.Length == 0)
                        {
                            prefix = target.Root.DisplayName;
                        }

                        await foreach (var (file, fileVirtual) in FileItems.EnumerateFilesAsync(resolver, target, FilePermission.Read, cancellationToken))
                        {
                            string relative = fileVirtual[(target.VirtualPath == Constants.RootPath ? 1 : target.VirtualPath.Length + 1)..];

                            // Never archive the archive into itself.
                            if (!PhysicalPaths.AreEqual(file.FullName, temporary))
                            {
                                writer.Write($"{prefix}/{relative}", file.FullName);
                            }
                        }
                    }
                    else if (File.Exists(target.PhysicalPath))
                    {
                        writer.Write(Path.GetFileName(target.PhysicalPath), target.PhysicalPath);
                    }
                }
            }

            File.Move(temporary, archivePhysical, overwrite: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not create archive {Physical}", archivePhysical);
            TryDelete(temporary);

            return Result<FileSystemItemDto>.Error("The archive could not be created.");
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporary);
            throw;
        }

        var info = new FileInfo(archivePhysical);
        await RecordAsync(
            new FileActivity(FileActivityKind.Compress, request.RootId, created.Value.VirtualPath, SizeBytes: info.Length),
            cancellationToken);

        return Result<FileSystemItemDto>.Success(FileItems.ToItem(info, destination.Value.Root, created.Value.VirtualPath, created.Value.Permissions));
    }

    public async Task<Result<BulkOperationResultDto>> ExtractAsync(
        ExtractRequest request,
        CancellationToken cancellationToken = default)
    {
        var archive = await resolver.RequireAsync(request.RootId, request.Path, FilePermission.Read, cancellationToken);
        if (!archive.IsSuccess)
        {
            return archive.To<BulkOperationResultDto>();
        }

        if (!File.Exists(archive.Value.PhysicalPath))
        {
            return Result<BulkOperationResultDto>.NotFound($"'{archive.Value.VirtualPath}' was not found.");
        }

        string destinationVirtual = string.IsNullOrWhiteSpace(request.DestinationPath)
            ? VirtualPath.GetParent(archive.Value.VirtualPath) ?? Constants.RootPath
            : request.DestinationPath;

        var destination = await resolver.RequireAsync(
            request.RootId,
            destinationVirtual,
            request.Overwrite
                ? FilePermission.Create | FilePermission.Extract | FilePermission.Modify
                : FilePermission.Create | FilePermission.Extract,
            cancellationToken);

        if (!destination.IsSuccess)
        {
            return destination.To<BulkOperationResultDto>();
        }

        if (!Directory.Exists(destination.Value.PhysicalPath))
        {
            return Result<BulkOperationResultDto>.NotFound($"'{destination.Value.VirtualPath}' was not found.");
        }

        List<FileOperationResultDto> results;

        try
        {
            if (!request.Overwrite)
            {
                using var listing = ArchiveFactory.OpenArchive(archive.Value.PhysicalPath, ReaderOptions.ForFilePath);
                var conflicts = FindConflictingExtractNames(
                    destination.Value.PhysicalPath,
                    listing.Entries.Where(e => e.Key is not null).Select(e => (e.Key!, e.IsDirectory)));

                if (conflicts.Count > 0)
                {
                    return Result<BulkOperationResultDto>.Conflict(conflicts.Count == 1
                        ? $"'{conflicts[0]}' already exists here."
                        : $"{conflicts.Count} of the archive's items already exist here.");
                }
            }

            results = await ExtractEntriesAsync(archive.Value, destination.Value, request.Overwrite, cancellationToken);
        }
        catch (ExtractLimitException ex)
        {
            logger.LogWarning("Refused to extract {Physical}: {Reason}", archive.Value.PhysicalPath, ex.Message);
            return Result<BulkOperationResultDto>.Invalid(new ValidationError(nameof(request.Path), ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not extract {Physical}", archive.Value.PhysicalPath);
            return Result<BulkOperationResultDto>.Error(
                "That archive could not be extracted. It may be corrupt or use an unsupported format.");
        }

        await RecordAsync(
            new FileActivity(FileActivityKind.Extract, request.RootId, archive.Value.VirtualPath, destination.Value.VirtualPath, request.RootId),
            cancellationToken);

        return Result<BulkOperationResultDto>.Success(BulkOperationResultDto.From(results));
    }

    /// <summary>
    /// Extracts entries one at a time. Each destination is resolved inside the target folder,
    /// checked against the same name rules as an upload, and counted against the size and entry
    /// limits.
    /// </summary>
    private async Task<List<FileOperationResultDto>> ExtractEntriesAsync(
        ResolvedPath archive,
        ResolvedPath destination,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        var results = new List<FileOperationResultDto>();
        long writtenBytes = 0;
        int entryCount = 0;

        await using var input = FileItems.OpenSequentialRead(archive.PhysicalPath);
        using var reader = ReaderFactory.OpenReader(input, ReaderOptions.ForExternalStream);

        string destinationRoot = Path.GetFullPath(destination.PhysicalPath);

        while (reader.MoveToNextEntry())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = reader.Entry;
            if (entry.Key is null)
            {
                continue;
            }

            if (++entryCount > settings.MaxExtractedEntries)
            {
                throw new ExtractLimitException(
                    $"This archive contains more than {settings.MaxExtractedEntries:N0} entries, so extraction stopped.");
            }

            // Entry names are attacker-controlled. "../../etc/cron.d/x" would otherwise write
            // outside the destination (zip-slip), so resolve and verify containment first.
            string? entryDestination = ResolveEntryDestination(destinationRoot, entry.Key);
            if (entryDestination is null)
            {
                results.Add(new FileOperationResultDto(entry.Key, false, "Skipped: it points outside the destination folder."));
                continue;
            }

            string relative = Path.GetRelativePath(destinationRoot, entryDestination).Replace(Path.DirectorySeparatorChar, '/');
            string entryVirtual = VirtualPath.Combine(destination.VirtualPath, relative.Split('/'));

            if (!PathResolver.TryMapToDisk(destination.Root, entryVirtual, out _, out _))
            {
                results.Add(new FileOperationResultDto(entry.Key, false, "Skipped: hidden or excluded name."));
                continue;
            }

            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(entryDestination);
                continue;
            }

            if (!FileNamePolicy.IsExtensionAllowed(Path.GetFileName(entryDestination), settings))
            {
                results.Add(new FileOperationResultDto(entry.Key, false, "Skipped: that type of file is not allowed."));
                continue;
            }

            if (entry.Size > 0 && writtenBytes + entry.Size > settings.MaxExtractedBytes)
            {
                throw new ExtractLimitException(
                    $"This archive expands to more than {FileKinds.FormatSize(settings.MaxExtractedBytes)}, so extraction stopped.");
            }

            if (!overwrite && (File.Exists(entryDestination) || Directory.Exists(entryDestination)))
            {
                results.Add(new FileOperationResultDto(entry.Key, false, "Already exists."));
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(entryDestination)!);

                reader.WriteEntryToFile(entryDestination, new ExtractionOptions
                {
                    Overwrite = overwrite,

                    // Never honour an archive's stored attributes; a crafted archive could mark
                    // an extracted file executable or hidden.
                    PreserveFileTime = false,
                    PreserveAttributes = false,
                    ExtractFullPath = false,
                });

                // The declared size can lie; measure what actually landed on disk.
                writtenBytes += new FileInfo(entryDestination).Length;
                if (writtenBytes > settings.MaxExtractedBytes)
                {
                    throw new ExtractLimitException(
                        $"This archive expands to more than {FileKinds.FormatSize(settings.MaxExtractedBytes)}, so extraction stopped.");
                }

                results.Add(new FileOperationResultDto(entry.Key, true, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not extract entry '{Entry}'", entry.Key);
                results.Add(new FileOperationResultDto(entry.Key, false, "Could not be written."));
            }
        }

        return results;
    }

    /// <summary>
    /// Names of archive entries that would overwrite something already on disk, so the whole
    /// extract can be refused before anything is written.
    /// </summary>
    internal static IReadOnlyList<string> FindConflictingExtractNames(
        string destinationRoot,
        IEnumerable<(string Key, bool IsDirectory)> entries)
    {
        string normalizedRoot = Path.GetFullPath(destinationRoot);
        var names = new List<string>();
        var seen = new HashSet<string>(PhysicalPaths.Comparer);

        foreach (var (key, isDirectory) in entries)
        {
            string? destination = ResolveEntryDestination(normalizedRoot, key);
            if (destination is null)
            {
                continue;
            }

            bool conflict = isDirectory
                ? File.Exists(destination)
                : File.Exists(destination) || Directory.Exists(destination);

            if (conflict && seen.Add(destination))
            {
                names.Add(Path.GetFileName(Path.TrimEndingDirectorySeparator(destination)));
            }
        }

        return names;
    }

    /// <summary>
    /// Maps an archive entry name to a path inside <paramref name="destinationRoot"/>, or null
    /// when it can't be made safe. Rejects drive letters and any <c>..</c> segment outright,
    /// since an entry trying to climb out is a sign of a malicious archive.
    /// </summary>
    internal static string? ResolveEntryDestination(string destinationRoot, string entryKey)
    {
        string cleaned = entryKey.Replace('\\', '/').Trim().TrimStart('/');

        if (cleaned.Length == 0 || (cleaned.Length > 1 && cleaned[1] == ':'))
        {
            return null;
        }

        var segments = new List<string>();
        foreach (string segment in cleaned.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                return null;
            }

            string safe = VirtualPath.SanitizeSegment(segment);
            if (safe.Length == 0)
            {
                return null;
            }

            segments.Add(safe);
        }

        if (segments.Count == 0)
        {
            return null;
        }

        string candidate = Path.GetFullPath(Path.Combine([destinationRoot, .. segments]));
        return PhysicalPaths.IsWithin(candidate, destinationRoot) && !PhysicalPaths.AreEqual(candidate, destinationRoot)
            ? candidate
            : null;
    }

    private static (ArchiveType Type, CompressionType Compression, string Extension) ResolveFormat(ArchiveFormat format) =>
        format switch
        {
            ArchiveFormat.Tar => (ArchiveType.Tar, CompressionType.None, ".tar"),
            ArchiveFormat.TarGz => (ArchiveType.Tar, CompressionType.GZip, ".tar.gz"),
            _ => (ArchiveType.Zip, CompressionType.Deflate, ".zip"),
        };

    private static string BuildArchiveName(CompressRequest request, string extension)
    {
        string requested = VirtualPath.SanitizeSegment(request.ArchiveName);

        if (requested.Length == 0)
        {
            requested = request.Paths.Count == 1
                ? Path.GetFileNameWithoutExtension(VirtualPath.GetName(VirtualPath.Normalize(request.Paths[0])))
                : "archive";
        }

        if (requested.Length == 0)
        {
            requested = "archive";
        }

        return requested.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? requested : requested + extension;
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

    private sealed class ExtractLimitException(string message) : Exception(message);
}
