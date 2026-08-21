#nullable enable
using Microsoft.AspNetCore.Http;

namespace CdpMcp;

internal static class CdpTenantHeaders
{
    public const string BridgeSession = "X-CDP-Bridge-Session";
    public const string WorkspaceKey = "X-CDP-Workspace-Key";
    public const string Composer = "X-CDP-Composer";

    public static CdpTenantKey? TryParse(IHeaderDictionary? headers)
    {
        if (headers is null)
            return null;

        if (!headers.TryGetValue(BridgeSession, out var bridgeVals) || bridgeVals.Count == 0)
            return null;

        headers.TryGetValue(WorkspaceKey, out var wsVals);
        headers.TryGetValue(Composer, out var composerVals);
        return CdpTenantKey.Normalize(
            bridgeVals[0],
            wsVals.Count > 0 ? wsVals[0] : null,
            composerVals.Count > 0 ? composerVals[0] : null);
    }

    public static bool IsPresent(IHeaderDictionary? headers) =>
        headers is not null && headers.ContainsKey(BridgeSession);
}
