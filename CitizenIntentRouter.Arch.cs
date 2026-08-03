#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent arch|arch_desk|cdp_arch|board — IdeArchBoardChannel without Cursor MCP (go=arch_desk place-only).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteArch(string raw)
    {
        var work = NormalizeArchCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("arch ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("arch_desk ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("arch_board ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_arch ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("board ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("sketch_desk ", StringComparison.OrdinalIgnoreCase))
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
        op = NormalizeArchOp(op);

        if (!IsArchOp(op))
            return new Route(Verb.Arch, raw, Ok: false, Reason: "arch_op_unknown");

        var path = ExtractKeyedValue(work, "id")
            ?? ExtractKeyedValue(work, "role_id")
            ?? ExtractKeyedValue(work, "role")
            ?? ExtractKeyedValue(work, "focus")
            ?? ExtractKeyedValue(work, "candidate")
            ?? ExtractKeyedValue(work, "profile")
            ?? PositionalArchId(work, op);

        return new Route(
            Verb.Arch,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Go: "arch_desk");
    }

    static string? PositionalArchId(string work, string op)
    {
        if (op is not ("add_role" or "elect" or "reject" or "promote" or "as_built" or "scene"))
            return null;

        var sp = work.IndexOf(' ');
        if (sp < 0) return null;
        var rest = work[(sp + 1)..].Trim();
        if (rest.StartsWith(op + " ", StringComparison.OrdinalIgnoreCase))
            rest = rest[(op.Length + 1)..].Trim();
        else if (rest.Equals(op, StringComparison.OrdinalIgnoreCase))
            return null;

        var headSp = rest.IndexOf(' ');
        var head = headSp < 0 ? rest : rest[..headSp];
        if (head.Length == 0 || head.Contains('=', StringComparison.Ordinal))
            return null;
        return head.Trim().Trim('"');
    }

    static string NormalizeArchCompound(string raw)
    {
        foreach (var (prefix, op) in ArchCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "arch " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "arch" + rest;
            return "arch " + op + rest;
        }

        foreach (var alias in ArchAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase))
                return "arch";
            if (raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return "arch " + raw[alias.Length..].TrimStart();
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] ArchCompounds =
    [
        ("arch_scene", "scene"),
        ("arch_roles", "roles"),
        ("arch_clear", "clear"),
        ("arch_promote", "promote"),
        ("arch_as_built", "as_built"),
        ("cdp_arch_scene", "scene"),
        ("cdp_arch_roles", "roles"),
        ("cdp_arch_as_built", "as_built"),
        ("board_scene", "scene")
    ];

    static readonly string[] ArchAliases =
    [
        "board",
        "cdp_arch",
        "arch_desk",
        "arch_board",
        "sketch_desk"
    ];

    static string NormalizeArchOp(string op) =>
        op switch
        {
            "desk" or "status" or "a" or "map" or "pulse" => "scene",
            "role" or "add" => "add_role",
            "candidates" or "candidate" => "add_candidates",
            "bind" or "choose" => "elect",
            "drop_candidate" => "reject",
            "wire" or "link" => "edge",
            "asbuilt" or "built" or "scan" => "as_built",
            "lexicon" => "roles",
            _ => op
        };

    static bool IsArchOp(string? op) =>
        op is "scene" or "add_role" or "add_candidates" or "elect" or "reject"
            or "edge" or "promote" or "clear" or "as_built" or "roles";
}
