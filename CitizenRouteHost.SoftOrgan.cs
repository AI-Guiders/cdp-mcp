#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Shared JSON ok/pulse readers for SoftOrgan Meta citizen hosts.</summary>
internal static partial class CitizenRouteHost
{
    static bool TryReadSoftOrganOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl))
                return okEl.ValueKind != JsonValueKind.False;
            if (root.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 })
                return false;
            return root.TryGetProperty("pulse", out _)
                || root.TryGetProperty("schema", out _)
                || root.TryGetProperty("rows", out _)
                || root.TryGetProperty("plugins", out _)
                || root.TryGetProperty("ops", out _)
                || root.TryGetProperty("level", out _)
                || root.TryGetProperty("idle", out _)
                || (root.TryGetProperty("view", out var view)
                    && view.ValueKind == JsonValueKind.Object
                    && view.TryGetProperty("lines", out _));
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadSoftOrganPulse(string json, string tag, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse(tag + " " + op + " " + pulse);

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                return TruncPulse(tag + " " + op + " · " + e);

            return TruncPulse(tag + " " + op);
        }
        catch
        {
            return TruncPulse(tag + " " + op);
        }
    }
}
