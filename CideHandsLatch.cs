#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Citizen hands receipt → Glass SoftInstrument (<c>hands-LATEST.json</c>).
/// Face chips (HND) own ok/fail/running — Intercom letter stays Sierra prose.
/// </summary>
internal static class CideHandsLatch
{
    public const string Schema = "cide_hands_latch/v1";
    public const string OriginAgent = "agent";
    public const string OrganId = "hands";

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

    public static string LatchPath => Path.Combine(StateRoot, OrganId + "-LATEST.json");

    public static void Publish(CitizenHandsReceipt.Snapshot snap)
    {
        try
        {
            Directory.CreateDirectory(StateRoot);
            var hint = CitizenHandsReceipt.FormatChromeHint(snap);
            var active = snap.Phase is CitizenHandsReceipt.Phase.Running or CitizenHandsReceipt.Phase.Done
                && !string.IsNullOrWhiteSpace(hint);
            var doc = new HandsLatchDoc
            {
                Schema = Schema,
                Origin = OriginAgent,
                StampedUtc = DateTimeOffset.UtcNow,
                Active = active,
                Phase = snap.Phase.ToString().ToLowerInvariant(),
                OkCount = snap.OkCount,
                FailCount = snap.FailCount,
                ElapsedMs = snap.Elapsed is { } e ? (long)Math.Round(e.TotalMilliseconds) : null,
                Pulse = hint,
                ChromeHint = active ? hint : null,
                Items = snap.Items.Count == 0
                    ? null
                    : snap.Items.Select(i => new HandsLatchItem
                    {
                        Label = i.Label,
                        Ok = i.Ok,
                        Tip = i.Tip
                    }).ToList()
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

    public static void PublishRunning(TimeSpan? elapsed = null) =>
        Publish(CitizenHandsReceipt.Running(elapsed));

    public static void PublishDone(
        IReadOnlyList<CitizenRouteHost.Applied>? executed,
        TimeSpan? elapsed = null) =>
        Publish(CitizenHandsReceipt.FromApplied(executed, elapsed));

    public static void Clear() => Publish(CitizenHandsReceipt.Idle());

    public static HandsLatchDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<HandsLatchDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public sealed class HandsLatchDoc
    {
        public string Schema { get; set; } = CideHandsLatch.Schema;
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }
        public bool Active { get; set; }
        public string? Phase { get; set; }
        public int OkCount { get; set; }
        public int FailCount { get; set; }
        public long? ElapsedMs { get; set; }
        public string? Pulse { get; set; }
        public string? ChromeHint { get; set; }
        public List<HandsLatchItem>? Items { get; set; }
    }

    public sealed class HandsLatchItem
    {
        public string Label { get; set; } = "";
        public bool Ok { get; set; }
        public string? Tip { get; set; }
    }
}
