#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Shared JSON helpers for buffer edit/comfort organ responses (OOA&D peel).</summary>
internal static class CitizenEditResponse
{
    internal static bool TryReadEditOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl))
                return okEl.ValueKind != JsonValueKind.False;
            return root.TryGetProperty("op", out _) || root.TryGetProperty("meta", out _);
        }
        catch
        {
            return false;
        }
    }

    internal static string? TryReadEditPulse(string json, string place)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("op", out var opEl) && opEl.ValueKind == JsonValueKind.String
                && opEl.GetString() is { Length: > 0 } op)
                return CitizenRouteHost.TruncPulse("edit " + op + " place=" + place);

            return CitizenRouteHost.TruncPulse("edit anchor place=" + place);
        }
        catch
        {
            return CitizenRouteHost.TruncPulse("edit anchor place=" + place);
        }
    }

    internal static void TryReadEditMeta(string json, out string? path, out string? docId)
    {
        path = null;
        docId = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("meta", out var meta) || meta.ValueKind != JsonValueKind.Object)
                return;
            if (meta.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
                path = p.GetString();
            if (meta.TryGetProperty("doc_id", out var d) && d.ValueKind == JsonValueKind.String)
                docId = d.GetString();
        }
        catch
        {
            /* best-effort */
        }
    }
}
