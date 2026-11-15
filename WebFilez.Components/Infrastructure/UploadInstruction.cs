namespace WebFilez.Components.Infrastructure;

/// <summary>What to do with one dropped or picked file. <see cref="NewPath"/> renames a folder drop.</summary>
public sealed record UploadInstruction(string Path, string Action, string? NewPath);
