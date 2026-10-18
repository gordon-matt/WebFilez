using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebFilez.Components.Http;

/// <summary>Source-generated, so the WebAssembly build can trim without breaking serialization.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(IReadOnlyList<FileRootDto>))]
[JsonSerializable(typeof(DirectoryListingDto))]
[JsonSerializable(typeof(SearchResultDto))]
[JsonSerializable(typeof(SearchHitDto))]
[JsonSerializable(typeof(IReadOnlyList<FolderNodeDto>))]
[JsonSerializable(typeof(FileDetailDto))]
[JsonSerializable(typeof(DirectorySizeDto))]
[JsonSerializable(typeof(FileSystemItemDto))]
[JsonSerializable(typeof(CreateDirectoryRequest))]
[JsonSerializable(typeof(CreateFileRequest))]
[JsonSerializable(typeof(RenameRequest))]
[JsonSerializable(typeof(DeleteRequest))]
[JsonSerializable(typeof(TransferRequest))]
[JsonSerializable(typeof(BulkOperationResultDto))]
[JsonSerializable(typeof(TextFileContentDto))]
[JsonSerializable(typeof(SaveTextFileRequest))]
[JsonSerializable(typeof(CompressRequest))]
[JsonSerializable(typeof(ExtractRequest))]
[JsonSerializable(typeof(DownloadTicketRequest))]
[JsonSerializable(typeof(DownloadTicketDto))]
[JsonSerializable(typeof(ProblemResponse))]
internal sealed partial class WebFilezJsonContext : JsonSerializerContext;

/// <summary>The parts of an RFC 9457 problem response the client reads.</summary>
internal sealed record ProblemResponse(string? Title, string? Detail, int? Status, Dictionary<string, string[]>? Errors);