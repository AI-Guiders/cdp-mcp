#nullable enable
using System.Text.Json;
using Cdp.ScriptableIde;
using HotChocolate;

namespace CdpMcp.GraphQl;

/// <summary>GraphQL Query root (CDP-ADR-0233). Query-only; mutate stays CSX/edit_plan. Bound via CdpQueryType.</summary>
internal sealed class CdpQueryRoot
{
    /// <summary>textHits → FindInFiles.Hit projection; default first=20 (slim).</summary>
    public TextHitConnection TextHits(
        CdpGraphQlCall call,
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

        var session = call.Session;
        var store = call.DocStore;
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
        CdpGraphQlCall call,
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

        var json = CdpPeekChannel.HandleJson(call.Session, call.Settings.Languages, call.DocStore, args);
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

    /// <summary>diagnostics → IdeProblemsChannel.Row + Anchor.</summary>
    public IReadOnlyList<DiagnosticNode> Diagnostics(
        CdpGraphQlCall call,
        string? path = null,
        int first = 50)
    {
        if (first < 1) first = 1;
        if (first > 200) first = 200;

        var snap = IdeProblemsChannel.Build(call.DocStore, call.Session);
        if (!snap.Ok)
            throw new GraphQLException($"diagnostics_failed: {snap.Pulse}");

        IEnumerable<IdeProblemsChannel.Row> rows = snap.Rows;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var needle = path.Trim();
            rows = rows.Where(r =>
                r.Path.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || r.Anchor.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        return rows
            .Take(first)
            .Select(r => new DiagnosticNode(
                r.Id,
                r.Severity,
                r.Message,
                r.Code,
                r.Path,
                r.Line,
                r.EndLine,
                string.IsNullOrWhiteSpace(r.Anchor) ? Anchor.File(r.Path).Line(Math.Max(1, r.Line)) : Anchor.Parse(r.Anchor),
                r.Stale))
            .ToList();
    }

    /// <summary>goto → GoToAll.Hit + Anchor (cdp_goto engine).</summary>
    public IReadOnlyList<GotoHitNode> Goto(
        CdpGraphQlCall call,
        string query,
        string? kind = null,
        int first = 20)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (first < 1) first = 1;
        if (first > 100) first = 100;

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["query"] = JsonSerializer.SerializeToElement(query),
            ["max"] = JsonSerializer.SerializeToElement(first),
        };
        if (!string.IsNullOrWhiteSpace(kind))
            args["kind"] = JsonSerializer.SerializeToElement(kind.Trim());

        var json = GoToAll.Dispatch(call.DocStore, call.Session, args);
        using var doc = JsonDocument.Parse(json);
        var rootEl = doc.RootElement;
        if (rootEl.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
        {
            var err = rootEl.TryGetProperty("error", out var e) ? e.GetString() : "goto_failed";
            var hint = rootEl.TryGetProperty("hint", out var h) ? h.GetString() : null;
            throw new GraphQLException($"{err}: {hint ?? "GoToAll failed"}");
        }

        var nodes = new List<GotoHitNode>();
        if (rootEl.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array)
        {
            foreach (var hit in hits.EnumerateArray())
            {
                var k = hit.TryGetProperty("kind", out var ke) ? ke.GetString() ?? "" : "";
                var name = hit.TryGetProperty("name", out var ne) ? ne.GetString() ?? "" : "";
                var score = hit.TryGetProperty("score", out var se) && se.TryGetInt32(out var si) ? si : 0;
                var wire = hit.TryGetProperty("anchor", out var ae) ? ae.GetString() : null;
                if (string.IsNullOrWhiteSpace(wire))
                    continue;
                nodes.Add(new GotoHitNode(k, name, score, Anchor.Parse(wire!)));
            }
        }

        return nodes;
    }

    /// <summary>session → SessionContextWire fields (empty≠invent).</summary>
    public SessionNode Session(CdpGraphQlCall call)
    {
        // empty ≠ error: honest null projectRoot when no cdp_open — do not invent or throw.
        var wire = SessionContextWire.From(call.Session);
        return new SessionNode(
            wire.Phase,
            wire.Object,
            wire.Intent,
            wire.Language,
            wire.ProjectRoot,
            wire.ProjectKind,
            wire.SolutionOrProjectPath,
            wire.ScmRoot);
    }

    /// <summary>correspondence → Correspondence.Run docs/reverse with Anchor.</summary>
    public CorrespondenceResult Correspondence(
        CdpGraphQlCall call,
        string? path = null,
        string? anchor = null,
        bool slim = true)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(path))
            args["path"] = JsonSerializer.SerializeToElement(path);
        if (!string.IsNullOrWhiteSpace(anchor))
            args["anchor"] = JsonSerializer.SerializeToElement(anchor);
        if (slim)
            args["slim"] = JsonSerializer.SerializeToElement(true);

        var json = global::CdpMcp.Correspondence.Run(call.DocStore, call.Session, args);
        using var doc = JsonDocument.Parse(json);
        var rootEl = doc.RootElement;
        if (rootEl.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
        {
            var err = rootEl.TryGetProperty("error", out var e) ? e.GetString() : "correspondence_failed";
            var hint = rootEl.TryGetProperty("hint", out var h) ? h.GetString() : null;
            throw new GraphQLException($"{err}: {hint ?? "Correspondence.Run failed"}");
        }

        var pathOut = rootEl.TryGetProperty("path", out var p) ? p.GetString() ?? path ?? "" : path ?? "";
        var ws = rootEl.TryGetProperty("workspace_root", out var w) ? w.GetString() : null;
        var docs = ParseCorrDocs(rootEl, "docs", "forward");
        if (docs.Count == 0)
            docs = ParseCorrDocs(rootEl, "forward", "forward");
        var reverse = ParseCorrDocs(rootEl, "reverse", "reverse");
        return new CorrespondenceResult(pathOut, ws, docs, reverse);
    }

    static List<CorrespondenceDocNode> ParseCorrDocs(JsonElement rootEl, string prop, string defaultRole)
    {
        var list = new List<CorrespondenceDocNode>();
        if (!rootEl.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var row in arr.EnumerateArray())
        {
            var wire = row.TryGetProperty("anchor", out var a) ? a.GetString() : null;
            var path = row.TryGetProperty("path", out var p) ? p.GetString()
                ?? (row.TryGetProperty("doc", out var d) ? d.GetString() : null)
                : null;
            var abs = row.TryGetProperty("abs", out var ab) ? ab.GetString() : null;
            var kind = row.TryGetProperty("kind", out var k) ? k.GetString() : null;
            var title = row.TryGetProperty("title", out var t) ? t.GetString() : null;
            var role = row.TryGetProperty("role", out var r) ? r.GetString() ?? defaultRole : defaultRole;
            if (string.IsNullOrWhiteSpace(wire))
            {
                var locus = abs ?? path;
                if (string.IsNullOrWhiteSpace(locus))
                    continue;
                wire = Anchor.File(locus!).ToWire();
            }
            list.Add(new CorrespondenceDocNode(Anchor.Parse(wire!), path ?? "", abs, kind, title, role));
        }
        return list;
    }

    /// <summary>git → git_git_scene via MetaDispatch (empty≠error: no_project / dispatch missing).</summary>
    public async Task<GitSceneNode> Git(
        CdpGraphQlCall call,
        CancellationToken ct = default)
    {
        var ws = call.Session.ScmRoot ?? call.Session.ProjectRoot;
        // empty≠error: no open project → ok payload with empty hint, not GraphQLException
        if (string.IsNullOrWhiteSpace(ws))
        {
            const string emptyJson =
                """{"ok":true,"schema":"git_scene/v0","empty":true,"hint":"no_project: cdp_open a repo before git query"}""";
            return new GitSceneNode("git_scene/v0", true, emptyJson);
        }
        if (call.DispatchToolAsync is null)
            throw new GraphQLException("dispatch_unavailable: MetaDispatch must supply DispatchToolAsync for git");

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["workspace_path"] = JsonSerializer.SerializeToElement(ws),
        };
        var json = await call.DispatchToolAsync("git_git_scene", args, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var rootEl = doc.RootElement;
        var ok = !(rootEl.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False);
        if (!ok)
        {
            var err = rootEl.TryGetProperty("error", out var e) ? e.GetString() : "git_failed";
            throw new GraphQLException($"{err}: git_git_scene failed");
        }
        var schema = rootEl.TryGetProperty("schema", out var s) ? s.GetString() ?? "git_scene/v0" : "git_scene/v0";
        return new GitSceneNode(schema, true, json);
    }

    /// <summary>knowledge → memory_world_recall_knowledge parity (empty hits OK when total=0).</summary>
    public async Task<KnowledgeRecallResult> Knowledge(
        CdpGraphQlCall call,
        string query,
        string? layer = null,
        int first = 15,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (first < 1) first = 1;
        if (first > 50) first = 50;
        if (call.DispatchToolAsync is null)
            throw new GraphQLException("dispatch_unavailable: MetaDispatch must supply DispatchToolAsync for knowledge");

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["query"] = JsonSerializer.SerializeToElement(query),
            ["limit"] = JsonSerializer.SerializeToElement(first),
        };
        if (!string.IsNullOrWhiteSpace(layer))
            args["layer"] = JsonSerializer.SerializeToElement(layer.Trim());
        if (!string.IsNullOrWhiteSpace(call.Session.ProjectRoot))
            args["workspace_path"] = JsonSerializer.SerializeToElement(call.Session.ProjectRoot);

        var json = await call.DispatchToolAsync("memory_world_recall_knowledge", args, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var rootEl = doc.RootElement;
        if (rootEl.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
        {
            var err = rootEl.TryGetProperty("error", out var e) ? e.GetString() : "knowledge_failed";
            throw new GraphQLException($"{err}: recall failed");
        }

        var total = rootEl.TryGetProperty("total", out var tot) && tot.TryGetInt32(out var ti) ? ti : 0;
        var layers = new List<string>();
        if (rootEl.TryGetProperty("layers_used", out var lu) && lu.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in lu.EnumerateArray())
            {
                var s = x.GetString();
                if (!string.IsNullOrWhiteSpace(s)) layers.Add(s!);
            }
        }

        var hits = new List<KnowledgeHitNode>();
        if (rootEl.TryGetProperty("hits", out var hitsEl) && hitsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var hit in hitsEl.EnumerateArray())
            {
                var path = hit.TryGetProperty("path", out var p) ? p.GetString() : null;
                var title = hit.TryGetProperty("title", out var t) ? t.GetString() : null;
                var preview = hit.TryGetProperty("preview", out var pr) ? pr.GetString()
                    ?? (hit.TryGetProperty("excerpt", out var ex) ? ex.GetString() : null)
                    : null;
                double? score = hit.TryGetProperty("score", out var sc) && sc.TryGetDouble(out var sd) ? sd : null;
                Anchor? anchor = null;
                if (hit.TryGetProperty("anchor", out var ae))
                {
                    var wire = ae.ValueKind == JsonValueKind.String ? ae.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(wire))
                        anchor = Anchor.Parse(wire!);
                }
                else if (!string.IsNullOrWhiteSpace(path))
                {
                    anchor = Anchor.File(path!);
                }
                hits.Add(new KnowledgeHitNode(path, title, preview, score, anchor));
            }
        }

        return new KnowledgeRecallResult(query, total, layers, hits, json);
    }
}
