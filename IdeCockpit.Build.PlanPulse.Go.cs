#nullable enable
using System.Text.Json;
using Cdp.Core;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>Desk-pulse go= branch — editor/git/world (extracted for method/file lines).</summary>
internal static partial class IdeCockpit
{

    static async Task<(
        object? GoResult,
        JsonElement? Git,
        ShellSnap Shell,
        InternetBrowserHabitat.BrowserPulse Browser,
        McpOutletHabitat.McpPulse Mcp,
        string? ResultPin)> ApplyDeskPulseGoAsync(
        string? goVerb,
        object? goResult,
        string? resultPin,
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
        if (goVerb is not { Length: > 0 })
            return (goResult, git, shell, browser, mcpPulse, resultPin);

        var rawPin = ResolvePinName(goVerb.Trim()) ?? goVerb.Trim();
        var pin = CanonicalOrganPin(rawPin);
        resultPin = pin;

        if (pin is "editor_scene")
        {
            goResult = EditorSnapPane(buffer);
            if (IdeDeskSeats.IsSeatsMode() && IsPlaceableOrgan(rawPin))
                IdeDeskSeats.PlaceOrgan(rawPin);
            return (goResult, git, shell, browser, mcpPulse, resultPin);
        }

        if (pin is "git_scene")
        {
            git = await TryGitAsync(session, byDomain, includeSubmodules, cancellationToken)
                .ConfigureAwait(false);
            goResult = WorldSnapPane(pin, git, shell, browser, mcpPulse);
            if (IdeDeskSeats.IsSeatsMode() && IsPlaceableOrgan(rawPin))
                IdeDeskSeats.PlaceOrgan(rawPin);
            return (goResult, git, shell, browser, mcpPulse, resultPin);
        }

        (goResult, _, git, shell, browser, mcpPulse) = await ApplyWorldOrGoAsync(
            goVerb, goResult, args, buffer, focusId, session, byDomain, includeSubmodules,
            shellHabitat, internetBrowser, mcpOutlet, git, shell, browser, mcpPulse,
            dispatch, cancellationToken).ConfigureAwait(false);
        resultPin = TryGoPinFromResult(goResult) ?? pin;
        return (goResult, git, shell, browser, mcpPulse, resultPin);
    }
}
