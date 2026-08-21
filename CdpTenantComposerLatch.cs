#nullable enable
using System.Collections.Concurrent;

namespace CdpMcp;

/// <summary>Per-bridge composer identity for same-window multi-chat (ADR-0200).</summary>
internal static class CdpTenantComposerLatch
{
    static readonly ConcurrentDictionary<string, string> ByBridge = new(StringComparer.Ordinal);

    public static string Get(string bridgeSession) =>
        ByBridge.TryGetValue(bridgeSession, out var c) ? c : "main";

    public static bool TrySet(string? bridgeSession, string? composer)
    {
        if (string.IsNullOrWhiteSpace(bridgeSession)
            || bridgeSession.Equals(CdpTenantKey.LegacyDefault.BridgeSession, StringComparison.Ordinal))
            return false;

        var key = CdpTenantKey.Normalize(bridgeSession, "default", composer);
        ByBridge[bridgeSession] = key.Composer;
        return true;
    }

    public static object Snapshot(string bridgeSession) => new
    {
        bridge = bridgeSession,
        composer = Get(bridgeSession),
        adr = "0200"
    };
}
