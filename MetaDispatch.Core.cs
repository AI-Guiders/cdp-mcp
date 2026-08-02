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
                return IdeDeploy.Run(d.Session, callArgs);
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
                return d.InternetBrowser.Dispatch(callArgs);
            case "cdp_settings":
                return d.IdeSettings.Dispatch(callArgs);
            case "cdp_search":
                return IdeFindChannel.HandleJson(d.DocStore, d.Session, callArgs);
            default:
                return null;
        }
    }
}
