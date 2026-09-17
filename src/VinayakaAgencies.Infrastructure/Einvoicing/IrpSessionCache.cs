using System.Collections.Concurrent;
using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.Infrastructure.Einvoicing;

/// <summary>
/// Holds live NIC auth tokens across calls, keyed by environment and GSTIN
/// (phase-6-einvoice-eway.md §2, "token lifetime handling and refresh").
/// </summary>
/// <remarks>
/// <para>
/// A singleton, separate from the client, for a mechanical reason: <c>AddHttpClient</c> registers a
/// typed client as transient, so a cache held in a field on <see cref="NicIrpClient"/> would be
/// discarded after every call and the portal would be re-authenticated each time. NIC rate-limits
/// authentication, so that is not merely wasteful — it is a way to get locked out.
/// </para>
/// <para>
/// Concurrent because Phase 9 makes this multi-user and a token race there would be silent. The
/// tokens held here are credentials for their lifetime, which is why nothing in this type is logged
/// or exposed.
/// </para>
/// </remarks>
internal sealed class IrpSessionCache
{
    private readonly ConcurrentDictionary<string, IrpSession> _sessions = new(StringComparer.Ordinal);

    /// <summary>The live session for this environment and GSTIN, or null when one must be fetched.</summary>
    public IrpSession? Get(IrpEnvironment environment, string gstin, DateTimeOffset nowUtc) =>
        _sessions.TryGetValue(KeyFor(environment, gstin), out var session) && session.IsLiveAt(nowUtc)
            ? session
            : null;

    public void Set(IrpEnvironment environment, string gstin, IrpSession session) =>
        _sessions[KeyFor(environment, gstin)] = session;

    /// <summary>Drops a session — used when the portal rejects a token we believed was live.</summary>
    public void Invalidate(IrpEnvironment environment, string gstin) =>
        _sessions.TryRemove(KeyFor(environment, gstin), out _);

    private static string KeyFor(IrpEnvironment environment, string gstin) =>
        $"{environment}|{gstin.ToUpperInvariant()}";
}

/// <summary>One authenticated session with the portal: the token, the session key, and the expiry.</summary>
internal sealed record IrpSession(string AuthToken, byte[] SessionKey, DateTimeOffset ExpiresAtUtc)
{
    public bool IsLiveAt(DateTimeOffset nowUtc) => nowUtc < ExpiresAtUtc;
}
