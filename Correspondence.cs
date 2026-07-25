#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// Analysis feature: documentation↔code correspondence (L1 from <c>.cascade/workspace.toml</c>).
/// Results = doc anchors + reverse code wires — not table rows.
/// </summary>
internal static class Correspondence
{
    public const string Schema = "correspondence/v0";
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string Run(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var rootHint = session.ScmRoot ?? session.ProjectRoot;
        var pathArg = OptString(args, "path") ?? OptString(args, "file");
        var wire = OptString(args, "anchor") ?? OptString(args, "from") ?? OptString(args, "at");

        string? abs = null;
        if (pathArg is { Length: > 0 })
            abs = ResolvePath(session, pathArg);
        else if (wire is { Length: > 0 } && TryFileFromWire(wire, session, out var fromWire))
            abs = fromWire;
        else if (store.All.FirstOrDefault() is { Path.Length: > 0 } doc)
            abs = doc.Path;

        if (abs is null)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                feature = "correspondence",
                error = "path_required",
                hint = "path= file under open project, or open a buffer, or anchor=[F:…]"
            }, Pretty);
        }

        var result = WorkspaceCorrespondence.TryResolve(abs, rootHint);
        if (result is null)
        {
            var walked = WorkspaceCorrespondence.FindWorkspaceRoot(abs, rootHint);
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                feature = "correspondence",
                error = walked is null ? "no_workspace_toml" : "resolve_failed",
                path = abs,
                workspace_root = walked,
                hint = "Need .cascade/workspace.toml (CIDE ADR map). Open cascade-ide or another marked repo."
            }, Pretty);
        }

        var docAnchors = result.ForwardDocs
            .Select(d => new
            {
                anchor = $"[F:{d.Path}]",
                path = d.Path,
                title = d.Title,
                role = "forward"
            })
            .ToArray();

        var reverse = result.ReverseAnchors
            .Select(r => new
            {
                anchor = r.Wire,
                doc = r.DocPath,
                title = r.DocTitle,
                kind = r.Kind,
                provenance = r.Provenance,
                doc_line = r.DocLineHint,
                excerpt = r.Excerpt,
                role = "reverse"
            })
            .ToArray();

        object? land = null;
        if (result.ForwardDocs.Length > 0)
        {
            var first = result.ForwardDocs[0];
            var absDoc = Path.Combine(result.WorkspaceRoot, first.Path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(absDoc))
            {
                try
                {
                    var text = File.ReadAllText(absDoc);
                    var lines = text.Replace("\r\n", "\n").Split('\n');
                    var peek = string.Join("\n", lines.Take(12));
                    var opened = store.Open(absDoc, refresh: false);
                    land = new
                    {
                        anchor = $"[F:{first.Path}]",
                        doc_id = opened.DocId,
                        title = first.Title,
                        start_line = 1,
                        end_line = Math.Min(12, lines.Length),
                        text = peek
                    };
                }
                catch { /* peek best-effort */ }
            }
        }

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            feature = "correspondence",
            file = result.FileRel,
            workspace_root = result.WorkspaceRoot,
            toml = result.TomlPath,
            layers = result.ActiveLayers,
            feature_line = result.FeatureLine,
            adr_line = result.AdrLine,
            forward_docs = docAnchors,
            reverse_anchors = reverse,
            context = WorkspaceCorrespondence.BuildContext(result),
            count = docAnchors.Length + reverse.Length,
            land,
            next = new object[]
            {
                new { go = "peek", label = "Peek ADR", why = "wire= from forward_docs[].anchor" },
                new { go = "scope", label = "Sniper reverse", why = "from= reverse_anchors[].anchor" },
                new { go = "semantic_map", label = "Semantic map", why = "related neighbors same path" },
                new { go = "goto", label = "Go To", why = "jump related type/file" }
            },
            hint =
                "L1 correspondence: forward ADR/docs; reverse = workspace_toml | bracket | doc_body. " +
                "context= unified get_correspondence_context shape. Anchors not table rows."
        }, Pretty);
    }

    static string ResolvePath(SessionContext session, string pathArg)
    {
        if (Path.IsPathRooted(pathArg))
            return Path.GetFullPath(pathArg);
        var root = session.ScmRoot ?? session.ProjectRoot ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(root, pathArg));
    }

    static bool TryFileFromWire(string wire, SessionContext session, out string abs)
    {
        abs = "";
        var raw = wire.Trim().Trim('[', ']');
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!part.StartsWith("F:", StringComparison.OrdinalIgnoreCase))
                continue;
            abs = ResolvePath(session, part[2..].Trim());
            return true;
        }

        return false;
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
