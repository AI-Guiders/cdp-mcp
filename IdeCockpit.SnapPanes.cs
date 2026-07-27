#nullable enable
using System.Text.Json;
using CdpMcp.Cockpit.Surface;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>World/editor/quiet snap pane wrappers.</summary>
internal static partial class IdeCockpit
{
    static readonly WorldSnapPaneUnit WorldSnapPanes = new();
    static readonly EditorSnapPaneUnit EditorSnapPanes = new();

    static object WorldSnapPane(
        string organ,
        JsonElement? git,
        ShellSnap shell,
        InternetBrowserHabitat.BrowserPulse browser,
        McpOutletHabitat.McpPulse mcp)
    {
        var pin = CanonicalOrganPin(organ);
        return WorldSnapPanes.Build(pin, new WorldSnapPaneUnit.Habitat(
            GitAvailable: git is not null,
            GitPulse: GitPulseLine(git),
            ShellTabCount: shell.TabCount,
            ShellRunning: shell.Running,
            BrowserOk: browser.Ok,
            BrowserLine: browser.Line,
            McpOk: mcp.Ok,
            McpLine: mcp.Line));
    }

    static object EditorSnapPane(BufferSnap buffer) =>
        EditorSnapPanes.Build(new EditorSnapPaneUnit.BufferCounts(
            buffer.Count, buffer.DirtyCount, buffer.DiskChangedCount));

    static object QuietNoProjectPane(string organ) => new
    {
        ok = true,
        go = organ,
        detail = "pulse",
        pulse = "no project — cdp_open",
        quiet = true,
        hint = "cdp_open first; pane_full= to force organ dump anyway."
    };

}
