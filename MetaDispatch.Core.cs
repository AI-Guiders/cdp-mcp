#nullable enable
using System.Text.Json;
using ModelContextProtocol.Server;

namespace CdpMcp;

internal static partial class MetaDispatch
{
    static async Task<string?> CoreAsync(
        MetaDispatchDeps d,
        string name,
        IReadOnlyDictionary<string, JsonElement> callArgs,
        CancellationToken cancellationToken,
        object? warm = null)
    {
        _ = warm;
        switch (name)
        {
            case "cdp_man":
                return ManText(callArgs);
            case "cdp_health":
                return HealthJson(d, callArgs);
            case "cdp_capabilities":
                return CapabilitiesJson(d, callArgs);
            case "cdp_context":
                return ContextJson(d, callArgs);
            case "cdp_open":
                return OpenJson(d, callArgs);
            case "cdp_restore":
                return RestoreJson(d, callArgs);
            case "cdp_deploy":
            {
                if (IdeLifecycleJobs.ResolveBackground(callArgs))
                    return IdeLifecycleJobs.StartDeploy(d.Session, callArgs, d.Pretty);
                return IdeDeploy.Run(d.Session, callArgs);
            }
            case "cdp_elicit":
                return await IdeElicit.RunAsync(d.ServerRef, callArgs, cancellationToken).ConfigureAwait(false);
            case "cdp_land":
                return await NavigationLand.RunAsync(
                        callArgs,
                        d.Session,
                        d.DocStore,
                        detectOpen: p => d.Settings.Languages.Detect(p),
                        syncShellCwd: () => d.ShellHabitat.SyncSessionCwd(d.Session.ProjectRoot),
                        notifyListChanged: d.NotifyListChanged,
                        dispatchTool: d.DispatchToolAsync,
                        cancellationToken)
                    .ConfigureAwait(false);
            case "cdp_cide_presentation":
                return IdeCidePresentationChannel.HandleJson(callArgs);
            case "cdp_intercom":
                return IdeCideIntercomChannel.HandleJson(callArgs);
            case "cdp_citizen":
                return IdeCitizenChannel.HandleJson(callArgs);
            case "cdp_mcp":
                return await d.McpOutlet.DispatchAsync(callArgs, cancellationToken).ConfigureAwait(false);
            case "cdp_browser":
            {
                var json = d.InternetBrowser.Dispatch(callArgs);
                TryPlaceBrowserFace(callArgs, json);
                return json;
            }
            case "cdp_settings":
                return d.IdeSettings.Dispatch(callArgs);
            case "cdp_search":
                return IdeFindChannel.HandleJson(d.DocStore, d.Session, callArgs);
            default:
                return null;
        }
    }

    /// <summary>
    /// Agent cdp_browser open|search → Glass WebAiPortal Face only when face=/show=/share=/to=operator.
    /// Default peer dig = lynx text only (operator may look at something else).
    /// </summary>
    static void TryPlaceBrowserFace(IReadOnlyDictionary<string, JsonElement> args, string json)
    {
        try
        {
            if (!CitizenRouteHost.WantBrowserFaceArgs(args))
                return;

            var op = OptArg(args, "op") ?? "scene";
            op = op.Trim().ToLowerInvariant();
            if (op is not ("open" or "goto" or "navigate" or "search" or "find" or "google" or "show" or "share" or "face"))
                return;

            var faceUrl = TryReadBrowserFaceUrl(json, args);
            if (faceUrl is null)
                return;

            IdeDeskSeats.PlaceOrgan("browser", faceUrl, showFace: true);
        }
        catch
        {
            /* Face peel best-effort — lynx dump still returned */
        }
    }

    static string? TryReadBrowserFaceUrl(string json, IReadOnlyDictionary<string, JsonElement> args)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var key in new[] { "url", "search_url", "href", "uri" })
            {
                if (root.TryGetProperty(key, out var el)
                    && el.ValueKind == JsonValueKind.String
                    && el.GetString() is { Length: > 0 } u)
                    return u.Trim();
            }
        }
        catch
        {
            /* fall through */
        }

        foreach (var key in new[] { "url", "uri", "href" })
        {
            var v = OptArg(args, key);
            if (v is { Length: > 0 })
                return v.Trim();
        }

        var q = OptArg(args, "q") ?? OptArg(args, "query") ?? OptArg(args, "text");
        if (q is { Length: > 0 })
            return "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(q);

        return null;
    }

    static string? OptArg(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        return el.GetString();
    }
}
