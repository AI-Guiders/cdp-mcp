#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class DeskBookmark
{
    /// <summary>
    /// Restore desk into this MCP process (second instance / after hard deploy).
    /// </summary>
    public static string Restore(
        SessionContext session,
        DocumentBufferStore buffers,
        Func<string, ProjectOpenResult> detectOpen,
        Action? syncShellCwd,
        Action? notifyListChanged,
        int? maxBuffers = null)
    {
        var doc = TryLoad()
                  ?? throw new InvalidOperationException(
                      $"No desk bookmark at {FilePath}. Open a project first (autosave), then restore after reload.");

        var openPayload = ApplyProject(session, doc, detectOpen, syncShellCwd, notifyListChanged);

        // Re-apply phase/object/intent from bookmark after ApplyOpen (which sets explore/code).
        if (!string.IsNullOrWhiteSpace(doc.SessionJson))
            SessionSnapshot.Apply(session, doc.SessionJson);

        var cap = maxBuffers ?? ExplicitRestoreMaxBuffers;
        var (openedBuffers, skipped, focusPath) = OpenBuffers(doc, buffers, session, cap);
        Save(session, buffers);
        return FormatRestore(doc, session, openPayload, openedBuffers, skipped, focusPath);
    }

    static string? ApplyProject(
        SessionContext session,
        DeskBookmarkDoc doc,
        Func<string, ProjectOpenResult> detectOpen,
        Action? syncShellCwd,
        Action? notifyListChanged)
    {
        if (doc.OpenPath is { Length: > 0 } && (File.Exists(doc.OpenPath) || Directory.Exists(doc.OpenPath)))
        {
            var open = detectOpen(doc.OpenPath);
            var openPayload = IdeLanguageTools.ApplyOpen(session, open);
            syncShellCwd?.Invoke();
            notifyListChanged?.Invoke();
            return openPayload;
        }

        if (!string.IsNullOrWhiteSpace(doc.SessionJson))
        {
            SessionSnapshot.Apply(session, doc.SessionJson);
            notifyListChanged?.Invoke();
        }

        return null;
    }

    static (List<object> Opened, List<object> Skipped, string? FocusPath) OpenBuffers(
        DeskBookmarkDoc doc,
        DocumentBufferStore buffers,
        SessionContext session,
        int maxBuffers)
    {
        var opened = new List<object>();
        var skipped = new List<object>();
        string? focusPath = null;
        var cap = Math.Max(0, maxBuffers);

        // Focus first so AutoWarm keeps the working file when capping.
        var planned = doc.Buffers
            .Where(b => !string.IsNullOrWhiteSpace(b.Path))
            .OrderByDescending(b => b.Focus)
            .Take(cap)
            .ToList();
        var plannedPaths = planned
            .Select(b => b.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var b in planned)
        {
            if (!File.Exists(b.Path))
            {
                skipped.Add(new { path = b.Path, reason = "missing" });
                continue;
            }

            try
            {
                var buf = buffers.Open(b.Path, refresh: false);
                EditorComfort.RememberFile(buf.Path);
                if (b.Focus || focusPath is null)
                    focusPath = buf.Path;
                opened.Add(new { path = buf.Path, doc_id = buf.DocId });
            }
            catch (Exception ex)
            {
                skipped.Add(new { path = b.Path, reason = ex.Message });
            }
        }

        foreach (var b in doc.Buffers)
        {
            if (string.IsNullOrWhiteSpace(b.Path) || plannedPaths.Contains(b.Path))
                continue;
            skipped.Add(new { path = b.Path, reason = "capped" });
        }

        if (focusPath is { Length: > 0 })
            EditorComfort.PushLocus(session, focusPath);

        return (opened, skipped, focusPath);
    }

    static string FormatRestore(
        DeskBookmarkDoc doc,
        SessionContext session,
        string? openPayload,
        List<object> openedBuffers,
        List<object> skipped,
        string? focusPath) =>
        JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "restore",
            bookmark_path = FilePath,
            saved_utc = doc.SavedUtc,
            open_path = doc.OpenPath,
            open = openPayload is null
                ? (object?)null
                : JsonSerializer.Deserialize<JsonElement>(openPayload),
            session = JsonSerializer.Deserialize<JsonElement>(session.ToJson()),
            buffers_opened = openedBuffers,
            buffers_skipped = skipped,
            focus = focusPath,
            next = new object[]
            {
                new { go = "cockpit", label = "Desk pulse", why = "cdp_cockpit after restore" },
                new { go = "editor_scene", label = "Editor map", why = "see restored buffers" },
                new { go = "buffer_scene", label = "Buffer scene", why = "doc list" }
            },
            hint =
                "Desk restored (project + buffers from disk). LLM chat context is NOT restored — separate follow-up. " +
                "Dual-instance: switch survivor → go=deploy (sticky warm). Explicit restore still available.",
            note = doc.Note
        }, JsonOpts);
}
