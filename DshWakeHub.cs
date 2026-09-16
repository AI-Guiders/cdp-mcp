#nullable enable
using System.Collections.Concurrent;
using System.Threading.Channels;
using Cdp.CdpState;

namespace CdpMcp;

/// <summary>
/// CDP-ADR-0227 — in-memory SSE hub for external wake subscribers (carrier=dsh).
///
/// Watchers keyed by (nick, bridgeSession): one plugin instance (one DSH process)
/// = one bridge session = one watcher per nick.
///
/// Delivery rule (CDP-ADR-0227, как согласовано с оператором 2026-09-16):
///   - envelope.Session empty  → line event (letter_mention, peer_ship, …) →
///     ALL watchers of the nick (line windows are the same line);
///   - envelope.Session set    → launcher-targeted event (build_finished from the
///     DSH plugin, …) → delivered to the matching bridge watcher AND, as a
///     broadcast copy, to every other watcher of the nick — so an independent
///     external subscriber (CdpToastService, nick=оператор) with its own bridge
///     session still receives the event. The launcher session is the producer's
///     addressing hint, not a delivery gate.
///
/// Process-local by design: SSE watch connections live in the tower process.
/// The dispatcher calls <see cref="TryDeliver"/>; the watch handler registers
/// and drains the pending backlog on connect.
/// </summary>
internal static class DshWakeHub
{
    sealed record Watcher(string Nick, string BridgeSession, ChannelWriter<CdpWakeEnvelopeEntity> Writer);

    static readonly ConcurrentDictionary<string, List<Watcher>> ByNick = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Register one SSE watch connection; the returned disposer unregisters it.</summary>
    public static IDisposable Register(string nick, string bridgeSession, ChannelWriter<CdpWakeEnvelopeEntity> writer)
    {
        var watcher = new Watcher(nick, bridgeSession, writer);
        var list = ByNick.GetOrAdd(nick, _ => new List<Watcher>());
        lock (list) list.Add(watcher);
        return new Registration(nick, watcher);
    }

    /// <summary>Try deliver one envelope to every matching watcher of the nick.</summary>
    /// <returns>true when at least one watcher accepted it; false = nobody watching (caller keeps pending).</returns>
    public static bool TryDeliver(CdpWakeEnvelopeEntity e)
    {
        if (string.IsNullOrWhiteSpace(e.Nick)) return false;
        if (!ByNick.TryGetValue(e.Nick, out var list)) return false;
        Watcher[] targets;
        lock (list) targets = list.ToArray();
        if (targets.Length == 0) return false;

        var delivered = false;
        foreach (var w in targets)
        {
            // CDP-ADR-0227 (2026-09-16): session is the producer's addressing
            // hint, not a gate — the matching bridge gets the targeted copy and
            // every other watcher of the nick gets a broadcast copy (external
            // subscribers like CdpToastService carry their own bridge session).
            if (w.Writer.TryWrite(e)) delivered = true;
        }
        return delivered;
    }

    sealed class Registration : IDisposable
    {
        readonly string _nick;
        readonly Watcher _watcher;
        int _done;

        public Registration(string nick, Watcher watcher)
        {
            _nick = nick;
            _watcher = watcher;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            if (ByNick.TryGetValue(_nick, out var list))
            {
                lock (list) list.Remove(_watcher);
                if (list.Count == 0) ByNick.TryRemove(_nick, out _);
            }
        }
    }
}
