#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>cdp_open for MetaDispatch.Core (method_lines peel).</summary>
internal static partial class MetaDispatch
{
    static string OpenJson(MetaDispatchDeps d, IReadOnlyDictionary<string, JsonElement> callArgs)
    {
        var session = d.Session;
        var docStore = d.DocStore;
        var settings = d.Settings;
        var shellHabitat = d.ShellHabitat;
        var NotifyListChanged = d.NotifyListChanged;
        var EnsureOpenRecentWired = d.EnsureOpenRecentWired;

        EnsureOpenRecentWired();
        string? openPath;
        if (callArgs.TryGetValue("path", out var openPathEl) && openPathEl.GetString() is { Length: > 0 } op)
            openPath = op;
        else if (callArgs.TryGetValue("recent_index", out var riEl) && riEl.TryGetInt32(out var ri))
        {
            var hit = OpenRecentStore.TryGet(ri)
                ?? throw new ArgumentException($"No Open Recent entry at index {ri}.");
            openPath = hit.Path;
        }
        else
        {
            var hit = OpenRecentStore.TryGet(0)
                ?? throw new ArgumentException(
                    "path is required for cdp_open (or pass recent_index / open something first so Recent is non-empty).");
            openPath = hit.Path;
        }

        var open = settings.Languages.Detect(openPath);
        var park = docStore.ParkOutsideProject(open.Root);
        var payload = IdeLanguageTools.ApplyOpen(session, open, park);
        shellHabitat.SyncSessionCwd(session.ProjectRoot);
        DeskBookmark.Save(session, docStore);
        NotifyListChanged();

        // HCI-like: warm MSBuild workspace once for csharp session (background).
        if (string.Equals(session.Language, "csharp", StringComparison.OrdinalIgnoreCase)
            && session.SolutionOrProjectPath is { Length: > 0 } warmPath)
        {
            var pathCopy = warmPath;
            _ = Task.Run(async () =>
            {
                try
                {
                    await RoslynMcp.ServiceLayer.MsBuildWorkspaceHost.WarmAsync(pathCopy).ConfigureAwait(false);
                }
                catch
                {
                    // Warm is best-effort; tools still open on demand.
                }
            });
        }

        return payload + "\n# list_changed: shortlist refreshed after cdp_open";
    }
}
