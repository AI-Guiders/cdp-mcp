#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Shared comfort JSON helpers for buffer organ Execute paths (OOA&D peel).</summary>
internal static class CitizenBufferComfort
{
    internal static bool TryReadUndoOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl))
                return okEl.ValueKind != JsonValueKind.False;
            return false;
        }
        catch
        {
            return false;
        }
    }

    internal static string? TryReadUndoError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                return CitizenRouteHost.TruncPulse(err.GetString());
        }
        catch
        {
            /* best-effort */
        }

        return null;
    }

    internal static string? TryReadUndoPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var bits = new List<string> { op };
            if (root.TryGetProperty("undone", out var u) && u.ValueKind == JsonValueKind.String
                && u.GetString() is { Length: > 0 } undone)
                bits.Add(undone);
            if (root.TryGetProperty("redone", out var r) && r.ValueKind == JsonValueKind.String
                && r.GetString() is { Length: > 0 } redone)
                bits.Add(redone);
            if (root.TryGetProperty("undo_left", out var ul) && ul.TryGetInt32(out var undoLeft))
                bits.Add("undo=" + undoLeft);
            if (root.TryGetProperty("redo_left", out var rl) && rl.TryGetInt32(out var redoLeft))
                bits.Add("redo=" + redoLeft);
            return CitizenRouteHost.TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return CitizenRouteHost.TruncPulse(op);
        }
    }

    internal static string? TryReadRootPath(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        }
        catch
        {
            /* best-effort */
        }

        return null;
    }

    internal static string ShortNavLeaf(string path)
    {
        var leaf = path;
        var slash = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        if (slash >= 0 && slash < path.Length - 1)
            leaf = path[(slash + 1)..];
        return leaf.Trim('[', ']');
    }

    internal static string? TryReadLocus(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("locus", out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        }
        catch
        {
            /* best-effort */
        }

        return null;
    }
}
