#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent peel|cdp_peel — IdePeelChannel without Cursor MCP (go=peel place-only when bare).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RoutePeel(string raw)
    {
        var work = NormalizePeelCompound(raw);
        var path = ExtractKeyedValue(work, "path")
            ?? ExtractKeyedValue(work, "file_path")
            ?? ExtractKeyedValue(work, "file");
        var members = ExtractKeyedValue(work, "members")
            ?? ExtractKeyedValue(work, "member_names")
            ?? ExtractKeyedValue(work, "member");
        var output = ExtractKeyedValue(work, "out")
            ?? ExtractKeyedValue(work, "output")
            ?? ExtractKeyedValue(work, "output_file_path");
        var applyRaw = ExtractKeyedValue(work, "apply");

        var hasAct = !string.IsNullOrWhiteSpace(path)
            || !string.IsNullOrWhiteSpace(members)
            || !string.IsNullOrWhiteSpace(output);

        string op;
        if (!hasAct)
            op = "place";
        else if (applyRaw is not null
            && (applyRaw.Equals("true", StringComparison.OrdinalIgnoreCase)
                || applyRaw.Equals("1", StringComparison.OrdinalIgnoreCase)
                || applyRaw.Equals("yes", StringComparison.OrdinalIgnoreCase)))
            op = "apply";
        else
            op = "preview";

        if (hasAct && (string.IsNullOrWhiteSpace(path)
            || string.IsNullOrWhiteSpace(members)
            || string.IsNullOrWhiteSpace(output)))
        {
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "peel_args_incomplete");
        }

        return new Route(
            Verb.Peel,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Tool: members,
            NewString: output,
            Go: "peel");
    }

    static string NormalizePeelCompound(string raw)
    {
        foreach (var (prefix, inject) in PeelCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return inject.Length == 0 ? "peel" : "peel " + inject;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..].TrimStart();
            if (inject.Length == 0)
                return "peel " + rest;
            if (ExtractKeyedValue(raw, "apply") is { Length: > 0 } || rest.Contains("apply=", StringComparison.OrdinalIgnoreCase))
                return "peel " + rest;
            return "peel " + inject + " " + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Inject)[] PeelCompounds =
    [
        ("peel_desk", ""),
        ("peel_preview", "apply=false"),
        ("peel_apply", "apply=true"),
        ("cdp_peel_preview", "apply=false"),
        ("cdp_peel_apply", "apply=true")
    ];
}