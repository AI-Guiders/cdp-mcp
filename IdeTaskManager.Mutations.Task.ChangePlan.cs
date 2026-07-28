#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeTaskManager
{
    static object TaskChangePlan(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var projectRoot = Opt(args, "project_root") ?? OptGoArg(args, "project_root");
        return IdeChangePlanner.Handle(store, state, projectRoot, args);
    }
}
