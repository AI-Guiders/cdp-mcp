namespace CdpMcpBridge;

/// <summary>
/// Dumb pipe (ADR-0209): the bridge always talks to its configured base —
/// the gatekeeper tower owns the client port and resolves the freshest healthy
/// slot from the witdb registry. No drift, no probes, no reserve port.
/// </summary>
internal static class CdpBridgeEndpoint
{
    static readonly object Gate = new();
    static Uri _configured = new("http://127.0.0.1:8771/");

    /// <summary>Bind the endpoint once at startup. reservePort ignored (ADR-0209).</summary>
    public static void Init(Uri configuredBase, int reservePort)
    {
        lock (Gate)
            _configured = configuredBase;
    }

    /// <summary>Fixed upstream base — the tower in front of the slot registry.</summary>
    public static Uri Current()
    {
        lock (Gate)
            return _configured;
    }
}
