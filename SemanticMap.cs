#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.Backends;

namespace CdpMcp;

/// <summary>
/// Analysis feature: semantic / related map around a file (Roslyn navigation context).
/// Results = path highlights + optional land — project-aware for any open .sln/.csproj.
/// </summary>
internal static partial class SemanticMap
{
    public const string Schema = "semantic_map/v0";
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static async Task<string> RunAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken ct = default)
    {
        if (!byDomain.TryGetValue(CdpDomains.Roslyn, out var roslyn) || !roslyn.IsEnabled)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                feature = "semantic_map",
                error = "roslyn_unavailable",
                hint = "Need Roslyn backend + cdp_open .sln/.csproj"
            }, Pretty);
        }

        var solution = session.SolutionOrProjectPath;
        if (string.IsNullOrWhiteSpace(solution))
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                feature = "semantic_map",
                error = "no_solution",
                hint = "cdp_open a .sln/.csproj first"
            }, Pretty);
        }

        var pathArg = OptString(args, "path") ?? OptString(args, "file");
        var wire = OptString(args, "anchor") ?? OptString(args, "from") ?? OptString(args, "at");
        string? abs = null;
        if (pathArg is { Length: > 0 })
            abs = ResolvePath(session, pathArg);
        else if (wire is { Length: > 0 } && TryFileFromWire(wire, session, out var fromWire))
            abs = fromWire;
        else if (store.All.FirstOrDefault() is { Path.Length: > 0 } doc)
            abs = doc.Path;

        if (abs is null || !File.Exists(abs))
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                feature = "semantic_map",
                error = "path_required",
                hint = "path= file in project, or open buffer, or anchor=[F:…]"
            }, Pretty);
        }

        var mode = (OptString(args, "mode") ?? "related").Trim();
        if (mode.Length == 0) mode = "related";
        var maxRelated = IntOr(args, "max_related", 24);

        int? line = IntOrNull(args, "line") ?? IntOrNull(args, "start_line");
        int? column = IntOrNull(args, "column") ?? IntOrNull(args, "start_column");
        if (wire is { Length: > 0 })
            TryLineFromWire(wire, ref line);

        var roslynArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["solution_or_project_path"] = JsonSerializer.SerializeToElement(solution),
            ["file_path"] = JsonSerializer.SerializeToElement(abs),
            ["mode"] = JsonSerializer.SerializeToElement(mode)
        };
        if (line is { } l) roslynArgs["line"] = JsonSerializer.SerializeToElement(l);
        if (column is { } c) roslynArgs["column"] = JsonSerializer.SerializeToElement(c);
        if (OptString(args, "preset") is { Length: > 0 } preset)
            roslynArgs["preset"] = JsonSerializer.SerializeToElement(preset);
        if (maxRelated > 0)
            roslynArgs["max_related"] = JsonSerializer.SerializeToElement(maxRelated);

        string raw;
        try
        {
            raw = await roslyn.CallAsync("roslyn_get_workspace_navigation_context", roslynArgs)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                feature = "semantic_map",
                error = "roslyn_failed",
                message = ex.Message,
                path = abs,
                mode
            }, Pretty);
        }

        var anchor = new CodeAnchor(abs, line, column, solution);
        var report = IdeReportBuilder.FromSemanticMapRelated(anchor, raw, mode);
        var hits = BuildHits(raw, session, max: 40);

        object? land = null;
        if (hits.Count > 0)
        {
            var top = hits[0];
            if (File.Exists(top.abs))
            {
                try
                {
                    var buf = store.Open(top.abs, refresh: false);
                    var lines = buf.Text.Replace("\r\n", "\n").Split('\n');
                    var start = Math.Clamp(top.line ?? 1, 1, Math.Max(1, lines.Length));
                    var end = Math.Min(lines.Length, start + 8);
                    var peek = string.Join("\n", lines.Skip(start - 1).Take(end - start + 1));
                    land = new
                    {
                        anchor = top.anchor,
                        doc_id = buf.DocId,
                        why = top.why,
                        start_line = start,
                        end_line = end,
                        text = peek
                    };
                }
                catch { /* best effort */ }
            }
        }

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            feature = "semantic_map",
            mode,
            file = Rel(session, abs),
            available = report.Available,
            summary = report.Summary,
            hits = hits.Select(h => new { h.anchor, path = h.rel, h.why, line = h.line }).ToArray(),
            count = hits.Count,
            land,
            report = JsonSerializer.Deserialize<JsonElement>(report.ToJson()),
            next = new object[]
            {
                new { go = "goto", label = "Go To neighbor", why = "query= from hits" },
                new { go = "correspondence", label = "Correspondence", why = "same path → ADR/docs" },
                new { go = "scope", label = "Sniper", why = "from= hits[].anchor" },
                new { go = "analysis_scene", label = "Widen mode", why = "mode=subgraph|related" }
            },
            hint =
                "Project-aware related map (Roslyn). path=/anchor= + optional mode=/line=/preset=. " +
                "Hits are anchors — not a path table."
        }, Pretty);
    }

}
