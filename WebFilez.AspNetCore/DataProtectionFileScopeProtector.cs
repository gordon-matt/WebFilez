using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace WebFilez;

/// <summary>
/// Scope tokens protected with the app's data-protection keys, so a browser can present one but
/// cannot widen it or invent one.
/// </summary>
internal sealed class DataProtectionFileScopeProtector(IDataProtectionProvider provider) : IFileScopeProtector
{
    private const char Separator = '\n';

    private readonly IDataProtector protector = provider.CreateProtector("WebFilez.FileScope.v1");

    public string Protect(string? rootId, string basePath)
    {
        string normalized = VirtualPath.Normalize(basePath);
        if (normalized == Constants.RootPath)
        {
            throw new ArgumentException("A scope token for the root itself does not confine anything.", nameof(basePath));
        }

        return protector.Protect((rootId ?? string.Empty) + Separator + normalized);
    }

    public bool TryUnprotect(string? token, out string? rootId, out string basePath)
    {
        rootId = null;
        basePath = Constants.RootPath;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string payload;
        try
        {
            payload = protector.Unprotect(token);
        }
        catch (CryptographicException)
        {
            return false;
        }

        int split = payload.IndexOf(Separator);
        if (split < 0)
        {
            return false;
        }

        rootId = payload[..split];
        if (rootId.Length == 0)
        {
            rootId = null;
        }

        basePath = VirtualPath.Normalize(payload[(split + 1)..]);
        return basePath != Constants.RootPath;
    }
}
