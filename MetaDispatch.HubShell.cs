#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using Cdp.Core;
using Cdp.Evidence;
using Cdp.ScriptableIde;
using CdpMcp.Backends;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

internal static partial class MetaDispatch
{
    static async Task<string?> HubShellAsync(
        MetaDispatchDeps d,
        string name,
        IReadOnlyDictionary<string, JsonElement> callArgs,
        CancellationToken cancellationToken,
        object? warm = null)
    {
        var session = d.Session;
        var docStore = d.DocStore;
        var byDomain = d.ByDomain;
        var modules = d.Modules;
        var allAffordances = d.AllAffordances;
        var settings = d.Settings;
        var Pretty = d.Pretty;
        var shellHabitat = d.ShellHabitat;
        var mcpOutlet = d.McpOutlet;
        var internetBrowser = d.InternetBrowser;
        var ideSettings = d.IdeSettings;
        var workspaceStore = d.WorkspaceStore;
        var workspaceState = d.WorkspaceState;
        var workspaceDbPath = d.WorkspaceDbPath;
        var NotifyListChanged = d.NotifyListChanged;
        var EnsureOpenRecentWired = d.EnsureOpenRecentWired;
        var EnsureWorkspaceDb = d.EnsureWorkspaceDb;
        var DispatchAsync = d.DispatchToolAsync;
        var DispatchCdpWork = d.DispatchCdpWork;

        switch (name)
        {
    case "cdp_shell_scene":
        return shellHabitat.Scene();
    case "cdp_shell_run":
    {
        string? cmd = callArgs.TryGetValue("command", out var cmdEl) ? cmdEl.GetString() : null;
        string[]? argv = null;
        if (callArgs.TryGetValue("argv", out var argvEl) && argvEl.ValueKind == JsonValueKind.Array)
        {
            argv = argvEl.EnumerateArray()
                .Select(e => e.GetString() ?? throw new ArgumentException("argv entries must be strings."))
                .ToArray();
            if (argv.Length == 0)
                argv = null;
        }

        if (argv is null && string.IsNullOrWhiteSpace(cmd))
            throw new ArgumentException("command or argv is required.");
        string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
        // Cursor Shell habit: working_directory — accept as cwd alias (agent-pain sticky miss).
        string? cwd = callArgs.TryGetValue("cwd", out var cwdEl) ? cwdEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(cwd)
            && callArgs.TryGetValue("working_directory", out var wdEl))
            cwd = wdEl.GetString();
        string? shell = callArgs.TryGetValue("shell", out var shEl) ? shEl.GetString() : null;
        int? timeout = callArgs.TryGetValue("timeout_seconds", out var toEl) && toEl.TryGetInt32(out var to)
            ? to
            : IdeSettingsHabitat.EffectiveShellTimeout();
        var background = callArgs.TryGetValue("background", out var bgEl) && bgEl.ValueKind == JsonValueKind.True;
        int? codepage = callArgs.TryGetValue("codepage", out var cpEl) && cpEl.TryGetInt32(out var cp)
            ? cp
            : IdeSettingsHabitat.EffectiveShellCodepage();
        return AttachShellEvidence(Pretty, 
            shellHabitat.Run(ShellDefaults(session), cmd, tab, cwd, shell, timeout, background, codepage, argv),
            session);
    }
    case "cdp_shell_history":
    {
        string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
        var n = callArgs.TryGetValue("n", out var nEl) && nEl.TryGetInt32(out var nn) ? nn : 20;
        return shellHabitat.History(tab, n);
    }
    case "cdp_shell_rerun":
    {
        string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
        int? index = callArgs.TryGetValue("index", out var ixEl) && ixEl.TryGetInt32(out var ix)
            ? ix
            : null;
        int? timeout = callArgs.TryGetValue("timeout_seconds", out var toEl) && toEl.TryGetInt32(out var to)
            ? to
            : IdeSettingsHabitat.EffectiveShellTimeout();
        var background = callArgs.TryGetValue("background", out var bgEl) && bgEl.ValueKind == JsonValueKind.True;
        return AttachShellEvidence(Pretty, 
            shellHabitat.Rerun(ShellDefaults(session), tab, index, timeout, background),
            session);
    }
    case "cdp_shell_last":
    {
        string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
        var maxChars = callArgs.TryGetValue("max_chars", out var mcEl) && mcEl.TryGetInt32(out var mc)
            ? mc
            : 0;
        return AttachShellEvidence(Pretty, shellHabitat.Last(tab, maxChars), session);
    }
    case "cdp_shell_which":
    {
        string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
        return shellHabitat.Which(tab);
    }
    case "cdp_shell_kill":
    {
        string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
        return shellHabitat.Kill(tab);
    }
    case "cdp_shell_close":
    {
        string? tab = callArgs.TryGetValue("tab", out var tabEl) ? tabEl.GetString() : null;
        return shellHabitat.Close(tab);
    }
    default:
        return null;
        }
    }
}
