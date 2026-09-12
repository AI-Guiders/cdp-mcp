#nullable enable
using System.Reflection;

namespace Cdp.Config;

/// <summary>ADR-0224: ship defaults inside the binary — operator TOML overlays diffs only.</summary>
public static class CdpEmbeddedConfig
{
    const string ResourceName = "Cdp.Deploy.Resources.cdp-mcp.defaults.toml";

    public static string LoadDefaultsToml()
    {
        var asm = typeof(CdpEmbeddedConfig).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded config missing: {ResourceName}. Rebuild Cdp.Deploy with Resources/cdp-mcp.defaults.toml.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
