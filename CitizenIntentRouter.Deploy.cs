#nullable enable

using Cdp.Deploy;
using CdpMcp.Habitat;

namespace CdpMcp;

/// <summary>Citizen @intent deploy — dual-instance publish without Cursor MCP (go=deploy place-only).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteDeploy(string raw)
    {
        var trimmed = raw.Trim();
        if (!CdpDeployCatalogResolver.Default.TryParseLine(trimmed, out var cmd))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "deploy_head_unknown");

        var modeToken = ExtractKeyedValue(trimmed, "mode");
        if (!string.IsNullOrWhiteSpace(modeToken) && !CdpDeployModeParser.TryParse(modeToken, out _))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "deploy_mode_unknown");

        var mode = cmd.Mode.ToWire();
        if (mode is not "hard" and not "soft" and not "rollout" and not "apply" and not "ship")
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "deploy_mode_unknown");

        return new Route(
            Verb.Deploy,
            raw,
            Ok: true,
            Op: mode,
            Detail: cmd.Target,
            Go: "deploy");
    }
}
