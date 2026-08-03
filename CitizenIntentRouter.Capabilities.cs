#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent capabilities|cdp_capabilities — mounted domains without Cursor MCP. go=capabilities stays Verb.Go place-only.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteCapabilities(string raw)
    {
        _ = NormalizeCapabilitiesCompound(raw);
        return new Route(
            Verb.Capabilities,
            raw,
            Ok: true,
            Go: "capabilities");
    }

    static string NormalizeCapabilitiesCompound(string raw)
    {
        foreach (var prefix in CapabilitiesPrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "capabilities";
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "capabilities " + raw[prefix.Length..].TrimStart();
        }

        return raw;
    }

    static readonly string[] CapabilitiesPrefixes =
    [
        "cdp_capabilities",
        "capabilities_desk",
        "caps",
        "capabilities"
    ];
}
