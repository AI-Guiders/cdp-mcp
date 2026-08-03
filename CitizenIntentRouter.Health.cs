#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent health|cdp_health — backend health without Cursor MCP. go=health stays Verb.Go place-only.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteHealth(string raw)
    {
        var work = NormalizeHealthCompound(raw);
        var explain = ExtractKeyedValue(work, "explain_tool")
            ?? ExtractKeyedValue(work, "explain")
            ?? ExtractKeyedValue(work, "tool");

        if (string.IsNullOrWhiteSpace(explain)
            && work.StartsWith("health ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = work["health ".Length..].Trim();
            if (rest.Length > 0 && !rest.Contains('=', StringComparison.Ordinal))
                explain = rest;
            else if (rest.Length > 0)
            {
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    explain = head;
            }
        }

        return new Route(
            Verb.Health,
            raw,
            Ok: true,
            Tool: string.IsNullOrWhiteSpace(explain) ? null : explain.Trim(),
            Go: "health");
    }

    static string NormalizeHealthCompound(string raw)
    {
        foreach (var prefix in HealthPrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "health";
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "health " + raw[prefix.Length..].TrimStart();
        }

        return raw;
    }

    static readonly string[] HealthPrefixes =
    [
        "cdp_health",
        "health_desk",
        "ops_health",
        "health"
    ];
}
