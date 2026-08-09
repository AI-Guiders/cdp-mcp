#nullable enable
using System.Text;
using System.Text.RegularExpressions;

namespace CdpMcp;

/// <summary>
/// Human-faced Intercom surface: strip wire (@intent/@event/@frame) from prose.
/// Hands receipt lives on SoftOrgan (<see cref="CideHandsLatch"/>) — not letter laundry.
/// </summary>
internal static partial class CitizenIntercomHumanSurface
{
    static readonly Regex WireLine = WireLineRegex();

    /// <summary>Publish body for Glass Intercom — Sierra prose only; receipt → SoftOrgan HND chip.</summary>
    public static string Publish(
        string? prose,
        IReadOnlyList<CitizenRouteHost.Applied>? executed = null,
        TimeSpan? elapsed = null)
    {
        _ = executed;
        _ = elapsed;
        return StripWire(prose);
    }

    /// <summary>Drop @intent / @event / @frame lines and peer-wire tips; keep human prose.</summary>
    public static string StripWire(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";

        var sb = new StringBuilder();
        var blankRun = 0;
        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var t = line.TrimStart();
            if (t.Length == 0)
            {
                if (sb.Length == 0 || blankRun > 0)
                    continue;
                blankRun++;
                sb.Append('\n');
                continue;
            }

            blankRun = 0;
            if (IsWireLine(t))
                continue;

            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(line);
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// SoftOrgan tip body (OK/FAIL/RUNNING keywords). Letter Publish no longer appends this.
    /// </summary>
    public static string FormatHands(
        IReadOnlyList<CitizenRouteHost.Applied>? executed,
        TimeSpan? elapsed = null) =>
        CitizenHandsReceipt.FormatTip(executed, elapsed);

    /// <summary>
    /// @frame desk / Autoi charge mirrored as Citizen — instrument dump, not human Radio.
    /// </summary>
    public static bool LooksLikeSaInstrumentWall(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        var t = body;
        if (t.Contains("truncated habitat wake", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.Contains("`tm |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("**`tm |", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.Contains("`board |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("board | P:", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.Contains("`peer |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`dialog |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`presence |", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.Contains("operator_priority", StringComparison.OrdinalIgnoreCase)
            && t.Length > 240)
            return true;

        return false;
    }

    static bool IsWireLine(string t)
    {
        if (t.StartsWith("@intent", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("@event", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("@frame", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("@pulse", StringComparison.OrdinalIgnoreCase))
            return true;

        // Peer tip: ok · gen=… · mcp=live · ack=
        if (t.StartsWith("ok · gen=", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.Contains(" · mcp=live · ", StringComparison.Ordinal)
            && t.Contains("ack=", StringComparison.Ordinal))
            return true;

        // @frame desk SA instrument bullets (stay out of human Intercom prose).
        if (IsSaInstrumentLine(t))
            return true;

        // @event table rows: "kind  | intent_ack"
        if (WireLine.IsMatch(t))
            return true;

        return false;
    }

    static bool IsSaInstrumentLine(string t)
    {
        var s = t.TrimStart('-', '*', ' ', '`');
        if (s.StartsWith("tm |", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("board |", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("peer |", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("dialog |", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("sticky |", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("presence |", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("cost |", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("sa |", StringComparison.OrdinalIgnoreCase))
            return true;

        // Markdown-wrapped: - **`tm | …`**
        if (t.Contains("`tm |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`board |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`peer |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`dialog |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`presence |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`sticky |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`cost |", StringComparison.OrdinalIgnoreCase)
            || t.Contains("`sa |", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    [GeneratedRegex(@"^(kind|id|ack|pulse)\s*\|\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WireLineRegex();
}
