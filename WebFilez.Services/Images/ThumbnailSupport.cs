namespace WebFilez.Services.Images;

/// <summary>
/// Raster formats, plus any <see cref="IThumbnailSource"/> that is registered.
/// Listing and the thumbnail endpoint share this so a video is only marked when something can draw it.
/// </summary>
internal sealed class ThumbnailSupport(IEnumerable<IThumbnailSource> sources)
{
    public bool Supports(string fileName) =>
        FileKinds.SupportsThumbnail(fileName) || Find(fileName) is not null;

    public IThumbnailSource? Find(string fileName)
    {
        foreach (IThumbnailSource source in sources)
        {
            if (source.Supports(fileName))
            {
                return source;
            }
        }

        return null;
    }
}
