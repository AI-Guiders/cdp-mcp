using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>Search root resolve, hit parse, land for Find in Files.</summary>
internal static partial class FindInFiles
{
    static bool TryResolveSearchRoot(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        bool external,
        out string searchRoot,
        out string cwd,
        out string error,
        out string hint)
    {
        searchRoot = "";
        cwd = "";
        error = "";
        hint = "";

        var pathArg = Opt(args, "path") ?? Opt(args, "search_in") ?? Opt(args, "root");

        if (external)
        {
            if (pathArg is not { Length: > 0 })
            {
                error = "path_required";
                hint = "scope=external needs path= absolute dir/file (or ~). Prefer narrower than volume root; else glob=.";
                return false;
            }

            searchRoot = ExpandPath(pathArg);
            if (!Path.IsPathRooted(searchRoot))
            {
                error = "path_not_rooted";
                hint = "scope=external path= must be absolute (e.g. D:\\Experiments\\agent-notes).";
                return false;
            }

            if (!Directory.Exists(searchRoot) && !File.Exists(searchRoot))
            {
                error = "path_not_found";
                hint = $"path= not found: {searchRoot}";
                return false;
            }

            cwd = Directory.Exists(searchRoot)
                ? searchRoot
                : (Path.GetDirectoryName(searchRoot) ?? searchRoot);
            return true;
        }

        var root = session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            error = "no_project";
            hint = "cdp_open first — or use scope=external path= for disk-wide find";
            return false;
        }

        searchRoot = pathArg ?? root!;
        if (!Path.IsPathRooted(searchRoot))
            searchRoot = Path.GetFullPath(Path.Combine(root!, searchRoot));
        else
            searchRoot = Path.GetFullPath(searchRoot);

        if (!Directory.Exists(searchRoot) && !File.Exists(searchRoot))
            searchRoot = root!;

        cwd = root!;
        return true;
    }

    static List<Hit> ParseJsonHits(SessionContext session, string stdout, int max)
    {
        var list = new List<Hit>();
        using var reader = new StringReader(stdout);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || list.Count >= max)
                continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeEl) ||
                    typeEl.GetString() is not "match")
                    continue;
                if (!root.TryGetProperty("data", out var data))
                    continue;
                var path = data.GetProperty("path").GetProperty("text").GetString();
                if (string.IsNullOrEmpty(path))
                    continue;
                var lineNum = data.GetProperty("line_number").GetInt32();
                var abs = Path.GetFullPath(path);
                var label = FileLabel(session, abs);
                var preview = "";
                var col = 1;
                if (data.TryGetProperty("lines", out var lines) &&
                    lines.TryGetProperty("text", out var textEl))
                {
                    preview = (textEl.GetString() ?? "").TrimEnd('\r', '\n');
                    if (preview.Length > 80)
                        preview = preview[..80] + "…";
                    preview = preview.Replace("\r", "").Replace("\n", "⏎");
                }

                if (data.TryGetProperty("submatches", out var subs) &&
                    subs.ValueKind == JsonValueKind.Array &&
                    subs.GetArrayLength() > 0 &&
                    subs[0].TryGetProperty("start", out var startEl))
                {
                    col = startEl.GetInt32() + 1;
                }

                var needle = BracketLocate.SanitizeTextNeedle(preview);
                var anchor = string.IsNullOrWhiteSpace(needle)
                    ? BracketLocate.Format(new BracketLocate.Span(label, null, lineNum, null))
                    : BracketLocate.Format(new BracketLocate.Span(label, null, lineNum, null, TextNeedle: needle));
                list.Add(new Hit(anchor, abs, lineNum, col, preview));
            }
            catch
            {
                // skip malformed json line
            }
        }

        return list;
    }

    static object? TryLand(DocumentBufferStore store, SessionContext session, Hit top)
    {
        try
        {
            if (!File.Exists(top.AbsolutePath))
                return null;
            var buf = store.Open(top.AbsolutePath);
            EditorComfort.RememberFile(top.AbsolutePath);
            var lines = SplitLines(buf.Text);
            var pad = 2;
            var start = Math.Max(1, top.Line - pad);
            var end = Math.Min(lines.Count, top.Line + pad);
            var slice = string.Join('\n', lines.Skip(start - 1).Take(end - start + 1));
            if (slice.Length > 2_400)
                slice = slice[..2_400] + "\n…";
            return new
            {
                anchor = top.Anchor,
                doc_id = buf.DocId,
                start_line = start,
                end_line = end,
                text = slice
            };
        }
        catch
        {
            return null;
        }
    }
}
