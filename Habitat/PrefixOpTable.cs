#nullable enable

namespace CdpMcp.Habitat;

/// <summary>Prefix → canonical op row. Rules are ordered; first match wins (specific prefixes first).</summary>
internal readonly record struct PrefixOpRule(string Op, params string[] Prefixes);

/// <summary>Prefix/alias tables for Citizen @intent routing — data, not IRule.</summary>
internal static class PrefixOpTable
{
    internal static string? Match(string text, ReadOnlySpan<PrefixOpRule> rules)
    {
        foreach (var rule in rules)
        {
            foreach (var prefix in rule.Prefixes)
            {
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || text.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    return rule.Op;
            }
        }

        return null;
    }

    /// <summary>Strip parent prefix (e.g. "buffer ") then match sub-rules on remainder.</summary>
    internal static string? MatchSubcommand(
        string text,
        string parentPrefix,
        ReadOnlySpan<PrefixOpRule> rules,
        string? whenEmpty = null)
    {
        if (!text.StartsWith(parentPrefix, StringComparison.OrdinalIgnoreCase)
            && !text.Equals(parentPrefix.TrimEnd(), StringComparison.OrdinalIgnoreCase))
            return null;

        var rest = text.Length > parentPrefix.Length ? text[parentPrefix.Length..].TrimStart() : "";
        if (rest.Length == 0)
            return whenEmpty;

        return Match(rest, rules);
    }

    internal static string Normalize(string op, IReadOnlyDictionary<string, string> aliases)
    {
        var key = op.Trim().ToLowerInvariant();
        return aliases.TryGetValue(key, out var canon) ? canon : key;
    }
}
