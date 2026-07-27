#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Durable "Restore Previous" desk bookmark — survives MCP kill/reload.
/// Restores project + session plane + open buffer paths (from disk). Not LLM chat context.
/// Partials: Restore (apply project / buffers / payload).
/// </summary>
internal static partial class DeskBookmark
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
