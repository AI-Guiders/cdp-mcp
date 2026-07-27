#nullable enable
using System.Text.Json;

namespace CdpMcp.Cockpit.ComputingUnits;

/// <summary>CCU: organ JSON → compact pulse line (go_detail=pulse path).</summary>
public sealed class OrganJsonPulseUnit : ICockpitComputeUnit
{
    public const int DefaultCapChars = 1_200;

    public GoResultSlimUnit.OrganPulseSnap FromJson(string raw, int capChars = DefaultCapChars)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var ok = !root.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.False;
            if (root.TryGetProperty("pulse", out var pulseEl) && pulseEl.ValueKind == JsonValueKind.String)
            {
                var pulseLine = pulseEl.GetString() ?? "";
                if (pulseLine.Length > 0)
                {
                    var hintEarly = root.TryGetProperty("hint", out var h0) && h0.ValueKind == JsonValueKind.String
                        ? Truncate(h0.GetString(), 240)
                        : null;
                    var schemaEarly = root.TryGetProperty("schema", out var sch0) && sch0.ValueKind == JsonValueKind.String
                        ? sch0.GetString()
                        : null;
                    return new GoResultSlimUnit.OrganPulseSnap(
                        ok, Truncate(pulseLine, capChars) ?? pulseLine, schemaEarly, null, hintEarly);
                }
            }

            var schema = root.TryGetProperty("schema", out var sch) && sch.ValueKind == JsonValueKind.String
                ? sch.GetString()
                : null;
            var hint = root.TryGetProperty("hint", out var h) && h.ValueKind == JsonValueKind.String
                ? Truncate(h.GetString(), 240)
                : null;
            object? next = null;
            if (root.TryGetProperty("next", out var n))
                next = JsonSerializer.Deserialize<JsonElement>(n.GetRawText());

            var bits = new List<string>();
            if (schema is { Length: > 0 })
                bits.Add(schema);
            bits.Add(ok ? "ok" : "FAIL");

            void AddNum(string key, string label)
            {
                if (root.TryGetProperty(key, out var el) && el.TryGetInt32(out var num))
                    bits.Add($"{label}={num}");
            }

            AddNum("count", "n");
            AddNum("dirty_count", "dirty");
            AddNum("disk_changed_count", "disk");
            AddNum("candidate_count", "cand");
            AddNum("slice_count", "slices");
            AddNum("path_count", "paths");
            AddNum("tab_count", "tabs");
            AddNum("groups", "groups");
            AddNum("files_scanned", "files");
            AddNum("undo_left", "undo");
            AddNum("redo_left", "redo");
            AddNum("replaced", "replaced");

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                bits.Add(Truncate(err.GetString(), 80) ?? "error");

            if (root.TryGetProperty("roots", out var roots) && roots.ValueKind == JsonValueKind.Array)
                bits.Add($"roots={roots.GetArrayLength()}");

            var line = string.Join(' ', bits);
            if (line.Length > capChars)
                line = line[..capChars] + "…";
            return new GoResultSlimUnit.OrganPulseSnap(ok, line, schema, next, hint);
        }
        catch
        {
            var line = Truncate(raw, capChars) ?? "";
            return new GoResultSlimUnit.OrganPulseSnap(
                true, line, null, null, "go_detail=full for parseable dump");
        }
    }

    static string? Truncate(string? s, int max)
    {
        if (s is null)
            return null;
        if (s.Length <= max)
            return s;
        return s[..max] + "…";
    }
}
