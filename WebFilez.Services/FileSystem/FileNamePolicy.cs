using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

namespace WebFilez.Services.FileSystem;

/// <summary>
/// Turns a user-supplied name into the name that goes on disk, and enforces the extension
/// allow and block lists. Applied to uploads, new files and folders, and renames.
/// </summary>
public sealed class FileNamePolicy(IOptionsMonitor<WebFilezOptions> options)
{
    /// <summary>
    /// Cleans a name and checks its extension. A blank name, a reserved temporary suffix, and a blocked extension are invalid.
    /// Every dotted suffix is checked, so a name such as <c>shell.php.jpg</c> is refused when <c>.php</c> is blocked.
    /// When an allow list is set, the final extension must be on it.
    /// </summary>
    /// <param name="requestedName">The name the caller typed.</param>
    /// <param name="isDirectory">Whether this is a folder. Folders are not checked against the extension lists.</param>
    /// <param name="fieldName">The validation field the error is reported against.</param>
    /// <returns>The cleaned name, or an invalid result.</returns>
    public Result<string> Apply(string? requestedName, bool isDirectory, string fieldName = "Name")
    {
        var settings = options.CurrentValue;
        string name = Clean(requestedName, settings.FileNames);

        return name.Length == 0
            ? Result<string>.Invalid(new ValidationError(
                fieldName,
                isDirectory ? "Enter a folder name." : "Enter a file name."))
            : name.EndsWith(Constants.TempFileSuffix, StringComparison.OrdinalIgnoreCase)
            ? Result<string>.Invalid(new ValidationError(fieldName, "That name is reserved."))
            : isDirectory
            ? Result<string>.Success(name)
            : IsExtensionAllowed(name, settings)
            ? Result<string>.Success(name)
            : Result<string>.Invalid(new ValidationError(
                fieldName,
                $"Files of type '{DisplayExtension(name)}' are not allowed here."));
    }

    internal static string Clean(string? requestedName, FileNameOptions settings)
    {
        string name = VirtualPath.SanitizeSegment(requestedName);
        if (name.Length == 0)
        {
            return name;
        }

        if (settings.RemoveDiacritics)
        {
            name = RemoveDiacritics(name);
        }

        if (settings.ReplaceSpaces)
        {
            name = name.Replace(" ", VirtualPath.SanitizeSegment(settings.SpaceReplacement) is { Length: > 0 } r ? r : "_");
        }

        if (settings.LowerCase)
        {
            name = name.ToLowerInvariant();
        }

        return VirtualPath.SanitizeSegment(name);
    }

    internal static bool IsExtensionAllowed(string name, WebFilezOptions settings)
    {
        // Every dotted suffix is checked, not just the last, so "shell.php.jpg" is caught on a
        // server configured to run anything containing ".php", and ".htaccess" counts as an
        // extension even though it is the whole name.
        var suffixes = GetSuffixes(name);

        if (settings.AllowedExtensions.Count > 0)
        {
            string last = Path.GetExtension(name);
            return last.Length > 0
                && settings.AllowedExtensions.Contains(last, StringComparer.OrdinalIgnoreCase)
                && !suffixes.Any(s => settings.BlockedExtensions.Contains(s, StringComparer.OrdinalIgnoreCase)
                    && !settings.AllowedExtensions.Contains(s, StringComparer.OrdinalIgnoreCase));
        }

        return !suffixes.Any(s => settings.BlockedExtensions.Contains(s, StringComparer.OrdinalIgnoreCase));
    }

    private static List<string> GetSuffixes(string name)
    {
        var suffixes = new List<string>();
        for (int i = name.IndexOf('.'); i >= 0; i = name.IndexOf('.', i + 1))
        {
            int next = name.IndexOf('.', i + 1);
            suffixes.Add(next < 0 ? name[i..] : name[i..next]);
        }

        return suffixes;
    }

    private static string DisplayExtension(string name)
    {
        string extension = Path.GetExtension(name);
        return extension.Length > 0 ? extension : name;
    }

    private static string RemoveDiacritics(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}