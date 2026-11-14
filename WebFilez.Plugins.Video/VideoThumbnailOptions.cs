namespace WebFilez.Plugins.Video;

/// <summary>
/// How the video plugin finds ffmpeg. Frame capture stays off when the binaries are missing.
/// </summary>
public sealed class VideoThumbnailOptions
{
    /// <summary>
    /// Where an official build is stored when <see cref="DownloadIfMissing"/> is set.
    /// Empty uses an <c>ffmpeg</c> folder under the content root.
    /// </summary>
    public string? DownloadDirectory { get; set; }

    /// <summary>
    /// Download the official Ffmpeg build when nothing local is complete. That build is GPL-licensed,
    /// so this stays off until the host opts in.
    /// </summary>
    public bool DownloadIfMissing { get; set; }

    /// <summary>A file path or a directory that already contains ffmpeg and ffprobe.</summary>
    public string? FfmpegPath { get; set; }

    /// <summary>On Linux, check <c>/usr/bin</c> before any other location.</summary>
    public bool PreferLinuxSystemDirectory { get; set; } = true;

    /// <summary>Also look next to the app and in the process working directory.</summary>
    public bool SearchApplicationDirectories { get; set; } = true;

    /// <summary>Also look through the process PATH.</summary>
    public bool SearchPath { get; set; } = true;

    /// <summary>Look in Program Files, the user profile, and the usual Linux and macOS bin directories.</summary>
    public bool SearchWellKnownLocations { get; set; } = true;
}