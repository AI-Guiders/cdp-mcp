#nullable enable

namespace CdpMcp;

/// <summary>
/// Citizen wire host seam (CDP-ADR-0028 peels #7–#8).
/// Afferent packer + prepend API behind <see cref="Inject"/> (default off).
/// No live completions host — synthetic tests only until habitat chat lands.
/// </summary>
internal static class CitizenWire
{
    public const string EnvInject = "CDP_CITIZEN_WIRE_INJECT";

    /// <summary>Process latch — tests flip this; host may set true when ready.</summary>
    public static bool Inject { get; set; }

    public static bool IsInjectEnabled()
    {
        if (Inject)
            return true;
        var env = Environment.GetEnvironmentVariable(EnvInject);
        return string.Equals(env, "1", StringComparison.Ordinal)
            || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Desk A pulse fields for <c>@frame desk</c>.</summary>
    public sealed record DeskPulse(
        string Board,
        string Sa,
        string? Peer = null,
        string? Next = null,
        string? Tm = null,
        string Cost = "A");

    public static string PackDesk(DeskPulse pulse, string version = "v0")
    {
        ArgumentNullException.ThrowIfNull(pulse);
        var sb = new System.Text.StringBuilder();
        sb.Append("@frame desk ").Append(version).Append('\n');
        AppendField(sb, "board", pulse.Board);
        AppendField(sb, "sa", pulse.Sa);
        if (!string.IsNullOrWhiteSpace(pulse.Peer))
            AppendField(sb, "peer", pulse.Peer);
        if (!string.IsNullOrWhiteSpace(pulse.Next))
            AppendField(sb, "next", pulse.Next);
        if (!string.IsNullOrWhiteSpace(pulse.Tm))
            AppendField(sb, "tm", pulse.Tm);
        AppendField(sb, "cost", string.IsNullOrWhiteSpace(pulse.Cost) ? "A" : pulse.Cost.Trim());
        return sb.ToString();
    }

    /// <summary>
    /// Prepend afferent pulse to host message bodies when inject is on.
    /// Guest Cursor path must leave <see cref="Inject"/> false.
    /// </summary>
    public static IReadOnlyList<string> PrependAfferent(
        IReadOnlyList<string> messages,
        string? afferentPulse)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (!IsInjectEnabled() || string.IsNullOrWhiteSpace(afferentPulse))
            return messages;

        var list = new List<string>(messages.Count + 1) { afferentPulse.TrimEnd() + "\n" };
        list.AddRange(messages);
        return list;
    }
    /// <summary>
    /// Bind cockpit desk board lines (seat rows or banner fragments) into <see cref="DeskPulse"/>.
    /// Pure; host calls this before <see cref="PackDesk"/> — no completions loop.
    /// </summary>
    public static DeskPulse FromDeskBoard(
        IEnumerable<string>? boardLines,
        string? sa = null,
        string? peer = null,
        string? next = null,
        string? tm = null,
        string cost = "A")
    {
        var board = FormatBoard(boardLines);
        return new DeskPulse(
            Board: string.IsNullOrWhiteSpace(board) ? "(empty)" : board,
            Sa: string.IsNullOrWhiteSpace(sa) ? "clear" : sa.Trim(),
            Peer: string.IsNullOrWhiteSpace(peer) ? null : peer.Trim(),
            Next: string.IsNullOrWhiteSpace(next) ? null : next.Trim(),
            Tm: string.IsNullOrWhiteSpace(tm) ? null : tm.Trim(),
            Cost: string.IsNullOrWhiteSpace(cost) ? "A" : cost.Trim());
    }

    /// <summary><see cref="FromDeskBoard"/> then <see cref="PackDesk"/>.</summary>
    public static string PackFromDeskBoard(
        IEnumerable<string>? boardLines,
        string? sa = null,
        string? peer = null,
        string? next = null,
        string? tm = null,
        string cost = "A",
        string version = "v0") =>
        PackDesk(FromDeskBoard(boardLines, sa, peer, next, tm, cost), version);

    static string FormatBoard(IEnumerable<string>? boardLines)
    {
        if (boardLines is null)
            return "";

        var parts = new List<string>();
        foreach (var raw in boardLines)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var line = raw.Trim();
            if (line.Contains('|', StringComparison.Ordinal))
            {
                foreach (var frag in line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (TryNormalizeSeatLine(frag, out var fromBanner))
                        parts.Add(fromBanner);
                    else if (frag.Length > 0)
                        parts.Add(frag);
                }
                continue;
            }

            if (TryNormalizeSeatLine(line, out var norm))
                parts.Add(norm);
            else
                parts.Add(line);
        }

        return string.Join(" | ", parts);
    }

    /// <summary>
    /// Cockpit seat row <c>P  plan       · rest</c> or already <c>P:plan · rest</c> → wire board fragment.
    /// </summary>
    static bool TryNormalizeSeatLine(string line, out string normalized)
    {
        normalized = "";
        if (line.Length < 2)
            return false;

        // Already wire-ish: P:plan · …
        if (line.Length >= 3 && char.IsAsciiLetter(line[0]) && line[1] == ':')
        {
            normalized = line;
            return true;
        }

        // Seat glyph + organ + optional · detail
        var span = line.AsSpan().Trim();
        if (span.Length < 2)
            return false;
        var glyph = span[0];
        if (glyph is not ('P' or 'F' or 'M' or 'p' or 'f' or 'm'))
            return false;
        if (span.Length < 2 || !char.IsWhiteSpace(span[1]))
            return false;

        span = span[1..].TrimStart();
        var organEnd = 0;
        while (organEnd < span.Length && !char.IsWhiteSpace(span[organEnd]) && span[organEnd] != '·')
            organEnd++;
        if (organEnd == 0)
            return false;

        var organ = span[..organEnd].ToString();
        var rest = span[organEnd..].TrimStart();
        if (rest.StartsWith("·"))
            rest = rest[1..].TrimStart();

        normalized = rest.Length == 0
            ? $"{char.ToUpperInvariant(glyph)}:{organ}"
            : $"{char.ToUpperInvariant(glyph)}:{organ} · {rest}";
        return true;
    }


    static void AppendField(System.Text.StringBuilder sb, string key, string value)
    {
        sb.Append(key).Append(" | ").Append(value.Trim()).Append('\n');
    }
}
