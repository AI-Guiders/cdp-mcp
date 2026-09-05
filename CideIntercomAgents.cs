using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// ADR-0212 stage (b): NickRegistry — the chat-room roster. N sibling agent lines
/// of one harness claim nicks here; Send to=@Nick routes through this registry.
/// witdb append-only beside intercom.witdb (same StateRoot, ADR-0212).
/// </summary>
internal static class CideIntercomAgents
{
    public const string Schema = "cide_intercom_agents/v1";

    static readonly object Gate = new();

    public static string WitDbPath =>
        Path.Combine(CideIntercomVoiceLatch.StateRoot, "intercom-agents.witdb");

    public sealed record AgentRow(
        string Nick,
        string Kind,
        string? LineId,
        string Harness,
        string? Session,
        DateTimeOffset StampedUtc);

    static string Encode(AgentRow r) =>
        $"{r.StampedUtc:O}\tnick={r.Nick}\tkind={r.Kind}\tline={r.LineId ?? "-"}\tharness={r.Harness}\tsession={r.Session ?? "-"}";

    static AgentRow? Decode(string line)
    {
        var parts = line.Split('\t');
        if (parts.Length < 5 || !parts[1].StartsWith("nick="))
            return null;
        DateTimeOffset stamp;
        try { stamp = DateTimeOffset.Parse(parts[0]); }
        catch { return null; }
        return new AgentRow(
            parts[1][5..],
            parts[2][5..],
            parts[3][5..] is "-" ? null : parts[3][5..],
            parts[4][8..],
            parts.Length > 5 && parts[5][8..] is "-" ? null : parts[5][8..],
            stamp);
    }

    /// <summary>Append claim. Same nick + same harness → update (re-claim).
    /// Same nick + different harness → null (nick_taken, honesty over takeover).</summary>
    public static AgentRow? Claim(string nick, string kind, string? lineId, string harness, string? session)
    {
        if (string.IsNullOrWhiteSpace(nick) || string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(harness))
            return null;

        lock (Gate)
        {
            var existing = Roster();
            var mine = existing.Where(a => a.Nick.Equals(nick.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (mine.Length > 0 && mine.Any(a => !a.Harness.Equals(harness, StringComparison.OrdinalIgnoreCase)))
                return null; // nick_taken by another harness — claim a different nick

            Directory.CreateDirectory(Path.GetDirectoryName(WitDbPath)!);
            var row = new AgentRow(nick.Trim(), kind.Trim(), lineId, harness.Trim(), session, DateTimeOffset.UtcNow);
            File.AppendAllText(WitDbPath, Encode(row) + Environment.NewLine);
            return row;
        }
    }

    /// <summary>Latest live row per nick (last claim wins within same harness).</summary>
    public static AgentRow? Resolve(string nick)
    {
        if (string.IsNullOrWhiteSpace(nick))
            return null;
        var mine = Roster()
            .Where(a => a.Nick.Equals(nick.Trim().TrimStart('@'), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return mine.Length == 0 ? null : mine[^1];
    }

    /// <summary>ADR-0212 stage (c): parse @mentions from a body and resolve them through
    /// the roster. Unknown nicks are dropped silently — a mention is a courtesy bell,
    /// not a hard address. Used by Send to stamp doc.Mentions (per-line inbox key).</summary>
    public static IReadOnlyList<string> MentionsOf(string? body)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
            return found;
        foreach (var m in System.Text.RegularExpressions.Regex.Matches(body, @"@[\wа-яё\-]+"))
        {
            var nick = m.ToString().TrimStart('@');
            if (found.Any(f => f.Equals(nick, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (Resolve(nick) is not null)
                found.Add(nick);
        }

        return found;
    }


    public static IReadOnlyList<AgentRow> Roster()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(WitDbPath))
                    return Array.Empty<AgentRow>();
                return File.ReadAllLines(WitDbPath)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(Decode)
                    .Where(r => r is not null)
                    .Select(r => r!)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<AgentRow>();
            }
        }
    }
}
