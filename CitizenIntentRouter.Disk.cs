#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent reload|keep_disk|disk_peek — buffer disk-drift hygiene without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteDisk(string raw)
    {
        var head = raw.Trim();
        string? op;
        if (head.StartsWith("disk_peek", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("diskpeek", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("peek_disk", StringComparison.OrdinalIgnoreCase))
            op = "disk_peek";
        else if (head.StartsWith("keep_disk", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("keepdisk", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("dont_reload", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("don't_reload", StringComparison.OrdinalIgnoreCase))
            op = "keep_disk";
        else if (head.StartsWith("reload", StringComparison.OrdinalIgnoreCase))
            op = "reload";
        else
            op = ExtractKeyedValue(raw, "op") ?? "reload";

        op = op.Trim().ToLowerInvariant() switch
        {
            "reload" or "from_disk" or "revert_disk" => "reload",
            "keep_disk" or "keepdisk" or "dont_reload" or "don't_reload" or "keep" => "keep_disk",
            "disk_peek" or "diskpeek" or "peek_disk" or "peek" => "disk_peek",
            _ => op.Trim().ToLowerInvariant()
        };

        if (op is not "reload" and not "keep_disk" and not "disk_peek")
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "disk_op_unknown");

        var path = ExtractKeyedValue(raw, "path") ?? ExtractPath(raw);
        var pad = ExtractKeyedValue(raw, "pad");
        return new Route(
            Verb.Disk,
            raw,
            Ok: true,
            Op: op,
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Detail: string.IsNullOrWhiteSpace(pad) ? null : pad.Trim(),
            Go: "buffer");
    }
}
