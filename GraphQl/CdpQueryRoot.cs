#nullable enable
using System.Text.Json;
using Cdp.ScriptableIde;
using HotChocolate;

namespace CdpMcp.GraphQl;

/// <summary>GraphQL Query root (CDP-ADR-0233). Query-only; mutate stays CSX/edit_plan.</summary>
[GraphQLName("Query")]
internal sealed class CdpQueryRoot
{
    /// <summary>textHits → FindInFiles.Hit projection; default first=20 (slim).</summary>
    public TextHitConnection TextHits(
        [Service] CdpHostRuntime runtime,
        string? query = null,
        string? path = null,
        string? scope = null,
        string? glob = null,
        bool? regex = null,
        bool? ignoreCase = null,
        string? like = null,
        int first = 20)
    {
        if (first < 1) first = 1;
        if (first > 200) first = 200;

        var needle = query;
        if (string.IsNullOrWhiteSpace(needle) && !string.IsNullOrWhiteSpace(like))
            needle = LikeToRg.Translate(like!);

        if (string.IsNullOrWhiteSpace(needle))
            throw new GraphQLException("query_required: pass query= or like= (Portal-style LIKE → rg translator).");

        var session = runtime.Session;
        var store = runtime.HostDeps.DocStore;
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["query"] = JsonSerializer.SerializeToElement(needle),
            ["scope"] = JsonSerializer.SerializeToElement(string.IsNullOrWhiteSpace(scope) ? "project" : scope.Trim()),
            ["max"] = JsonSerializer.SerializeToElement(first),
            ["peek"] = JsonSerializer.SerializeToElement(false),
        };
        if (!string.IsNullOrWhiteSpace(path))
            args["path"] = JsonSerializer.SerializeToElement(path);
        if (!string.IsNullOrWhiteSpace(glob))
            args["glob"] = JsonSerializer.SerializeToElement(glob);
        if (regex is true || (!string.IsNullOrWhiteSpace(like) && string.IsNullOrWhiteSpace(query)))
            args["regex"] = JsonSerializer.SerializeToElement(true);
        if (ignoreCase is true)
            args["ignore_case"] = JsonSerializer.SerializeToElement(true);

        var json = FindInFiles.Dispatch(store, session, args, all: true);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
        {
            var err = root.TryGetProperty("error", out var e) ? e.GetString() : "search_failed";
            var hint = root.TryGetProperty("hint", out var h) ? h.GetString() : null;
            var detail = root.TryGetProperty("detail", out var d) ? d.GetString() : null;
            throw new GraphQLException($"{err}: {detail ?? hint ?? "FindInFiles failed"}");
        }

        var nodes = new List<TextHitNode>();
        if (root.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array)
        {
            foreach (var hit in hits.EnumerateArray())
            {
                var wire = hit.TryGetProperty("anchor", out var a) ? a.GetString() : null;
                var abs = hit.TryGetProperty("path", out var p) ? p.GetString()
                    : hit.TryGetProperty("absolute_path", out var ap) ? ap.GetString() : null;
                var line = hit.TryGetProperty("line", out var l) && l.TryGetInt32(out var li) ? li : 0;
                var col = hit.TryGetProperty("column", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
                var preview = hit.TryGetProperty("preview", out var pr) ? pr.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(wire) && !string.IsNullOrWhiteSpace(abs) && line > 0)
                    wire = Anchor.File(abs!).Line(line).ToWire();
                if (string.IsNullOrWhiteSpace(wire))
                    continue;
                nodes.Add(new TextHitNode(Anchor.Parse(wire!), abs ?? "", line, col, preview));
            }
        }

        return new TextHitConnection(nodes, nodes.Count);
    }

    /// <summary>peek → CdpPeekChannel lines with Anchor.</summary>
    public PeekResult Peek(
        [Service] CdpHostRuntime runtime,
        string path,
        int? offset = null,
        int? limit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["path"] = JsonSerializer.SerializeToElement(path),
        };
        if (offset is int o) args["offset"] = JsonSerializer.SerializeToElement(o);
        if (limit is int lim) args["limit"] = JsonSerializer.SerializeToElement(lim);

        var json = CdpPeekChannel.HandleJson(runtime.Session, runtime.Settings.Languages, runtime.HostDeps.DocStore, args);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
        {
            var err = root.TryGetProperty("error", out var e) ? e.GetString() : "peek_failed";
            throw new GraphQLException(err ?? "peek_failed");
        }

        var lines = new List<PeekLineNode>();
        if (root.TryGetProperty("lines", out var linesEl) && linesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in linesEl.EnumerateArray())
            {
                var n = row.TryGetProperty("n", out var ne) && ne.TryGetInt32(out var ni) ? ni : 0;
                var text = row.TryGetProperty("text", out var te) ? te.GetString() ?? "" : "";
                Anchor? anchor = null;
                if (row.TryGetProperty("anchor", out var ae))
                {
                    var wire = ae.ValueKind == JsonValueKind.String ? ae.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(wire))
                        anchor = Anchor.Parse(wire!);
                }
                anchor ??= Anchor.File(path).Line(n);
                lines.Add(new PeekLineNode(n, text, anchor));
            }
        }

        return new PeekResult(path, lines);
    }
}
