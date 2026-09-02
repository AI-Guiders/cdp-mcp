#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class MetaDispatch
{
    static string? IdeSessionGraph(MetaDispatchDeps d, IReadOnlyDictionary<string, JsonElement> callArgs) =>
        FederationSessionBridge.BuildSceneJson(d.Session, callArgs, d.Pretty);
}
