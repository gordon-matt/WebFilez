namespace WebFilez.Services.Storage;

/// <summary>
/// The plain object URL for one storage provider. A provider package registers this alongside
/// <see cref="IStorageProvider"/>. <see cref="DefaultFileUrlResolver"/> calls it when the root has
/// no configured public base. The core library does not know which providers exist.
/// </summary>
/// <remarks>
/// Return a stable address. A presigned, SAS, or other expiring URL is allowed only when
/// <see cref="FileUrlContext.AllowExpiringUrls"/> is true. The first registered contributor whose
/// <see cref="ProviderKey"/> matches the root is the one that runs.
/// </remarks>
public interface IStoragePublicUrl
{
    /// <summary>Matches <see cref="IStorageProvider.Key"/>.</summary>
    string ProviderKey { get; }

    /// <summary>
    /// The URL a host may store for this file or folder, or <c>null</c> when the root has no
    /// plain public address. <see cref="FileUrl"/> applies the same encoding as a configured base.
    /// </summary>
    string? GetUrl(FileUrlContext context);
}
