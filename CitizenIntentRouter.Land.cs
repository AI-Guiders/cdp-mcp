#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent land|deep_link — NavigationLand / cdp_land without Cursor MCP (Family:navigation Anchor).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteLand(string raw)
    {
        var work = NormalizeLandCompound(raw);
        var wired = ExtractKeyedValue(work, "anchor")
            ?? ExtractKeyedValue(work, "at")
            ?? ExtractKeyedValue(work, "wire");

        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("land ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("deep_link ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("deeplink ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        // Bare land / deep_link → desk Restore Previous (Command:restore).
        op = string.IsNullOrWhiteSpace(op)
            ? (string.IsNullOrWhiteSpace(wired) ? "restore" : "wire")
            : op.Trim().ToLowerInvariant();
        op = NormalizeLandOp(op);

        if (!IsLandOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "land_op_unknown");

        var path = ExtractKeyedValue(work, "path")
            ?? ExtractKeyedValue(work, "file")
            ?? ExtractKeyedValue(work, "name");
        var line = ExtractKeyedValue(work, "line")
            ?? ExtractKeyedValue(work, "L");
        var member = ExtractKeyedValue(work, "member")
            ?? ExtractKeyedValue(work, "M")
            ?? ExtractKeyedValue(work, "symbol");
        var goTarget = ExtractKeyedValue(work, "go")
            ?? ExtractKeyedValue(work, "organ");

        string? command;
        if (!string.IsNullOrWhiteSpace(wired))
        {
            command = wired.Trim();
        }
        else
        {
            command = BuildLandAnchor(op, path, line, member, goTarget);
            if (command is null)
            {
                return new Route(
                    Verb.Unknown,
                    raw,
                    Ok: false,
                    Reason: op is "go" ? "land_go_required" : "land_path_required");
            }
        }

        return new Route(
            Verb.Land,
            raw,
            Ok: true,
            Op: op is "wire" ? InferLandOpFromWire(command) ?? "wire" : op,
            Path: path,
            Detail: line,
            Scene: member,
            Organ: goTarget,
            Command: command,
            Go: "land");
    }

    static string NormalizeLandCompound(string raw)
    {
        foreach (var (prefix, op) in LandCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "land " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "land" + rest;
            return "land " + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] LandCompounds =
    [
        ("land_restore", "restore"),
        ("land_open", "open"),
        ("land_goto", "goto"),
        ("land_show", "show"),
        ("land_go", "go"),
        ("deep_link", "restore"),
        ("deeplink", "restore")
    ];

    static string NormalizeLandOp(string op) =>
        op switch
        {
            "bookmark" or "desk" or "previous" => "restore",
            "reveal" or "file" => "open",
            "jump" or "line" => "goto",
            "artifact" or "exists" => "show",
            "organ" or "cockpit" => "go",
            "raw" or "anchor" or "wire" => "wire",
            _ => op
        };

    static bool IsLandOp(string? op) =>
        op is "restore" or "open" or "goto" or "show" or "go" or "wire";

    static string? BuildLandAnchor(
        string op,
        string? path,
        string? line,
        string? member,
        string? goTarget)
    {
        if (op is "restore")
            return "[Family:navigation;Command:restore]";

        if (op is "go")
        {
            if (string.IsNullOrWhiteSpace(goTarget))
                return null;
            return "[Family:navigation;Command:go;Go:" + goTarget.Trim() + "]";
        }

        if (op is "wire")
            return null;

        if (string.IsNullOrWhiteSpace(path))
            return null;

        var nested = new List<string> { "File:" + path.Trim() };
        if (!string.IsNullOrWhiteSpace(member))
            nested.Add("Member:" + member.Trim());
        if (!string.IsNullOrWhiteSpace(line))
            nested.Add("Line:" + line.Trim());

        return "[Family:navigation;Command:" + op + ";Anchor:[" + string.Join(';', nested) + "]]";
    }

    static string? InferLandOpFromWire(string wire)
    {
        const string marker = "Command:";
        var idx = wire.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;
        var rest = wire[(idx + marker.Length)..];
        var end = rest.IndexOfAny([';', ']']);
        var cmd = (end < 0 ? rest : rest[..end]).Trim().ToLowerInvariant();
        return IsLandOp(cmd) && cmd is not "wire" ? cmd : null;
    }
}
