using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Durable "Restore Previous" desk bookmark — survives MCP kill/reload.
/// Restores project + session plane + open buffer paths (from disk). Not LLM chat context.
/// </summary>
internal static class DeskBookmark
{
    public const string Schema = "desk_bookmark/v1";
    public const int MaxBuffers = 24;

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cdp-mcp",
        "desk-previous.json");

    public static void Save(SessionContext session, DocumentBufferStore buffers)
    {
        var openPath = session.SolutionOrProjectPath
                       ?? session.TsConfigPath
                       ?? session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(openPath) && buffers.All.Count == 0)
            return;

        var bufferPaths = buffers.All
            .Select(b => b.Path)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxBuffers)
            .ToList();

        // Prefer open buffers; fill from file MRU if desk is thin.
        if (bufferPaths.Count == 0)
        {
            foreach (var p in EditorComfort.RecentFilePaths())
            {
                if (!File.Exists(p)) continue;
                bufferPaths.Add(p);
                if (bufferPaths.Count >= MaxBuffers) break;
            }
        }

        var focus = bufferPaths.FirstOrDefault();
        var doc = new DeskBookmarkDoc
        {
            Schema = Schema,
            SavedUtc = DateTime.UtcNow.ToString("O"),
            OpenPath = openPath,
            SessionJson = SessionSnapshot.Capture(session),
            Buffers = bufferPaths
                .Select(p => new DeskBufferRef
                {
                    Path = p,
                    Focus = string.Equals(p, focus, StringComparison.OrdinalIgnoreCase)
                })
                .ToList(),
            Note = "Desk only — LLM chat context out of scope (separate follow-up)."
        };

        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts));
            File.Move(tmp, FilePath, overwrite: true);
        }
    }

    public static DeskBookmarkDoc? TryLoad()
    {
        lock (Gate)
        {
            if (!File.Exists(FilePath))
                return null;
            try
            {
                return JsonSerializer.Deserialize<DeskBookmarkDoc>(File.ReadAllText(FilePath), JsonOpts);
            }
            catch
            {
                return null;
            }
        }
    }

    public static string PeekJson()
    {
        var doc = TryLoad();
        if (doc is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                error = "no_desk_bookmark",
                path = FilePath,
                hint = "Work with cdp_open / buffers first — bookmark autosaves; then cdp_restore after MCP reload."
            }, JsonOpts);
        }

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "peek",
            path = FilePath,
            saved_utc = doc.SavedUtc,
            open_path = doc.OpenPath,
            buffer_count = doc.Buffers.Count,
            buffers = doc.Buffers.Select(b => b.Path),
            note = doc.Note
        }, JsonOpts);
    }

    /// <summary>
    /// Restore desk into this MCP process (second instance / after hard deploy).
    /// </summary>
    public static string Restore(
        SessionContext session,
        DocumentBufferStore buffers,
        Func<string, ProjectOpenResult> detectOpen,
        Action? syncShellCwd,
        Action? notifyListChanged)
    {
        var doc = TryLoad()
                  ?? throw new InvalidOperationException(
                      $"No desk bookmark at {FilePath}. Open a project first (autosave), then restore after reload.");

        string? openPayload = null;
        var openedBuffers = new List<object>();
        var skipped = new List<object>();

        if (doc.OpenPath is { Length: > 0 } && (File.Exists(doc.OpenPath) || Directory.Exists(doc.OpenPath)))
        {
            var open = detectOpen(doc.OpenPath);
            openPayload = IdeLanguageTools.ApplyOpen(session, open);
            syncShellCwd?.Invoke();
            notifyListChanged?.Invoke();
        }
        else if (!string.IsNullOrWhiteSpace(doc.SessionJson))
        {
            // No open path on disk — still apply plane fields if present.
            SessionSnapshot.Apply(session, doc.SessionJson);
            notifyListChanged?.Invoke();
        }

        // Re-apply phase/object/intent from bookmark after ApplyOpen (which sets explore/code).
        if (!string.IsNullOrWhiteSpace(doc.SessionJson))
            SessionSnapshot.Apply(session, doc.SessionJson);

        string? focusPath = null;
        foreach (var b in doc.Buffers)
        {
            if (string.IsNullOrWhiteSpace(b.Path) || !File.Exists(b.Path))
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
                openedBuffers.Add(new { path = buf.Path, doc_id = buf.DocId });
            }
            catch (Exception ex)
            {
                skipped.Add(new { path = b.Path, reason = ex.Message });
            }
        }

        if (focusPath is { Length: > 0 })
            EditorComfort.PushLocus(session, focusPath);

        // Refresh bookmark with what we actually restored (alive desk).
        Save(session, buffers);

        return JsonSerializer.Serialize(new
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

    internal sealed class DeskBookmarkDoc
    {
        public string Schema { get; set; } = DeskBookmark.Schema;
        public string? SavedUtc { get; set; }
        public string? OpenPath { get; set; }
        public string? SessionJson { get; set; }
        public List<DeskBufferRef> Buffers { get; set; } = [];
        public string? Note { get; set; }
    }

    internal sealed class DeskBufferRef
    {
        public string Path { get; set; } = "";
        public bool Focus { get; set; }
    }
}
