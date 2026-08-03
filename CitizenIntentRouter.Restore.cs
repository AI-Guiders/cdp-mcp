#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent restore|recent — cdp_restore / cdp_recent without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteRestore(string raw)
    {
        var family = DetectRestoreFamily(raw);
        var work = NormalizeRestoreCompound(raw, family);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("restore ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("restore_previous ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("desk_restore ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("recent ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("open_recent ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op)
            ? (family == "recent" ? "list" : "restore")
            : op.Trim().ToLowerInvariant();
        op = NormalizeRestoreOp(op, family);

        if (!IsRestoreOp(op, family))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "restore_op_unknown");

        var take = ExtractKeyedValue(work, "take")
            ?? ExtractKeyedValue(work, "max")
            ?? ExtractKeyedValue(work, "n");

        return new Route(
            Verb.Restore,
            raw,
            Ok: true,
            Op: op,
            Detail: take,
            Organ: family,
            Go: family == "recent" ? "recent" : "restore");
    }

    static string DetectRestoreFamily(string raw)
    {
        if (raw.Equals("recent", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("recent ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("open_recent", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("open_recent ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("recent_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("recent_list ", StringComparison.OrdinalIgnoreCase))
            return "recent";
        return "restore";
    }

    static string NormalizeRestoreCompound(string raw, string family)
    {
        foreach (var (prefix, op) in RestoreCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return family == "recent" ? "recent " + op : "restore " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return (family == "recent" ? "recent" : "restore") + rest;
            return (family == "recent" ? "recent " : "restore ") + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] RestoreCompounds =
    [
        ("restore_previous", "restore"),
        ("desk_restore", "restore"),
        ("restore_peek", "peek"),
        ("open_recent", "list"),
        ("recent_list", "list")
    ];

    static string NormalizeRestoreOp(string op, string family) =>
        family == "recent"
            ? op switch
            {
                "scene" or "status" or "ls" or "show" => "list",
                _ => op
            }
            : op switch
            {
                "scene" or "status" or "bookmark" or "previous" or "desk" => "restore",
                "show" or "check" => "peek",
                _ => op
            };

    static bool IsRestoreOp(string? op, string family) =>
        family == "recent"
            ? op is "list"
            : op is "restore" or "peek";
}
