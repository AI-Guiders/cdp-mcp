#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent toolchain — cdp_toolchain without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteToolchain(string raw)
    {
        var work = NormalizeToolchainCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("toolchain ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("toolchain_desk ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_toolchain ", StringComparison.OrdinalIgnoreCase))
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
        op = NormalizeToolchainOp(op);

        if (!IsToolchainOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "toolchain_op_unknown");

        var id = ExtractKeyedValue(work, "id")
            ?? ExtractKeyedValue(work, "toolchain")
            ?? ExtractKeyedValue(work, "lang");
        if ((op is "ensure" or "install" or "which") && string.IsNullOrWhiteSpace(id))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "toolchain_id_required");

        var bins = ExtractKeyedValue(work, "bins") ?? ExtractKeyedValue(work, "bin");
        if (op is "add" && (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(bins)))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "toolchain_id_and_bins_required");

        var via = ExtractKeyedValue(work, "via") ?? ExtractKeyedValue(work, "manager");

        return new Route(
            Verb.Toolchain,
            raw,
            Ok: true,
            Op: op,
            Tool: id,
            Detail: via,
            Command: bins,
            Go: "toolchain");
    }

    static string NormalizeToolchainCompound(string raw)
    {
        foreach (var (prefix, op) in ToolchainCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "toolchain " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "toolchain" + rest;
            return "toolchain " + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] ToolchainCompounds =
    [
        ("toolchain_ensure", "ensure"),
        ("toolchain_probe", "probe"),
        ("toolchain_install", "install"),
        ("toolchain_add", "add"),
        ("toolchain_scene", "scene"),
        ("toolchain_which", "which"),
        ("toolchain_desk", "scene")
    ];

    static string NormalizeToolchainOp(string op) =>
        op switch
        {
            "status" or "catalog" or "desk" or "show" => "scene",
            "check" => "probe",
            _ => op
        };

    static bool IsToolchainOp(string? op) =>
        op is "scene" or "probe" or "ensure" or "install" or "add" or "which";
}
