#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent script|csx|script_scene — ScriptScene habitat without Cursor MCP (go=script place-only).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteScript(string raw)
    {
        var work = NormalizeScriptCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("script ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("csx ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("script_scene ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op) ? "scene" : op.Trim().ToLowerInvariant();
        op = NormalizeScriptOp(op);

        if (!IsScriptOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "script_op_unknown");

        var path = ExtractKeyedValue(work, "path")
            ?? ExtractKeyedValue(work, "name")
            ?? ExtractKeyedValue(work, "file");

        return new Route(
            Verb.Script,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Go: "script");
    }

    /// <summary>Map desk compounds script_put|script_open|… into script + head op.</summary>
    static string NormalizeScriptCompound(string raw)
    {
        foreach (var (prefix, op) in ScriptCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "script " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "script" + rest;
            return "script " + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] ScriptCompounds =
    [
        ("script_scene", "scene"),
        ("script_put", "put"),
        ("script_open", "open"),
        ("script_check", "check"),
        ("script_run", "run"),
        ("script_last", "last"),
        ("script_help", "help"),
        ("script_report", "last")
    ];

    static string NormalizeScriptOp(string op) =>
        op switch
        {
            "map" or "status" or "list" => "scene",
            "new" or "create" => "put",
            "compile" => "check",
            "dryrun" or "dry_run" => "run",
            "report" => "last",
            "of" => "help",
            _ => op
        };

    static bool IsScriptOp(string? op) =>
        op is "scene" or "put" or "open" or "check" or "run" or "last" or "help";
}
