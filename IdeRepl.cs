#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Cockpit Command Line (CCL) — desk <c>cmd=</c> steers seats / soft organs (ADR 0138 / 0191 / 0193).
/// Examples: <c>go browser</c>, <c>layout agent</c>, <c>probe</c>, <c>report</c>, <c>feature X</c>.
/// </summary>
internal static partial class IdeRepl
{
    public const string SchemaVersion = "ccl/v1";

    /// <summary>
    /// Merge parsed line into cockpit args. Returns help/error object when not a steer.
    /// </summary>
    public static (Dictionary<string, JsonElement> Args, object? Direct)? Apply(
        string line,
        IReadOnlyDictionary<string, JsonElement> cockpitArgs)
    {
        var raw = (line ?? "").Trim();
        if (raw.Length == 0)
            return (new Dictionary<string, JsonElement>(cockpitArgs, StringComparer.Ordinal), Help("empty"));

        // Strip leading prompt noise
        if (raw.StartsWith('>') || raw.StartsWith('$') || raw.StartsWith(':'))
            raw = raw[1..].Trim();

        var merged = new Dictionary<string, JsonElement>(cockpitArgs, StringComparer.Ordinal);
        // Consume the cmd line so we don't re-parse.
        merged.Remove("cmd");
        merged.Remove("line");
        merged.Remove("repl");
        merged.Remove("ccl");
        merged.Remove("ccc");

        var tokens = Tokenize(raw);
        if (tokens.Count == 0)
            return (merged, Help("empty"));

        var head = tokens[0].ToLowerInvariant();

        if (TryDesk(head, tokens, merged) is { } hitDesk)
            return hitDesk;
        if (TryOrgans(head, tokens, merged) is { } hitOrgans)
            return hitOrgans;
        if (TryBoard(head, tokens, merged) is { } hitBoard)
            return hitBoard;
        if (TryOps(head, tokens, merged) is { } hitOps)
            return hitOps;
        if (TryShare(head, tokens, merged) is { } hitShare)
            return hitShare;
        if (TryCrm(head, tokens, merged) is { } hitCrm)
            return hitCrm;

        return (merged, Err("unknown_cmd", "help | go report | probe | check | run | layout agent | plan"));
    }
}
