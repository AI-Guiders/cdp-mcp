#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent refactor_plan|cdp_refactor — decide-before-cut without Cursor MCP. go=refactor_plan stays Verb.Go; not stolen.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteRefactor(string raw)
    {
        var work = NormalizeRefactorCompound(raw);
        var opRaw = ExtractKeyedValue(work, "op")
            ?? ExtractRefactorPositionalOp(work)
            ?? "plan";
        var op = NormalizeRefactorOp(opRaw.Trim().ToLowerInvariant());
        if (!IsRefactorOp(op))
            return new Route(Verb.Refactor, raw, Ok: false, Reason: "refactor_op_unknown");

        return new Route(
            Verb.Refactor,
            raw,
            Ok: true,
            Op: op,
            Path: ExtractKeyedValue(work, "path")
                ?? ExtractKeyedValue(work, "file_path")
                ?? ExtractKeyedValue(work, "locus"),
            Detail: ExtractKeyedValue(work, "scope") ?? ExtractKeyedValue(work, "max"),
            Go: "refactor_plan");
    }

    static string? ExtractRefactorPositionalOp(string work)
    {
        var rest = work.StartsWith("refactor ", StringComparison.OrdinalIgnoreCase)
            ? work["refactor ".Length..].Trim()
            : work;
        if (string.IsNullOrWhiteSpace(rest) || rest.Contains('=', StringComparison.Ordinal))
            return null;
        var token = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        return IsRefactorOp(NormalizeRefactorOp(token.ToLowerInvariant())) ? token : null;
    }

    static string NormalizeRefactorCompound(string raw)
    {
        foreach (var prefix in RefactorPrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "refactor";
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "refactor " + raw[prefix.Length..].TrimStart();
        }

        return raw;
    }

    static string NormalizeRefactorOp(string op) =>
        op switch
        {
            "help" or "status" or "scene" => "plan",
            "map" or "hotspots" => "debt",
            "what_if" => "budget",
            "radius" => "blast",
            "seam" => "partials",
            "next_cut" or "cut" => "recommend",
            _ => op
        };

    static bool IsRefactorOp(string? op) =>
        op is "plan" or "debt" or "budget" or "blast" or "partials" or "recommend" or "pulse";

    static readonly string[] RefactorPrefixes =
    [
        "cdp_refactor",
        "refactor_plan",
        "debt_scene",
        "refactor"
    ];
}
