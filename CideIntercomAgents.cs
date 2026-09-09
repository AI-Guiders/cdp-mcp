#nullable enable
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// ADR-0212 stage (b): NickRegistry — the chat-room roster. N sibling agent lines
/// of one harness claim nicks here; Send to=@Nick routes through this registry.
/// witdb append-only beside intercom.witdb (same StateRoot, ADR-0212).
/// ADR-0219 §DI.2: thin static facade delegating to the composed
/// <see cref="CdpIntercomAgents"/> instance (transitional period); the
/// WitDbPathOverride test seam stays until tests construct their own graphs.
/// </summary>
internal static class CideIntercomAgents
{
    public const string Schema = CdpIntercomAgents.Schema;

    /// <summary>Seam для тестов (ADR-0219 переходный): подмена пути ростера делает тесты герметичными.</summary>
    public static Func<string>? WitDbPathOverride { get; set; }

    static readonly CdpIntercomAgents Default = new(() =>
        WitDbPathOverride?.Invoke()
        ?? Path.Combine(CideIntercomVoiceLatch.StateRoot, "intercom-agents.witdb"));

    public static string WitDbPath => Default.WitDbPath;

    public sealed record AgentRow(
        string Nick,
        string Kind,
        string? LineId,
        string Harness,
        string? Session,
        DateTimeOffset StampedUtc);

    /// <summary>Append claim. Same nick + same harness → update (re-claim).
    /// Same nick + different harness → null (nick_taken, honesty over takeover).</summary>
    public static AgentRow? Claim(string nick, string kind, string? lineId, string harness, string? session) =>
        Default.Claim(nick, kind, lineId, harness, session);

    /// <summary>Latest live row per nick (last claim wins within same harness).</summary>
    public static AgentRow? Resolve(string nick) => Default.Resolve(nick);

    /// <summary>Умный дефолт seat для армов без явного harness — см. <see cref="CdpIntercomAgents.ResolveDefaultSeat"/>.</summary>
    public static AgentRow? ResolveDefaultSeat(out IReadOnlyList<AgentRow> liveCandidates) =>
        Default.ResolveDefaultSeat(out liveCandidates);

    /// <summary>ADR-0212 stage (c): @mentions → roster resolution; unknown nicks dropped.</summary>
    public static IReadOnlyList<string> MentionsOf(string? body) => Default.MentionsOf(body);

    public static IReadOnlyList<AgentRow> Roster() => Default.Roster();
}
