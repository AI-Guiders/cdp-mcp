using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Code Analysis domain scene — peer of <c>git_scene</c> / <c>test_scene</c>.
/// On demand; feature menu grows in-domain (clones first).
/// </summary>
internal static class AnalysisScene
{
    public const string Schema = "analysis_scene/v0";
    public const string ToolName = "cdp_analysis_scene";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static bool IsAnalysisTool(string name) =>
        string.Equals(name, ToolName, StringComparison.OrdinalIgnoreCase);

    public static string Dispatch(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var feature = (OptString(args, "feature") ?? OptString(args, "op") ?? "").Trim().ToLowerInvariant();
        if (feature is "" or "scene" or "map" or "status")
            return SceneMap(session);

        return feature switch
        {
            "clones" or "clone" or "duplicates" or "code_clones" =>
                CodeClones.Run(store, session, args),
            "correspondence" or "corr" or "docs" or "adr_map" =>
                Correspondence.Run(store, session, args),
            _ => JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = false,
                error = "unknown_feature",
                feature,
                hint = "feature omit → scene map; feature=clones|correspondence"
            }, Pretty)
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
                        why = "path= → ADR/docs + reverse anchors",
                        go_args = new { feature = "correspondence" }
                    },
                    new
                    {
                        go = "analysis_scene",
                        label = "Clones in file",
                        why = "go_args: { feature:\"clones\", scope:\"file\", path?:\"…\" }",
                        go_args = new { feature = "clones", scope = "file" }
                    },
                    new
                    {
                        go = "analysis_scene",
                        label = "Clones in project",
                        why = "min 10 statements (VS Analyze Solution analogue)",
                        go_args = new { feature = "clones", scope = "project" }
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
                    title = "Doc↔code correspondence (L1)",
                    hint =
                        "Forward ADR/feature docs + reverse code_anchors from .cascade/workspace.toml. " +
                        "Results = anchors. path= or open buffer.",
                    go_args = new { feature = "correspondence" }
                },
                new
                {
                    id = "clones",
                    title = "Code clones (VS-style)",
                    hint =
                        "Structural duplicates: exact / strong. Results = anchors, not paths. " +
                        "scope=file|method|selection|project|solution; optional anchor=/from= seed.",
                    go_args = new { feature = "clones", scope = hasProject ? "file" : "file" }
                }
            ],
            hint =
                "Domain scene (not MFD). feature=correspondence|clones; more analysis features land here."
        }, Pretty);
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
