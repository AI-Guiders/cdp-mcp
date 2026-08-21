#nullable enable

using AIGuiders.Platform.Cockpit.DataBus;
using CdpMcp.Cockpit.DataAcquisition;

namespace CdpMcp.Cockpit.EnvironmentReadiness;

/// <summary>Headless LSP host presence probe (binary on PATH; process state from caller).</summary>
internal static class EnvironmentReadinessLspProbe
{
    public static IdeHostStateChanged ProbeHostPresence()
    {
        var csharp = ResolveCSharpHost();
        var markdown = ToolchainPathProbe.Resolve("marksman");
        return new IdeHostStateChanged(
            CSharpLspProcessActive: false,
            MarkdownLspProcessActive: false,
            CSharpLspHostPresent: csharp is not null,
            MarkdownLspHostPresent: markdown is not null);
    }

    static string? ResolveCSharpHost() =>
        ToolchainPathProbe.Resolve("csharp-ls")
        ?? ToolchainPathProbe.Resolve("OmniSharp")
        ?? ToolchainPathProbe.Resolve("basedpyright-langserver");
}
