using WebFilez.Services.FileSystem;

namespace WebFilez.Services.Images;

/// <summary>
/// Produces a still for a file Skia cannot decode, such as a video.
/// </summary>
/// <remarks>
/// Register an implementation with <c>TryAddEnumerable</c>. None are registered by
/// <c>AddWebFilez</c>, so raster thumbnails stay the default and a plugin can add more.
/// </remarks>
public interface IThumbnailSource
{
    /// <summary>Whether this source can thumbnail <paramref name="fileName"/> right now.</summary>
    bool Supports(string fileName);

    /// <summary>
    /// Image bytes Skia can decode, or null when no frame can be made.
    /// A missing tool or an unreadable file returns null. It does not throw.
    /// </summary>
    Task<byte[]?> TryReadImageAsync(ResolvedPath target, CancellationToken cancellationToken = default);
}
