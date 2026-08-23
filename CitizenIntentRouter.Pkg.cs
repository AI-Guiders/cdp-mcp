#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent pkg|nuget — cdp_pkg_* without Cursor MCP (list|find|add|remove|update|outdated).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RoutePkg(string raw)
    {
        var work = NormalizePkgCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("pkg ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("nuget ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("packages ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("package ", StringComparison.OrdinalIgnoreCase))
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
        op = NormalizePkgOp(op);

        if (!IsPkgOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "pkg_op_unknown");

        var path = ExtractKeyedValue(work, "path");
        var id = ExtractKeyedValue(work, "id")
            ?? ExtractKeyedValue(work, "package")
            ?? ExtractKeyedValue(work, "name");
        var query = ExtractKeyedValue(work, "query")
            ?? ExtractKeyedValue(work, "q")
            ?? ExtractKeyedValue(work, "search");
        var version = ExtractKeyedValue(work, "version")
            ?? ExtractKeyedValue(work, "ver");
        var take = ExtractKeyedValue(work, "take")
            ?? ExtractKeyedValue(work, "limit");
        var root = ExtractKeyedValue(work, "root");

        // Positional: pkg find Newtonsoft · pkg add Some.Package
        if (string.IsNullOrWhiteSpace(query) && op is "find")
            query = TryPositionalAfterOp(work, "find");
        if (string.IsNullOrWhiteSpace(id) && op is "add" or "remove" or "update" or "latest")
            id = TryPositionalAfterOp(work, op);

        if (op is "find" && string.IsNullOrWhiteSpace(query))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "pkg_query_required");
        if ((op is "add" or "remove" or "update") && string.IsNullOrWhiteSpace(id))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "pkg_id_required");
        if (op is "latest" && string.IsNullOrWhiteSpace(id))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "pkg_id_required");

        return new Route(
            Verb.Pkg,
            raw,
            Ok: true,
            Op: op,
            Path: path ?? root,
            Tool: id,
            Scene: query,
            Detail: version ?? take,
            Go: "pkg");
    }

    static string? TryPositionalAfterOp(string work, string opHead)
    {
        var marker = " " + opHead + " ";
        var idx = work.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;
        var rest = work[(idx + marker.Length)..].Trim();
        if (rest.Length == 0 || rest.Contains('=', StringComparison.Ordinal))
            return null;
        var sp = rest.IndexOf(' ');
        return sp < 0 ? rest : rest[..sp];
    }

    static string NormalizePkgCompound(string raw)
    {
        foreach (var (prefix, op) in PkgCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "pkg " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "pkg" + rest;
            return "pkg " + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] PkgCompounds =
    [
        ("pkg_list", "list"),
        ("pkg_find", "find"),
        ("pkg_add", "add"),
        ("pkg_remove", "remove"),
        ("pkg_update", "update"),
        ("pkg_outdated", "outdated"),
        ("nuget_list", "list"),
        ("nuget_find", "find"),
        ("nuget_add", "add"),
        ("nuget_remove", "remove"),
        ("nuget_update", "update"),
        ("nuget_outdated", "outdated"),
        ("pkg_audit", "audit"),
        ("pkg_vuln", "audit"),
        ("pkg_vulnerable", "audit"),
        ("pkg_latest", "latest"),
        ("pkg_upgrade_plan", "upgrade_plan"),
        ("pkg_supply_chain", "supply_chain"),
        ("nuget_audit", "audit"),
        ("nuget_latest", "latest"),
        ("nuget_upgrade_plan", "upgrade_plan")
    ];

    static string NormalizePkgOp(string op) =>
        op switch
        {
            "deps" or "dependencies" or "ls" or "status" => "list",
            "search" or "query" => "find",
            "install" or "new" => "add",
            "uninstall" or "rm" or "delete" => "remove",
            "upgrade" or "bump" => "update",
            "stale" or "old" => "outdated",
            "vuln" or "vulnerable" or "cve" or "audit_vuln" => "audit",
            "upgrade-plan" or "fix_vuln" or "fix_vulnerable" => "upgrade_plan",
            "supply-chain" or "supplychain" => "supply_chain",
            _ => op
        };

    static bool IsPkgOp(string? op) =>
        op is "list" or "find" or "add" or "remove" or "update" or "outdated"
            or "audit" or "latest" or "upgrade_plan" or "supply_chain";
}
