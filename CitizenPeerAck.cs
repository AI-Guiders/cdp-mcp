#nullable enable
using System.Text;

namespace CdpMcp;

/// <summary>
/// Peer duplex after host execute (ADR-0028 / design <c>intent_ack</c>).
/// Surfaces applied|dropped acks and latches peer= for the next afferent inject.
/// </summary>
internal static class CitizenPeerAck
{
    static readonly object Gate = new();
    static int Generation;
    static string? LastPeerLine;
    static string? LastEventBlock;

    public sealed record Result(string Peer, string Event, int Applied, int Dropped, int Generation);

    /// <summary>Last peer pulse from host execute (null until first ack).</summary>
    public static string? LastPeer
    {
        get { lock (Gate) return LastPeerLine; }
    }

    /// <summary>Last <c>@event peer</c> block (null until first ack).</summary>
    public static string? LastEvent
    {
        get { lock (Gate) return LastEventBlock; }
    }

    public static void ResetForTests()
    {
        lock (Gate)
        {
            Generation = 0;
            LastPeerLine = null;
            LastEventBlock = null;
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
            gen = ++Generation;
            peer = FormatPeer(gen, applied, executed.Count, executed);
            ev = FormatEvent(gen, executed);
            LastPeerLine = peer;
            LastEventBlock = ev;
        }

        return new Result(peer, ev, applied, dropped, gen);
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
                sb.Append('\n').Append("pulse | ").Append(OneLine(a.Pulse, 160));
            else if (!string.IsNullOrWhiteSpace(a.Reason))
                sb.Append(" (").Append(OneLine(a.Reason, 120)).Append(')');
        }

        return sb.ToString();
    }

    static string? FirstPulseTip(IReadOnlyList<CitizenRouteHost.Applied> executed)
    {
        foreach (var a in executed)
        {
            if (!string.IsNullOrWhiteSpace(a.Pulse))
                return OneLine(a.Pulse, 48);
            if (!a.Ok && !string.IsNullOrWhiteSpace(a.Reason))
                return OneLine(a.Reason, 48);
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
}
