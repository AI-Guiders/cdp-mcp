#nullable enable

using CdpMcp.Habitat;

namespace CdpMcp;

/// <summary>Citizen @intent deploy — dual-instance publish without Cursor MCP (go=deploy place-only).</summary>
internal static partial class CitizenIntentRouter
{
    static readonly PrefixOpRule[] DeployModePrefixRules =
    [
        new("hard", "hard_deploy"),
        new("soft", "soft_deploy"),
    ];

    static Route RouteDeploy(string raw)
    {
        var head = raw.Trim();
        string? mode = ExtractKeyedValue(raw, "mode");
        if (string.IsNullOrWhiteSpace(mode))
        {
            mode = PrefixOpTable.Match(head, DeployModePrefixRules);
            if (string.IsNullOrWhiteSpace(mode) && raw.StartsWith("deploy ", StringComparison.OrdinalIgnoreCase))
            {
                var rest = raw["deploy ".Length..].Trim();
                var headSp = rest.IndexOf(' ');
                var token = headSp < 0 ? rest : rest[..headSp];
                if (token.Length > 0 && !token.Contains('=', StringComparison.Ordinal)
                    && token is "hard" or "soft" or "rollout" or "apply")
                    mode = token;
            }
        }

        mode = string.IsNullOrWhiteSpace(mode) ? "hard" : mode.Trim().ToLowerInvariant();
        mode = mode switch
        {
            "h" or "hard" or "kill" => "hard",
            "s" or "soft" or "stage" => "soft",
            "r" or "rollout" or "dual" => "rollout",
            "a" or "apply" or "pending" or "apply_pending" => "apply",
            _ => mode
        };

        if (mode is not "hard" and not "soft" and not "rollout" and not "apply")
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "deploy_mode_unknown");

        var target = ExtractKeyedValue(raw, "target") ?? ExtractKeyedValue(raw, "to");
        return new Route(
            Verb.Deploy,
            raw,
            Ok: true,
            Op: mode,
            Detail: string.IsNullOrWhiteSpace(target) ? null : target.Trim(),
            Go: "deploy");
    }
}
