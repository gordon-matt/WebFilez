using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using WebFilez.AspNetCore;
using WebFilez.Services.Images;
using WebFilez.Services.Transfers;

namespace WebFilez;

public static class EndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the WebFilez HTTP API. Every hosting model needs it, because downloads, uploads
        /// and thumbnails always travel over HTTP. Returns the group so it can be secured:
        /// <code>
        /// app.MapWebFilezApi().RequireAuthorization();
        /// </code>
        /// </summary>
        /// <param name="prefix">Defaults to <see cref="WebFilezOptions.ApiPrefix"/>.</param>
        public RouteGroupBuilder MapWebFilezApi(string? prefix = null)
        {
            prefix ??= endpoints.ServiceProvider.GetRequiredService<IOptions<WebFilezOptions>>().Value.ApiPrefix;

            var group = endpoints.MapGroup("/" + prefix.Trim('/')).WithTags("WebFilez");

            MapBrowsing(group);
            MapMutations(group);
            MapContent(group);
            MapTransfers(group);

            return group;
        }
    }

    private static void MapBrowsing(RouteGroupBuilder group)
    {
        group.MapGet(ApiRoutes.Roots, async (IFileManagerService service, CancellationToken ct) =>
            (await service.GetRootsAsync(ct)).ToHttpResult());

        group.MapGet(ApiRoutes.List, async (
            string root,
            string? path,
            string? filter,
            KindFilter? kind,
            ListingSortField? sort,
            bool? desc,
            bool? hidden,
            int? page,
            int? size,
            IFileManagerService service,
            CancellationToken ct) =>
        {
            var request = new ListingRequest(
                root,
                path,
                filter,
                kind ?? KindFilter.All,
                sort ?? ListingSortField.Name,
                desc ?? false,
                hidden ?? false,
                page ?? 1,
                size ?? 1000);

            return (await service.ListAsync(request, ct)).ToHttpResult();
        });

        group.MapGet(ApiRoutes.Folders, async (string root, string? path, IFileManagerService service, CancellationToken ct) =>
            (await service.GetFoldersAsync(root, path, ct)).ToHttpResult());

        group.MapGet(ApiRoutes.Detail, async (string root, string path, IFileManagerService service, CancellationToken ct) =>
            (await service.GetDetailAsync(root, path, ct)).ToHttpResult());

        group.MapGet(ApiRoutes.Size, async (string root, string path, IFileManagerService service, CancellationToken ct) =>
            (await service.GetDirectorySizeAsync(root, path, ct)).ToHttpResult());
    }

    private static void MapMutations(RouteGroupBuilder group)
    {
        group.MapPost(ApiRoutes.Directories, async (CreateDirectoryRequest request, IFileManagerService service, CancellationToken ct) =>
            (await service.CreateDirectoryAsync(request, ct)).ToHttpResult());

        group.MapPost(ApiRoutes.Files, async (CreateFileRequest request, IFileManagerService service, CancellationToken ct) =>
            (await service.CreateFileAsync(request, ct)).ToHttpResult());

        group.MapPost(ApiRoutes.Rename, async (RenameRequest request, IFileManagerService service, CancellationToken ct) =>
            (await service.RenameAsync(request, ct)).ToHttpResult());

        group.MapPost(ApiRoutes.Delete, async (DeleteRequest request, IFileManagerService service, CancellationToken ct) =>
            (await service.DeleteAsync(request, ct)).ToHttpResult());

        group.MapPost(ApiRoutes.Transfer, async (TransferRequest request, IFileManagerService service, CancellationToken ct) =>
            (await service.TransferAsync(request, ct)).ToHttpResult());

        group.MapPost(ApiRoutes.Compress, async (CompressRequest request, IFileManagerService service, CancellationToken ct) =>
            (await service.CompressAsync(request, ct)).ToHttpResult());

        group.MapPost(ApiRoutes.Extract, async (ExtractRequest request, IFileManagerService service, CancellationToken ct) =>
            (await service.ExtractAsync(request, ct)).ToHttpResult());
    }

    private static void MapContent(RouteGroupBuilder group)
    {
        group.MapGet(ApiRoutes.Text, async (string root, string path, IFileManagerService service, CancellationToken ct) =>
            (await service.ReadTextAsync(root, path, ct)).ToHttpResult());

        group.MapPut(ApiRoutes.Text, async (SaveTextFileRequest request, IFileManagerService service, CancellationToken ct) =>
            (await service.SaveTextAsync(request, ct)).ToHttpResult());

        group.MapPost(ApiRoutes.Tickets, async (DownloadTicketRequest request, IFileManagerService service, CancellationToken ct) =>
            (await service.CreateDownloadTicketAsync(request, ct)).ToHttpResult());
    }

    private static void MapTransfers(RouteGroupBuilder group)
    {
        group.MapGet(ApiRoutes.Download, async (
            string root,
            string path,
            bool? inline,
            HttpContext context,
            IFileTransferService service,
            CancellationToken ct) =>
        {
            var result = await service.OpenReadAsync(root, path, ct);
            if (!result.IsSuccess)
            {
                return HttpResults.Failure(result);
            }

            var file = result.Value;
            context.Response.Headers.XContentTypeOptions = "nosniff";

            // Only types the browser decodes rather than executes may render inline on this
            // origin; anything else, notably HTML and SVG, is always an attachment.
            if (inline == true && FileKinds.IsInlineSafe(file.MimeType))
            {
                var disposition = new ContentDispositionHeaderValue("inline");
                disposition.SetHttpFileName(file.FileName);
                context.Response.Headers.ContentDisposition = disposition.ToString();

                string contentType = file.MimeType == "text/plain" ? "text/plain; charset=utf-8" : file.MimeType;
                return Results.File(file.Content, contentType, lastModified: file.ModifiedAt, entityTag: Weak(file.ETag), enableRangeProcessing: true);
            }

            return Results.File(
                file.Content,
                FileKinds.IsInlineSafe(file.MimeType) ? file.MimeType : FileKinds.DefaultMimeType,
                file.FileName,
                file.ModifiedAt,
                Weak(file.ETag),
                enableRangeProcessing: true);
        });

        group.MapGet(ApiRoutes.Thumbnail, async (
            string root,
            string path,
            int? size,
            HttpContext context,
            IThumbnailService service,
            CancellationToken ct) =>
        {
            var result = await service.GetAsync(root, path, size ?? 256, ct);
            if (!result.IsSuccess)
            {
                return HttpResults.Failure(result);
            }

            // The UI versions thumbnail URLs with the file's timestamp, so a cached copy can
            // be trusted for a week; private, because it is per-user content.
            context.Response.Headers.CacheControl = "private, max-age=604800";
            context.Response.Headers.XContentTypeOptions = "nosniff";

            return Results.File(result.Value.Bytes, result.Value.MimeType, entityTag: Weak(result.Value.ETag));
        });

        group.MapGet($"{ApiRoutes.Zip}/{{token}}", async (string token, HttpContext context, IFileTransferService service, CancellationToken ct) =>
        {
            var ticket = service.RedeemTicket(token);
            if (!ticket.IsSuccess)
            {
                return HttpResults.Failure(ticket);
            }

            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(ticket.Value.FileName);

            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            context.Response.ContentType = "application/zip";
            context.Response.Headers.ContentDisposition = disposition.ToString();
            context.Response.Headers.XContentTypeOptions = "nosniff";

            var written = await service.WriteZipAsync(ticket.Value, context.Response.Body, ct);
            if (!written.IsSuccess && !context.Response.HasStarted)
            {
                context.Response.Headers.Remove(HeaderNames.ContentDisposition);
                context.Response.ContentType = null;
                return HttpResults.Failure(written);
            }

            return Results.Empty;
        });

        group.MapPost(ApiRoutes.Upload, UploadAsync).DisableAntiforgery();
    }

    /// <summary>
    /// Streams a multipart upload straight to disk. The form is read by hand rather than bound,
    /// so a large file never sits in memory and the size limit is enforced as bytes arrive.
    /// Expects the fields <c>root</c>, <c>path</c>, optional <c>relativePath</c> and
    /// <c>overwrite</c>, then a single file part.
    /// </summary>
    private static async Task<IResult> UploadAsync(
        HttpContext context,
        IFileTransferService service,
        IOptionsMonitor<WebFilezOptions> options,
        CancellationToken ct)
    {
        // A cross-site form can post multipart data but can't add a custom header, so this
        // stops another site from uploading into a signed-in user's folders.
        if (context.Request.Headers[Constants.RequestHeaderName] != Constants.RequestHeaderValue)
        {
            return Results.Problem(detail: "Missing the WebFilez request header.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType)
            || !contentType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(contentType.Boundary).Value is not { Length: > 0 } boundary)
        {
            return Results.Problem(detail: "Expected a multipart/form-data upload.", statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
        {
            // Headroom over the file limit for the other form fields and multipart framing.
            sizeFeature.MaxRequestBodySize = options.CurrentValue.MaxUploadBytes + (1024 * 1024);
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var reader = new MultipartReader(boundary, context.Request.Body);

        try
        {
            while (await reader.ReadNextSectionAsync(ct) is { } section)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
                {
                    continue;
                }

                string name = HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? string.Empty;

                if (!disposition.IsFileDisposition())
                {
                    using var fieldReader = new StreamReader(section.Body, Encoding.UTF8);
                    char[] buffer = new char[4096];
                    int read = await fieldReader.ReadBlockAsync(buffer, ct);
                    fields[name] = new string(buffer, 0, read);
                    continue;
                }

                if (!fields.TryGetValue("root", out string? root))
                {
                    return Results.Problem(detail: "The root field must come before the file.", statusCode: StatusCodes.Status400BadRequest);
                }

                string fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar).Value
                    ?? HeaderUtilities.RemoveQuotes(disposition.FileName).Value
                    ?? string.Empty;

                var request = new UploadRequest(
                    root,
                    fields.GetValueOrDefault("path"),
                    Path.GetFileName(fileName.Replace('\\', '/')),
                    section.Body,
                    RelativeDirectory(fields.GetValueOrDefault("relativePath")),
                    bool.TryParse(fields.GetValueOrDefault("overwrite"), out bool overwrite) && overwrite);

                return (await service.UploadAsync(request, ct)).ToHttpResult();
            }
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Results.Problem(detail: "That file is larger than the upload limit.", statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        return Results.Problem(detail: "The upload contained no file.", statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// The folder part of a dropped file's relative path: <c>holiday/day1/a.jpg</c> becomes
    /// <c>holiday/day1</c>.
    /// </summary>
    private static string? RelativeDirectory(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        string normalized = relativePath.Replace('\\', '/');
        int slash = normalized.LastIndexOf('/');

        return slash > 0 ? normalized[..slash] : null;
    }

    private static EntityTagHeaderValue Weak(string tag) => new($"\"{tag}\"", isWeak: true);
}
