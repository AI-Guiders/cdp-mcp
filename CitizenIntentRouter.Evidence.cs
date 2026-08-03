#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent evidence|cdp_evidence — MetaDispatch cdp_evidence without Cursor MCP (no steal bare report).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteEvidence(string raw)
    {
        var work = NormalizeEvidenceCompound(raw);
        var kind = ExtractKeyedValue(work, "kind")
            ?? ExtractKeyedValue(work, "op")
            ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(kind))
        {
            if (work.StartsWith("evidence ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_evidence ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    kind = head;
            }
        }

        kind = string.IsNullOrWhiteSpace(kind) ? "auto" : kind.Trim().ToLowerInvariant();
        kind = NormalizeEvidenceKind(kind);

        if (!IsEvidenceKind(kind))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "evidence_kind_unknown");

        var text = ExtractKeyedValue(work, "text")
            ?? ExtractKeyedValue(work, "body")
            ?? ExtractKeyedValue(work, "log");
        var path = ExtractKeyedValue(work, "path")
            ?? ExtractKeyedValue(work, "file");

        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(path))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "evidence_input_required");

        return new Route(
            Verb.Evidence,
            raw,
            Ok: true,
            Op: kind,
            Path: path,
            Tool: text,
            Go: "report");
    }

    static string NormalizeEvidenceCompound(string raw)
    {
        foreach (var (prefix, kind) in EvidenceCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "evidence " + kind;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "kind") is { Length: > 0 }
                || ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "evidence" + rest;
            return "evidence " + kind + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Kind)[] EvidenceCompounds =
    [
        ("evidence_build", "build"),
        ("evidence_test", "test"),
        ("evidence_publish", "publish"),
        ("evidence_shell", "shell"),
        ("evidence_csx", "csx"),
        ("evidence_generic", "generic"),
        ("evidence_auto", "auto"),
        ("cdp_evidence_build", "build"),
        ("cdp_evidence_test", "test"),
        ("cdp_evidence_publish", "publish"),
        ("cdp_evidence_shell", "shell"),
        ("cdp_evidence_csx", "csx")
    ];

    static string NormalizeEvidenceKind(string kind) =>
        kind switch
        {
            "dotnet" or "msbuild" or "compile" => "build",
            "unit" or "xunit" or "tests" => "test",
            "pub" or "pack" => "publish",
            "cli" or "cmd" or "pwsh" => "shell",
            "script" or "roslyn" => "csx",
            "other" or "plain" or "raw" => "generic",
            "status" or "scene" => "auto",
            _ => kind
        };

    static bool IsEvidenceKind(string? kind) =>
        kind is "auto" or "build" or "test" or "publish" or "shell" or "csx" or "generic";
}
