#nullable enable
using System.Text.Json;
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp;

/// <summary>go= result slim / organ pulse helpers.</summary>
internal static partial class IdeCockpit
{
    sealed record OrganPulse(bool Ok, string Line, string? Schema, object? Next, string? Hint);

    static readonly OrganJsonPulseUnit OrganJsonPulse = new();

    static OrganPulse PulseFromOrgan(string raw)
    {
        var p = OrganJsonPulse.FromJson(raw, GoPulseCapChars);
        return new OrganPulse(p.Ok, p.Line, p.Schema, p.Next, p.Hint);
    }

    static object? TryParseJson(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(text);
        }
        catch
        {
            return text;
        }
    }

    static (string Text, bool Truncated) CapGoResult(string raw, int cap)
    {
        if (raw.Length <= cap)
            return (raw, false);
        return (raw[..cap] + "\n…[cockpit go.result truncated]", true);
    }

    static readonly GoResultSlimUnit GoResultSlim = new();

    /// <summary>
    /// Soft-organ Handle() often ignores go_detail — slim fat dumps to pulse when A (default).
    /// </summary>
    static object? SlimGoResult(object? goResult, string? goDetailRaw) =>
        GoResultSlim.Slim(goResult, goDetailRaw, raw =>
        {
            var p = PulseFromOrgan(raw);
            return new GoResultSlimUnit.OrganPulseSnap(p.Ok, p.Line, p.Schema, p.Next, p.Hint);
        });

    static bool IsPressureGoResult(object? goResult)
    {
        if (goResult is null)
            return false;
        try
        {
            var raw = JsonSerializer.Serialize(goResult);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("schema", out var sch)
                && sch.ValueKind == JsonValueKind.String
                && string.Equals(sch.GetString(), IdePressureChannel.SchemaVersion, StringComparison.Ordinal))
                return true;
            if (root.TryGetProperty("go", out var go)
                && go.ValueKind == JsonValueKind.String
                && go.GetString() is { Length: > 0 } g)
            {
                return g.Equals("pressure_desk", StringComparison.OrdinalIgnoreCase)
                       || g.Equals("pressure", StringComparison.OrdinalIgnoreCase)
                       || g.Equals("compact_prep", StringComparison.OrdinalIgnoreCase)
                       || g.Equals("pre_compact", StringComparison.OrdinalIgnoreCase)
                       || g.Equals("cdp_pressure", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

}
