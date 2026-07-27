#nullable enable
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Surface;

/// <summary>Surface CCU: world-organ pulse pane (git/shell/browser/mcp).</summary>
public sealed class WorldSnapPaneUnit : ICockpitComputeUnit
{
    public readonly record struct Habitat(
        bool GitAvailable,
        string GitPulse,
        int ShellTabCount,
        int ShellRunning,
        bool BrowserOk,
        string BrowserLine,
        bool McpOk,
        string McpLine);

    public object Build(string pin, in Habitat h) => pin switch
    {
        "git_scene" => Pane("git_scene", h.GitAvailable, h.GitPulse),
        "shell_scene" => Pane(
            "shell_scene",
            true,
            h.ShellRunning > 0
                ? $"shell · {h.ShellTabCount} tab(s) · {h.ShellRunning} running"
                : $"shell · {h.ShellTabCount} tab(s)"),
        "browser" => Pane("browser", h.BrowserOk, h.BrowserLine),
        "mcp_scene" => Pane("mcp_scene", h.McpOk, h.McpLine),
        _ => Pane(pin, true, pin)
    };

    public static object Pane(string go, bool ok, string pulse) => new
    {
        ok,
        go,
        detail = "pulse",
        pulse,
        world = true,
        hint = "World channel: replace on M. pane_full= / go_detail=full for dump."
    };
}
