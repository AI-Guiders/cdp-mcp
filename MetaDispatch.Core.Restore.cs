#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>cdp_restore for MetaDispatch.Core (method_lines peel).</summary>
internal static partial class MetaDispatch
{
    static string RestoreJson(MetaDispatchDeps d, IReadOnlyDictionary<string, JsonElement> callArgs)
    {
        var session = d.Session;
        var docStore = d.DocStore;
        var settings = d.Settings;
        var shellHabitat = d.ShellHabitat;
        var NotifyListChanged = d.NotifyListChanged;

        var restoreOp = "restore";
        if (callArgs.TryGetValue("op", out var ropEl) && ropEl.GetString() is { Length: > 0 } rop)
            restoreOp = rop.Trim();

        if (string.Equals(restoreOp, "peek", StringComparison.OrdinalIgnoreCase)
            || string.Equals(restoreOp, "status", StringComparison.OrdinalIgnoreCase))
            return DeskBookmark.PeekJson();

        return DeskBookmark.Restore(
            session,
            docStore,
            detectOpen: p => settings.Languages.Detect(p),
            syncShellCwd: () => shellHabitat.SyncSessionCwd(session.ProjectRoot),
            notifyListChanged: NotifyListChanged) + "\n# list_changed: shortlist refreshed after cdp_restore";
    }
}
