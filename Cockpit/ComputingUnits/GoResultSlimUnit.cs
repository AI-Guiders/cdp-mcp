#nullable enable
using System.Text.Json;

namespace CdpMcp.Cockpit.ComputingUnits;

/// <summary>CCU: slim fat soft-organ Handle dumps to pulse when go_detail≠full.</summary>
public sealed class GoResultSlimUnit : ICockpitComputeUnit
{
    public readonly record struct OrganPulseSnap(
        bool Ok,
        string Line,
        string? Schema,
        object? Next,
        string? Hint);

    public object? Slim(
        object? goResult,
        string? goDetailRaw,
        Func<string, OrganPulseSnap> pulseFrom)
    {
        if (goResult is null)
            return null;
        var detail = (goDetailRaw ?? "pulse").Trim().ToLowerInvariant();
        if (detail is "full")
            return goResult;

        string raw;
        try
        {
            raw = JsonSerializer.Serialize(goResult);
        }
        catch
        {
            return goResult;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("detail", out var d)
                && d.ValueKind == JsonValueKind.String
                && d.GetString() is "pulse"
                && root.TryGetProperty("pulse", out var p)
                && p.ValueKind == JsonValueKind.String
                && !HasFatDump(root))
            {
                return goResult;
            }

            var pulse = pulseFrom(raw);
            var go = PropStr(root, "go") ?? "go";
            var tool = PropStr(root, "tool");
            return new
            {
                ok = pulse.Ok,
                go,
                tool,
                detail = "pulse",
                pulse = pulse.Line,
                schema = pulse.Schema,
                next = pulse.Next,
                slimmed = true,
                hint = pulse.Hint ?? "go_detail=full for organ dump; or call organ tool directly."
            };
        }
        catch
        {
            var pulse = pulseFrom(raw);
            return new
            {
                ok = pulse.Ok,
                go = "go",
                detail = "pulse",
                pulse = pulse.Line,
                slimmed = true,
                hint = "go_detail=full for organ dump"
            };
        }
    }

    public static bool HasFatDump(JsonElement root)
    {
        if (root.TryGetProperty("view", out var view) && view.ValueKind == JsonValueKind.Object)
            return true;
        if (root.TryGetProperty("result", out var result)
            && result.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            return true;
        if (root.TryGetProperty("board", out _))
            return true;
        if (root.TryGetProperty("lines", out var lines)
            && lines.ValueKind == JsonValueKind.Array
            && lines.GetArrayLength() > 2)
            return true;
        if (root.TryGetProperty("panes", out var panes)
            && panes.ValueKind == JsonValueKind.Array
            && panes.GetArrayLength() > 0)
            return true;
        return false;
    }

    static string? PropStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}
