#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent pressure — L1 stash/recall without Cursor MCP (go=pressure place-only).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RoutePressure(string raw)
    {
        var op = ExtractKeyedValue(raw, "op") ?? ExtractKeyedValue(raw, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (raw.StartsWith("pressure ", StringComparison.OrdinalIgnoreCase))
            {
                var rest = raw["pressure ".Length..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op) ? "scene" : op.Trim().ToLowerInvariant();
        op = NormalizePressureOp(op);

        if (!IsPressureOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "pressure_op_unknown");

        if (op is "stash" or "memo"
            && string.IsNullOrWhiteSpace(
                ExtractKeyedValue(raw, "body")
                ?? ExtractKeyedValue(raw, "text")
                ?? ExtractKeyedValue(raw, "content")))
        {
            return new Route(
                Verb.Pressure,
                raw,
                Ok: false,
                Op: op,
                Go: "pressure",
                Reason: "pressure_body_required");
        }

        return new Route(
            Verb.Pressure,
            raw,
            Ok: true,
            Op: op,
            Go: "pressure");
    }

    static string NormalizePressureOp(string op) =>
        op switch
        {
            "pulse" or "status" or "desk" => "scene",
            "write" or "save" => "stash",
            "append" or "note" => "memo",
            "history" or "tail" => "line",
            "load" or "peek" => "recall",
            "recon" => "reconcile",
            "aligned" => "align",
            "gate_ready" => "ready",
            "armed" or "l1" => "arm",
            "done" => "clear",
            _ => op
        };

    static bool IsPressureOp(string? op) =>
        op is "scene" or "arm" or "stash" or "memo" or "line"
            or "recall" or "reconcile" or "steer" or "ssot" or "fast"
            or "align" or "ready" or "gate" or "clear" or "disarm";
}
