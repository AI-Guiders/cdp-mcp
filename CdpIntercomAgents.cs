#nullable enable

namespace CdpMcp;

/// <summary>
/// ADR-0219 §DI.2 — IntercomAgents as an instance: the chat-room roster over the
/// append-only TSV registry (ADR-0212). One instance = one composed roster path;
/// the static facade <see cref="CideIntercomAgents"/> delegates to the default
/// (transitional period). pathProvider is consulted per call — the facade test
/// seam stays live until tests construct their own graphs.
/// </summary>
internal sealed class CdpIntercomAgents
{
    public const string Schema = "cide_intercom_agents/v1";

    readonly object Gate = new();
    readonly Func<string> _path;

    public CdpIntercomAgents(Func<string>? pathProvider = null)
        => _path = pathProvider ?? DefaultPath;

    static string DefaultPath() =>
        Path.Combine(CideIntercomVoiceLatch.StateRoot, "intercom-agents.witdb");

    public string WitDbPath => _path();

    static string Encode(CideIntercomAgents.AgentRow r) =>
        $"{r.StampedUtc:O}\tnick={r.Nick}\tkind={r.Kind}\tline={r.LineId ?? "-"}\tharness={r.Harness}\tsession={r.Session ?? "-"}";

    static CideIntercomAgents.AgentRow? Decode(string line)
    {
        var parts = line.Split('\t');
        if (parts.Length < 5 || !parts[1].StartsWith("nick="))
            return null;
        DateTimeOffset stamp;
        try { stamp = DateTimeOffset.Parse(parts[0]); }
        catch { return null; }
        return new CideIntercomAgents.AgentRow(
            parts[1][5..],
            parts[2][5..],
            parts[3][5..] is "-" ? null : parts[3][5..],
            parts[4][8..],
            parts.Length > 5 && parts[5][8..] is "-" ? null : parts[5][8..],
            stamp);
    }

    /// <summary>Append claim. Same nick + same harness → update (re-claim).
    /// Same nick + different harness → null (nick_taken, honesty over takeover).</summary>
    public CideIntercomAgents.AgentRow? Claim(string nick, string kind, string? lineId, string harness, string? session)
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
            var row = new CideIntercomAgents.AgentRow(nick.Trim(), kind.Trim(), lineId, harness.Trim(), session, DateTimeOffset.UtcNow);
            File.AppendAllText(WitDbPath, Encode(row) + Environment.NewLine);
            return row;
        }
    }

    /// <summary>Latest live row per nick (last claim wins within same harness).</summary>
    public CideIntercomAgents.AgentRow? Resolve(string nick)
    {
        if (string.IsNullOrWhiteSpace(nick))
            return null;
        var mine = Roster()
            .Where(a => a.Nick.Equals(nick.Trim().TrimStart('@'), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return mine.Length == 0 ? null : mine[^1];
    }

    /// <summary>
    /// Умный дефолт seat для армов без явного harness (Света 2026-09-08, «дефолт на опенкод»):
    /// ЕДИНСТВЕННАЯ живая opencode-линия из реестра (witdb, last-claim-wins per nick).
    /// Нет живой линии или их 2+ — null: арм честно отказывается (no_live_line / ambiguous_live_line),
    /// а не молча стучит в чужую линию (Тихон 2026-09-08: в двух-линейном мире «свежайшая» ≠ вызывающий,
    /// timer-армы уходили Ток).
    /// </summary>
    public CideIntercomAgents.AgentRow? ResolveDefaultSeat(out IReadOnlyList<CideIntercomAgents.AgentRow> liveCandidates)
    {
        var seats = Roster()
            .Where(a => a.Harness.Equals("opencode", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(a.Session))
            .GroupBy(a => a.Nick, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderByDescending(a => a.StampedUtc)
            .ToArray();
        liveCandidates = seats;
        return seats.Length == 1 ? seats[0] : null;
    }

    /// <summary>ADR-0212 stage (c): parse @mentions from a body and resolve them through
    /// the roster. Unknown nicks are dropped silently — a mention is a courtesy bell,
    /// not a hard address. Used by Send to stamp doc.Mentions (per-line inbox key).</summary>
    public IReadOnlyList<string> MentionsOf(string? body)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
            return found;
        // Inline-code spans — не адресаты: цитата `@Ник` в уроке/посте не звонит в колокол
        // (Тень 2026-09-07: три self-wake от постов ОБ упоминаниях, кавычки не различались).
        var prose = System.Text.RegularExpressions.Regex.Replace(body, "```.*?```", " ",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        prose = System.Text.RegularExpressions.Regex.Replace(prose, "`[^`]*`", " ");
        foreach (var m in System.Text.RegularExpressions.Regex.Matches(prose, @"@[\wа-яё\-]+"))
        {
            var nick = m.ToString().TrimStart('@');
            if (found.Any(f => f.Equals(nick, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (Resolve(nick) is not null)
                found.Add(nick);
        }

        return found;
    }

    public IReadOnlyList<CideIntercomAgents.AgentRow> Roster()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(WitDbPath))
                    return Array.Empty<CideIntercomAgents.AgentRow>();
                return File.ReadAllLines(WitDbPath)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(Decode)
                    .Where(r => r is not null)
                    .Select(r => r!)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<CideIntercomAgents.AgentRow>();
            }
        }
    }
}
