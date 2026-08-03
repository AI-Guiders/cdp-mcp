#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent quality|gates — quality_gates soft organ without Cursor MCP. go=quality stays Verb.Go place-only.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteQuality(string raw)
    {
        var work = NormalizeQualityCompound(raw);
        var scope = NormalizeQualityScope(
            ExtractKeyedValue(work, "scope")
            ?? ExtractKeyedValue(work, "scan"));
        var path = ExtractKeyedValue(work, "path");
        var limit = ExtractKeyedValue(work, "limit");

        return new Route(
            Verb.Quality,
            raw,
            Ok: true,
            Scene: scope,
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Detail: string.IsNullOrWhiteSpace(limit) ? null : limit.Trim(),
            Go: "quality");
    }

    static string? NormalizeQualityScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return null;

        return scope.Trim().ToLowerInvariant() switch
        {
            "assertions" or "adx" => "assert",
            "project" or "map" => "disk",
            var s => s
        };
    }

    static string NormalizeQualityCompound(string raw)
    {
        foreach (var (prefix, scope) in QualityCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "quality scope=" + scope;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            var rest = raw[prefix.Length..].TrimStart();
            if (ExtractKeyedValue(raw, "scope") is { Length: > 0 })
                return "quality " + rest;
            return "quality scope=" + scope + (rest.Length > 0 ? " " + rest : "");
        }

        foreach (var prefix in QualityPrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "quality";
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "quality " + raw[prefix.Length..].TrimStart();
        }

        return raw;
    }

    static readonly (string Prefix, string Scope)[] QualityCompounds =
    [
        ("quality_assert", "assert"),
        ("quality_assertions", "assert"),
        ("quality_adx", "assert"),
        ("quality_disk", "disk"),
        ("quality_project", "disk"),
        ("quality_map", "disk"),
        ("gates_assert", "assert"),
        ("gates_disk", "disk")
    ];

    static readonly string[] QualityPrefixes =
    [
        "quality_gates",
        "quality_desk",
        "cdp_quality",
        "gates",
        "quality"
    ];
}
