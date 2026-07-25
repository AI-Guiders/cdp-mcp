using System.Text.Json;
using Cdp.Core;
using CdpMcp.Backends;

namespace CdpMcp;

/// <summary>
/// Code Analysis domain scene — peer of <c>git_scene</c> / <c>test_scene</c>.
/// On demand; feature menu grows in-domain (correspondence, semantic_map, clones).
/// </summary>
internal static class AnalysisScene
{
    public const string Schema = "analysis_scene/v0";
    public const string ToolName = "cdp_analysis_scene";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static bool IsAnalysisTool(string name) =>
        string.Equals(name, ToolName, StringComparison.OrdinalIgnoreCase);

    public static Task<string> DispatchAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken ct = default)
    {
        var feature = (OptString(args, "feature") ?? OptString(args, "op") ?? "").Trim().ToLowerInvariant();
        if (feature is "" or "scene" or "map" or "status")
            return Task.FromResult(SceneMap(session));

        return feature switch
        {
            "clones" or "clone" or "duplicates" or "code_clones" =>
                Task.FromResult(CodeClones.Run(store, session, args)),
            "correspondence" or "corr" or "docs" or "adr_map" or "context" =>
                Task.FromResult(Correspondence.Run(store, session, args)),
            "semantic_map" or "semantic" or "related" or "nav_map" =>
                SemanticMap.RunAsync(store, session, byDomain, args, ct),
            _ => Task.FromResult(JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                error = "unknown_feature",
                feature,
                hint = "feature omit → scene map; feature=correspondence|semantic_map|clones"
            }, Pretty))
        };
    }

    static string SceneMap(SessionContext session)
    {
        var hasProject = session.ProjectRoot is { Length: > 0 };
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            scene = "analysis",
            pulse = hasProject ? "analysis ready" : "no project — cdp_open first",
            next = hasProject
                ? (object[])
                [
                    new
                    {
                        go = "analysis_scene",
                        label = "Correspondence",
                        why = "path= → ADR/docs + reverse (toml|doc_body)",
                        go_args = new { feature = "correspondence" }
                    },
                    new
                    {
                        go = "analysis_scene",
                        label = "Semantic map",
                        why = "related neighbors around path=",
                        go_args = new { feature = "semantic_map", mode = "related" }
                    },
                    new
                    {
                        go = "analysis_scene",
                        label = "Clones in file",
                        why = "go_args: { feature:\"clones\", scope:\"file\" }",
                        go_args = new { feature = "clones", scope = "file" }
                    }
                ]
                : (object[])
                [
                    new { go = "project_scene", label = "Open project", why = "cdp_open / project_scene first" }
                ],
            features = (object[])
            [
                new
                {
                    id = "correspondence",
                    title = "Doc↔code correspondence (L1 + doc_body)",
                    hint =
                        "Forward ADR/feature + reverse from workspace.toml and ADR prose. " +
                        "Unified context= embedded. path= or open buffer.",
                    go_args = new { feature = "correspondence" }
                },
                new
                {
                    id = "semantic_map",
                    title = "Semantic / related map",
                    hint =
                        "Roslyn navigation neighbors around a file. mode=related|…; path=/anchor=; results=anchors.",
                    go_args = new { feature = "semantic_map", mode = "related" }
                },
                new
                {
                    id = "clones",
                    title = "Code clones (VS-style)",
                    hint =
                        "Structural duplicates: exact / strong. Results = anchors, not paths. " +
                        "scope=file|method|selection|project|solution; optional anchor=/from= seed.",
                    go_args = new { feature = "clones", scope = "file" }
                }
            ],
            hint =
                "Domain scene (not MFD). feature=correspondence|semantic_map|clones; project-aware when overlays/solution exist."
        }, Pretty);
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
