using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// VS Ctrl+T / Go To All — land on anchors (file / type / member), not text Find.
/// Lean: project walk + Roslyn syntax names. Prefixes: f t m # (VS-style).
/// </summary>
internal static partial class GoToAll
{
    public const string Schema = "goto_all/v0";
    public const string ToolName = "cdp_goto";
    public const int DefaultMax = 40;
    public const int MaxFilesScan = 400;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", "node_modules", "packages", "TestResults", "publish", "publish-release", "dist"
    };

    public static bool IsGoToTool(string name) =>
        string.Equals(name, ToolName, StringComparison.OrdinalIgnoreCase);

    public static string Dispatch(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var raw = (Opt(args, "query") ?? Opt(args, "q") ?? Opt(args, "text") ?? "").Trim();
        if (raw.Length == 0)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                error = "query_required",
                hint = "query= Foo | t Bar | m Baz | f File | q undo  (Ctrl+T code / Ctrl+Q features)"
            }, Pretty);
        }

        var (kind, query) = ParseQuery(raw, Opt(args, "kind") ?? Opt(args, "filter"));
        if (query.Length == 0)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                error = "empty_query_after_prefix"
            }, Pretty);
        }

        var max = Math.Clamp(IntOr(args, "max", DefaultMax), 1, 100);

        // VS Ctrl+Q — desk verbs (no project required).
        if (kind is "feature")
        {
            var features = IdeCockpit.SearchFeatures(query, max);
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                query,
                kind = "feature",
                count = features.Length,
                hits = features.Select(f => new
                {
                    kind = "feature",
                    name = f.Go,
                    score = f.Score,
                    go = f.Go,
                    tool = f.Tool
                }),
                next = features.Length > 0
                    ? (object[])[new { go = features[0].Go, label = "Run top feature", why = "Ctrl+Q hit" }]
                    : (object[])[new { go = "goto", label = "Retry", why = "kind=feature / q: prefix" }],
                hint = "VS Ctrl+Q analogue — go= verbs. Prefix q: or kind=feature."
            }, Pretty);
        }

        var root = session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                error = "no_project",
                hint = "cdp_open first (or q: for features without project)"
            }, Pretty);
        }

        var hits = new List<Hit>();

        if (kind is "all" or "file")
            AddFiles(hits, session, root!, query, max);

        if (kind is "all" or "type" or "member" or "symbol")
            AddSymbols(hits, store, session, root!, query, kind, max);

        var ranked = hits
            .GroupBy(h => h.Anchor, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(h => h.Score).First())
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Kind, StringComparer.Ordinal)
            .ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToArray();

        object? land = null;
        if (ranked.Length > 0)
        {
            EditorComfort.PushLocus(session, ranked[0].Anchor);
            var autoPeek = BoolOr(args, "peek", defaultValue: true);
            if (autoPeek)
                land = TryLand(store, session, ranked[0]);
        }

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            query,
            kind,
            count = ranked.Length,
            hits = ranked.Select(h => new
            {
                kind = h.Kind,
                name = h.Name,
                score = h.Score,
                anchor = h.Anchor
            }),
            land,
            next = ranked.Length > 0
                ? (object[])
                [
                    new { go = "scope", label = "Sniper from land", why = $"from={ranked[0].Anchor}" },
                    new { go = "edit_draft", label = "Edit here", why = "land already open+peeked" },
                    new { go = "back", label = "Nav back", why = "locus stack" }
                ]
                : (object[])
                [
                    new { go = "goto", label = "Retry", why = "f:|t:|m:|#: or q: features" }
                ],
            hint =
                "Ctrl+T: f/t/m/# → anchors; top hit auto open+peek (peek=false to skip). " +
                "Ctrl+Q: q:undo / kind=feature."
        }, Pretty);
    }

    static object? TryLand(DocumentBufferStore store, SessionContext session, Hit top)
    {
        try
        {
            var span = BracketLocate.Parse(top.Anchor);
            if (span.File is not { Length: > 0 })
                return null;
            var path = ResolveUserPath(session, span.File);
            if (!File.Exists(path))
                return null;
            var buf = store.Open(path);
            EditorComfort.RememberFile(path);

            var line = span.LineStart ?? 1;
            var end = span.LineEnd ?? line;
            var pad = 2;
            var start = Math.Max(1, line - pad);
            var text = buf.Text;
            var lines = SplitLines(text);
            end = Math.Min(lines.Count, end + pad);
            if (end < start)
                end = start;
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

    static List<string> SplitLines(string text)
    {
        var list = new List<string>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            list.Add(line);
        return list;
    }

    static string ResolveUserPath(SessionContext session, string path)
    {
        var p = path.Trim();
        if (Path.IsPathRooted(p))
            return Path.GetFullPath(p);
        var root = session.ProjectRoot is { Length: > 0 } pr
            ? pr
            : Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(root, p));
    }

    static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }
}
