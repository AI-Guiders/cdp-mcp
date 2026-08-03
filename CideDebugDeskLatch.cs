#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Agent Debug-SA pulse → CIDE/Glass SoftOrgan (instant).
/// Writes %LocalAppData%/cdp-mcp/debug_desk-LATEST.json; Glass MFD DebugStack glance.
/// Live: DAP stopped/continued hooks enrich stack/locals. Idle stays silent (Dark Cockpit).
/// </summary>
internal static class CideDebugDeskLatch
{
    public const string Schema = "cide_debug_desk_latch/v1";
    public const string OriginAgent = "agent";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>Test hook: redirect latch root.</summary>
    internal static string? RootOverrideForTests { get; set; }

    public static string StateRoot =>
        RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp");

    public static string LatchPath => Path.Combine(StateRoot, "debug_desk-LATEST.json");

    public static void Publish(
        bool active,
        string pulse,
        string? verdict,
        int bpCount,
        bool stopped,
        bool activeDap,
        IReadOnlyList<StackFrameDoc>? stack = null,
        IReadOnlyList<LocalVarDoc>? locals = null)
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var pulseLine = string.IsNullOrWhiteSpace(pulse) ? "debug_desk · idle" : pulse.Trim();
            var doc = new DebugDeskLatchDoc
            {
                Schema = Schema,
                Origin = OriginAgent,
                StampedUtc = DateTimeOffset.UtcNow,
                Active = active,
                Pulse = pulseLine,
                Verdict = string.IsNullOrWhiteSpace(verdict) ? null : verdict.Trim(),
                BpCount = bpCount,
                Stopped = stopped,
                ActiveDap = activeDap,
                ChromeHint = active ? pulseLine : null,
                Stack = stack is { Count: > 0 } ? stack.ToList() : null,
                Locals = locals is { Count: > 0 } ? locals.ToList() : null
            };
            var json = JsonSerializer.Serialize(doc, JsonOpts);
            var tmp = LatchPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, LatchPath, overwrite: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    public static DebugDeskLatchDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<DebugDeskLatchDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public sealed class DebugDeskLatchDoc
    {
        public string Schema { get; set; } = CideDebugDeskLatch.Schema;
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }
        public bool Active { get; set; }
        public string? Pulse { get; set; }
        public string? Verdict { get; set; }
        public int BpCount { get; set; }
        public bool Stopped { get; set; }
        public bool ActiveDap { get; set; }
        public string? ChromeHint { get; set; }
        public List<StackFrameDoc>? Stack { get; set; }
        public List<LocalVarDoc>? Locals { get; set; }
    }

    public sealed class StackFrameDoc
    {
        public string Name { get; set; } = "?";
        public string? File { get; set; }
        public int Line { get; set; }
    }

    public sealed class LocalVarDoc
    {
        public string Name { get; set; } = "?";
        public string Value { get; set; } = "";
    }
}
