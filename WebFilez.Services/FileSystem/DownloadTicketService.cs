using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace WebFilez.Services.FileSystem;

/// <summary>
/// Short-lived, single-use tickets that hand a multi-item zip request to a plain browser
/// navigation.
/// </summary>
/// <remarks>
/// A zip of a large selection can't be a GET with the paths in the query string, because a few
/// hundred files blow past URL length limits. It also can't usefully be a <c>fetch</c> POST,
/// because that buffers the whole archive in browser memory. Registering the selection and
/// navigating to a ticket URL gets a native, streaming download. Tickets are unguessable, expire
/// quickly and are consumed on use, and every path is permission-checked again when the zip is
/// written, so a ticket is never a way to reach files by other means.
/// </remarks>
public sealed class DownloadTicketService(TimeProvider timeProvider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, DownloadTicket> tickets = new(StringComparer.Ordinal);

    public string Create(string rootId, IReadOnlyList<string> paths, string fileName)
    {
        PruneExpired();

        string token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        tickets[token] = new DownloadTicket(rootId, paths, fileName, timeProvider.GetUtcNow().Add(Lifetime));

        return token;
    }

    /// <summary>Redeems a ticket, or returns null when it is unknown, expired or already used.</summary>
    public DownloadTicket? Redeem(string token) => !tickets.TryRemove(token, out var ticket) ? null : ticket.ExpiresAt > timeProvider.GetUtcNow() ? ticket : null;

    private void PruneExpired()
    {
        var now = timeProvider.GetUtcNow();

        foreach (var (token, ticket) in tickets)
        {
            if (ticket.ExpiresAt <= now)
            {
                tickets.TryRemove(token, out _);
            }
        }
    }
}

public sealed record DownloadTicket(
    string RootId,
    IReadOnlyList<string> Paths,
    string FileName,
    DateTimeOffset ExpiresAt);