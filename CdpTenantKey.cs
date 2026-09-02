#nullable enable
using System.Text;

namespace CdpMcp;

/// <summary>Tenant identity wire: bridge:workspace:composer (ADR-0200).</summary>
internal readonly record struct CdpTenantKey(string BridgeSession, string WorkspaceKey, string Composer)
{
    public static CdpTenantKey LegacyDefault { get; } = new("legacy", "default", "main");

    public string Wire => $"{BridgeSession}:{WorkspaceKey}:{Composer}";

    public bool IsLegacyDefault =>
        BridgeSession == LegacyDefault.BridgeSession
        && WorkspaceKey == LegacyDefault.WorkspaceKey
        && Composer == LegacyDefault.Composer;

    public static CdpTenantKey Normalize(string? bridge, string? workspace, string? composer)
    {
        var b = SanitizeSegment(bridge, "legacy", 48);
        var w = SanitizeSegment(workspace, "default", 12);
        var c = SanitizeSegment(composer, "main", 32);
        return new CdpTenantKey(b, w, c);
    }

    public string ResolveTenantStateRoot()
    {
        if (IsLegacyDefault)
            return CdpProfile.StateRoot;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var wsBase = Path.Combine(local, "cdp-mcp", "ws", WorkspaceKey);
        return Path.Combine(wsBase, "tenants", BridgeSession, Composer);
    }

    static string SanitizeSegment(string? raw, string fallback, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        var s = raw.Trim();
        var buf = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')
                buf.Append(ch);
        }

        if (buf.Length == 0)
            return fallback;

        var outStr = buf.ToString();
        return outStr.Length <= maxLen ? outStr : outStr[..maxLen];
    }
}
