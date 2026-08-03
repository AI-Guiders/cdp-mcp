#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent problems|errlist|errorlist — IdeProblemsChannel (go=problems; no steal bare row/aim).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteProblems(string raw)
    {
        var work = NormalizeProblemsCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("problems ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("problem ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("errlist ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("errorlist ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op) ? "list" : op.Trim().ToLowerInvariant();
        op = NormalizeProblemsOp(op);

        if (!IsProblemsOp(op))
            return new Route(Verb.Problems, raw, Ok: false, Reason: "problems_op_unknown");

        var path = ExtractKeyedValue(work, "row")
            ?? ExtractKeyedValue(work, "id")
            ?? ExtractKeyedValue(work, "pick")
            ?? ExtractKeyedValue(work, "wire")
            ?? ExtractKeyedValue(work, "anchor")
            ?? ExtractKeyedValue(work, "at");

        return new Route(
            Verb.Problems,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Go: "problems");
    }

    static string NormalizeProblemsCompound(string raw)
    {
        foreach (var (prefix, op) in ProblemsCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "problems " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "problems" + rest;
            return "problems " + op + rest;
        }

        foreach (var alias in ProblemsAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase))
                return "problems";
            if (raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return "problems " + raw[alias.Length..].TrimStart();
        }

        if (raw.Equals("problems", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("problems ", StringComparison.OrdinalIgnoreCase))
            return raw;

        return raw;
    }

    static readonly (string Prefix, string Op)[] ProblemsCompounds =
    [
        ("problems_scene", "scene"),
        ("problems_list", "list"),
        ("problem_list", "list"),
        ("errlist_list", "list"),
        ("errorlist_list", "list")
    ];

    static readonly string[] ProblemsAliases =
    [
        "problem",
        "errlist",
        "errorlist"
    ];

    static string NormalizeProblemsOp(string op) =>
        op switch
        {
            "desk" or "status" or "help" or "a" or "map" => "scene",
            _ => op
        };

    static bool IsProblemsOp(string? op) =>
        op is "scene" or "list";

    static bool IsProblemsIntent(string raw)
    {
        if (raw.Equals("problems", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("problems ", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var alias in ProblemsAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var (prefix, _) in ProblemsCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
