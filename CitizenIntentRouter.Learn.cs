#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent learn_desk|cdp_learn — Lean learning desk without Cursor MCP. go=learn stays Verb.Go; not stolen.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteLearn(string raw)
    {
        var work = NormalizeLearnCompound(raw);
        var opRaw = ExtractKeyedValue(work, "op")
            ?? ExtractLearnPositionalOp(work)
            ?? "scene";
        var op = NormalizeLearnOp(opRaw.Trim().ToLowerInvariant());
        if (!IsLearnOp(op))
            return new Route(Verb.Learn, raw, Ok: false, Reason: "learn_op_unknown");

        return new Route(
            Verb.Learn,
            raw,
            Ok: true,
            Op: op,
            Scene: ExtractKeyedValue(work, "title") ?? ExtractKeyedValue(work, "name"),
            Path: ExtractKeyedValue(work, "path") ?? ExtractKeyedValue(work, "file_path"),
            Organ: ExtractKeyedValue(work, "id"),
            Detail: ExtractKeyedValue(work, "topic"),
            Go: "learn");
    }

    static string? ExtractLearnPositionalOp(string work)
    {
        var rest = work.StartsWith("learn ", StringComparison.OrdinalIgnoreCase)
            ? work["learn ".Length..].Trim()
            : work;
        if (string.IsNullOrWhiteSpace(rest) || rest.Contains('=', StringComparison.Ordinal))
            return null;
        var token = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        return IsLearnOp(NormalizeLearnOp(token.ToLowerInvariant())) ? token : null;
    }

    static string NormalizeLearnCompound(string raw)
    {
        foreach (var prefix in LearnPrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "learn";
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "learn " + raw[prefix.Length..].TrimStart();
        }

        return raw;
    }

    static string NormalizeLearnOp(string op) =>
        op switch
        {
            "help" or "status" => "scene",
            "capture" or "note" or "write" => "stash",
            "get" or "peek" => "recall",
            "export" => "promote",
            _ => op
        };

    static bool IsLearnOp(string? op) =>
        op is "scene" or "stash" or "list" or "recall" or "promote";

    static readonly string[] LearnPrefixes =
    [
        "cdp_learn",
        "learn_desk",
        "learning",
        "learn"
    ];
}
