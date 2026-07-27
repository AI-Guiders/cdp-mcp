#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.ComputingUnits;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>BuildAsync world-snap / go-dispatch peel.</summary>
internal static partial class IdeCockpit
{
    static async Task<(
        object? GoResult,
        string? GoVerb,
        JsonElement? Git,
        ShellSnap Shell,
        InternetBrowserHabitat.BrowserPulse Browser,
        McpOutletHabitat.McpPulse Mcp)>
        ApplyWorldOrGoAsync(
            string? goVerb,
            object? goResult,
            IReadOnlyDictionary<string, JsonElement> args,
            BufferSnap buffer,
            string? focusId,
            SessionContext session,
            IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
            bool includeSubmodules,
            ShellHabitat shellHabitat,
            InternetBrowserHabitat internetBrowser,
            McpOutletHabitat mcpOutlet,
            JsonElement? git,
            ShellSnap shell,
            InternetBrowserHabitat.BrowserPulse browser,
            McpOutletHabitat.McpPulse mcpPulse,
            Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> dispatch,
            CancellationToken cancellationToken)
    {
        var worldSnap = WorldSceneGo.Compute(new WorldSceneGoUnit.Input(
            GoVerb: goVerb,
            GoDetail: OptString(args, "go_detail"),
            HasGoArgs: args.ContainsKey("go_args"),
            IsWorldSceneGo: goVerb is { Length: > 0 } && IdeWorldChannel.IsWorldSceneGo(goVerb)));
        if (worldSnap.UseWorldSnap && worldSnap.Pin is { Length: > 0 } pinEarly)
        {
            var pin = ResolvePinName(pinEarly) ?? pinEarly;
            goResult = WorldSnapPane(pin, git, shell, browser, mcpPulse);
            if (IdeDeskSeats.IsSeatsMode() && IsPlaceableOrgan(pin))
                IdeDeskSeats.PlaceOrgan(pin);
            return (goResult, null, git, shell, browser, mcpPulse);
        }

        if (goVerb is not { Length: > 0 })
            return (goResult, goVerb, git, shell, browser, mcpPulse);

        var pinGo = ResolvePinName(goVerb.Trim()) ?? goVerb.Trim();
        goResult = await DispatchGoAsync(goVerb.Trim(), args, buffer, focusId, dispatch, cancellationToken)
            .ConfigureAwait(false);
        if (IdeDeskSeats.IsSeatsMode() && IsPlaceableOrgan(pinGo))
            IdeDeskSeats.PlaceOrgan(pinGo);

        if (IdeWorldChannel.IsWorldOrgan(pinGo))
        {
            shell = CollectShell(shellHabitat.Scene());
            browser = internetBrowser.Pulse();
            mcpPulse = mcpOutlet.Pulse();
            if (CanonicalOrganPin(pinGo) is "git_scene")
                git = await TryGitAsync(session, byDomain, includeSubmodules, cancellationToken).ConfigureAwait(false);
        }

        return (goResult, null, git, shell, browser, mcpPulse);
    }
}
