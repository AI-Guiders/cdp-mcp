#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent analysis|cdp_analysis_scene — AnalysisScene without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteAnalysis(string raw)
    {
        var work = NormalizeAnalysisCompound(raw);
        var feature = ExtractKeyedValue(work, "feature")
            ?? ExtractKeyedValue(work, "op")
            ?? ExtractKeyedValue(work, "cmd");

        if (string.IsNullOrWhiteSpace(feature))
        {
            if (work.StartsWith("analysis ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("analysis_scene ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_analysis_scene ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_analysis ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    feature = head;
            }
        }

        feature = string.IsNullOrWhiteSpace(feature) ? "map" : feature.Trim().ToLowerInvariant();
        feature = NormalizeAnalysisFeature(feature);

        if (!IsAnalysisFeature(feature))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "analysis_feature_unknown");

        var path = ExtractKeyedValue(work, "path")
            ?? ExtractKeyedValue(work, "file_path")
            ?? ExtractKeyedValue(work, "file");
        var mode = ExtractKeyedValue(work, "mode");
        var scope = ExtractKeyedValue(work, "scope");
        var anchor = ExtractKeyedValue(work, "anchor")
            ?? ExtractKeyedValue(work, "from");

        return new Route(
            Verb.Analysis,
            raw,
            Ok: true,
            Op: feature,
            Path: path,
            Tool: mode ?? scope,
            NewString: anchor,
            Go: "analysis_scene");
    }

    static string NormalizeAnalysisCompound(string raw)
    {
        foreach (var (prefix, inject) in AnalysisCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return inject.Length == 0 ? "analysis" : "analysis " + inject;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..].TrimStart();
            if (inject.Length == 0)
                return "analysis " + rest;
            if (ExtractKeyedValue(raw, "feature") is { Length: > 0 }
                || ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "analysis " + rest;
            return "analysis " + inject + " " + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Inject)[] AnalysisCompounds =
    [
        ("analysis_desk", ""),
        ("analysis_scene", ""),
        ("analysis_map", "feature=map"),
        ("analysis_clones", "feature=clones"),
        ("analysis_correspondence", "feature=correspondence"),
        ("analysis_corr", "feature=correspondence"),
        ("analysis_semantic", "feature=semantic_map"),
        ("analysis_semantic_map", "feature=semantic_map"),
        ("cdp_analysis_scene", ""),
        ("cdp_analysis", ""),
        ("cdp_analysis_clones", "feature=clones"),
        ("cdp_analysis_correspondence", "feature=correspondence"),
        ("cdp_analysis_semantic", "feature=semantic_map")
    ];

    static string NormalizeAnalysisFeature(string feature) =>
        feature switch
        {
            "desk" or "status" or "show" or "pulse" or "scene" or "" => "map",
            "clone" or "duplicates" or "code_clones" => "clones",
            "corr" or "docs" or "adr_map" or "context" => "correspondence",
            "noadr" or "skip_adr" => "no_adr",
            "semantic" or "related" or "nav_map" => "semantic_map",
            _ => feature
        };

    static bool IsAnalysisFeature(string? feature) =>
        feature is "map" or "clones" or "correspondence" or "semantic_map" or "no_adr";
}
