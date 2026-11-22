namespace WebFilez.Enums;

/// <summary>The archive written by compress.</summary>
public enum ArchiveFormat
{
    /// <summary>A zip file.</summary>
    Zip = 0,

    /// <summary>An uncompressed tar.</summary>
    Tar = 1,

    /// <summary>A gzipped tar.</summary>
    TarGz = 2,
}