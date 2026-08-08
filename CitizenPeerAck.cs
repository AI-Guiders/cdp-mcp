#nullable enable
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Peer duplex after host execute (ADR-0028 / design <c>intent_ack</c>).
/// Surfaces applied|dropped acks and latches peer= for the next afferent inject.
/// Durable latch survives habitat remount so Glass Radio observe is not wiped mid-dialog.
/// Latch: %LocalAppData%/cdp-mcp/citizen-peer-LATEST.json
/// </summary>
internal static class CitizenPeerAck
{
    public const string Schema = "citizen_peer_ack_latch/v0";
    public const int LatchTtlMinutes = 30;
    /// <summary>Observe pulse budget in @event peer (align ≥ InventoryObservePulseMax — gaps×9+).</summary>
    public const int EventPulseMax = CitizenRouteHost.InventoryObservePulseMax;
    public const int PeerTipMax = 72;

    static readonly object Gate = new();
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static int Generation;
    static string? LastPeerLine;
    static string? LastEventBlock;
    static DateTimeOffset? LastStampedUtc;
    static bool DiskHydrated;

    public sealed record Result(string Peer, string Event, int Applied, int Dropped, int Generation);

    /// <summary>Test hook: redirect latch root (shares voice latch override when set).</summary>
    internal static string? RootOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? CideIntercomVoiceLatch.RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string LatchPath => Path.Combine(StateRoot, "citizen-peer-LATEST.json");

    /// <summary>Last peer pulse from host execute (null until first ack / expired latch).</summary>
    public static string? LastPeer
    {
        get
        {
            lock (Gate)
            {
                EnsureHydrated();
                return LastPeerLine;
            }
        }
    }

    /// <summary>Last <c>@event peer</c> block (null until first ack / expired latch).</summary>
    public static string? LastEvent
    {
        get
        {
            lock (Gate)
            {
                EnsureHydrated();
                return LastEventBlock;
            }
        }
    }

    public static void ResetForTests()
    {
        lock (Gate)
        {
            Generation = 0;
            LastPeerLine = null;
            LastEventBlock = null;
            LastStampedUtc = null;
            // Skip disk hydrate in unit tests unless RootOverrideForTests points at a temp root.
            DiskHydrated = true;
            if (RootOverrideForTests is null)
                return;
            try
            {
                if (File.Exists(LatchPath))
                    File.Delete(LatchPath);
            }
            catch
            {
                // ignore test cleanup
            }
        }
    }

    /// <summary>Simulate process remount: forget memory, keep disk latch for hydrate.</summary>
    internal static void DropMemoryForTests()
    {
        lock (Gate)
        {
            Generation = 0;
            LastPeerLine = null;
            LastEventBlock = null;
            LastStampedUtc = null;
            DiskHydrated = false;
        }
    }

    public static Result FromExecuted(IReadOnlyList<CitizenRouteHost.Applied>? executed)
    {
        executed ??= [];
        var applied = 0;
        var dropped = 0;
        foreach (var a in executed)
        {
            if (a.Ok) applied++;
            else dropped++;
        }

        int gen;
        string peer;
        string ev;
        lock (Gate)
        {
            EnsureHydrated();
            gen = ++Generation;
            peer = FormatPeer(gen, applied, executed.Count, executed);
            ev = FormatEvent(gen, executed);
            LastPeerLine = peer;
            LastEventBlock = ev;
            LastStampedUtc = DateTimeOffset.UtcNow;
            DiskHydrated = true;
            PersistLocked();
        }

        return new Result(peer, ev, applied, dropped, gen);
    }

    static void EnsureHydrated()
    {
        if (DiskHydrated)
        {
            if (IsExpiredLocked())
                ClearMemoryLocked();
            return;
        }

        DiskHydrated = true;
        try
        {
            if (!File.Exists(LatchPath))
                return;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<PeerLatchDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return;
            if (doc.StampedUtc == default
                || DateTimeOffset.UtcNow - doc.StampedUtc > TimeSpan.FromMinutes(LatchTtlMinutes))
                return;
            if (string.IsNullOrWhiteSpace(doc.Event) && string.IsNullOrWhiteSpace(doc.Peer))
                return;

            Generation = Math.Max(Generation, doc.Generation);
            LastPeerLine = string.IsNullOrWhiteSpace(doc.Peer) ? null : doc.Peer;
            LastEventBlock = string.IsNullOrWhiteSpace(doc.Event) ? null : doc.Event;
            LastStampedUtc = doc.StampedUtc;
        }
        catch
        {
            // leave memory empty
        }
    }

    static bool IsExpiredLocked() =>
        LastStampedUtc is { } stamp
        && DateTimeOffset.UtcNow - stamp > TimeSpan.FromMinutes(LatchTtlMinutes);

    static void ClearMemoryLocked()
    {
        LastPeerLine = null;
        LastEventBlock = null;
        LastStampedUtc = null;
    }

    static void PersistLocked()
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var doc = new PeerLatchDoc
            {
                Schema = Schema,
                Peer = LastPeerLine,
                Event = LastEventBlock,
                Generation = Generation,
                StampedUtc = LastStampedUtc ?? DateTimeOffset.UtcNow
            };
            var json = JsonSerializer.Serialize(doc, JsonOpts);
            var tmp = LatchPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, LatchPath, overwrite: true);
        }
        catch
        {
            // in-memory still valid for this process
        }
    }

    static string FormatPeer(int gen, int applied, int total, IReadOnlyList<CitizenRouteHost.Applied> executed)
    {
        var peer = $"ok · gen={gen} · mcp=live · compact=no · ack={applied}/{total}";
        var tip = FirstPulseTip(executed);
        return tip is null ? peer : peer + " · " + tip;
    }

    static string FormatEvent(int gen, IReadOnlyList<CitizenRouteHost.Applied> executed)
    {
        if (executed.Count == 0)
        {
            return string.Join('\n',
            [
                "@event peer v0",
                "kind  | intent_ack",
                $"id    | turn-{gen}",
                "ack   | —"
            ]);
        }

        var sb = new StringBuilder();
        for (var i = 0; i < executed.Count; i++)
        {
            if (i > 0)
                sb.Append('\n').Append('\n');

            var a = executed[i];
            var kind = a.Ok ? "intent_ack" : "intent_dropped";
            var status = a.Ok ? "applied" : "dropped";
            var label = ShortIntent(a);
            sb.Append("@event peer v0\n");
            sb.Append("kind  | ").Append(kind).Append('\n');
            sb.Append("id    | turn-").Append(gen).Append('-').Append(i + 1).Append('\n');
            sb.Append("ack   | ").Append(label).Append(" → ").Append(status);
            if (!string.IsNullOrWhiteSpace(a.Pulse))
                sb.Append('\n').Append("pulse | ").Append(OneLine(a.Pulse, EventPulseMax));
            else if (!string.IsNullOrWhiteSpace(a.Reason))
                sb.Append(" (").Append(OneLine(a.Reason, 120)).Append(')');

            // take (and future ship verbs): body into Completions afferent — not OneLine pulse.
            if (!string.IsNullOrWhiteSpace(a.Ship))
            {
                sb.Append('\n').Append("ship  |");
                sb.Append('\n').Append(a.Ship.TrimEnd());
            }
        }

        return sb.ToString();
    }

    static string? FirstPulseTip(IReadOnlyList<CitizenRouteHost.Applied> executed)
    {
        foreach (var a in executed)
        {
            if (!string.IsNullOrWhiteSpace(a.Pulse))
                return OneLine(a.Pulse, PeerTipMax);
            if (!a.Ok && !string.IsNullOrWhiteSpace(a.Reason))
                return OneLine(a.Reason, PeerTipMax);
        }

        return null;
    }

    static string OneLine(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        var t = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        if (t.Length > max)
            t = t[..(max - 1)] + "…";
        return t;
    }

    static string ShortIntent(CitizenRouteHost.Applied a)
    {
        if (!string.IsNullOrWhiteSpace(a.Raw))
        {
            var raw = a.Raw.Trim();
            if (raw.StartsWith("@intent", StringComparison.OrdinalIgnoreCase))
                raw = raw["@intent".Length..].Trim();
            if (raw.Length > 48)
                raw = raw[..45] + "…";
            return "intent-" + raw.Replace(' ', '_');
        }

        if (!string.IsNullOrWhiteSpace(a.Go))
            return "intent-go=" + a.Go;
        return "intent-" + a.Verb.ToLowerInvariant();
    }

    sealed class PeerLatchDoc
    {
        public string Schema { get; set; } = CitizenPeerAck.Schema;
        public string? Peer { get; set; }
        public string? Event { get; set; }
        public int Generation { get; set; }
        public DateTimeOffset StampedUtc { get; set; }
    }
}
