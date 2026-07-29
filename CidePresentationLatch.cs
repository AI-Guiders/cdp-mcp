#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Agent desk → CIDE operator glass (instant).
/// Writes %LocalAppData%/cdp-mcp/presentation-LATEST.json; CIDE projector applies
/// topology / tier / instruments / mfd_page live. Internal transport — agent looks desk, not JSON.
/// Does not touch agent <c>cdp_settings</c> desk keys or repo <c>workspace.toml</c>.
/// </summary>
internal static class CidePresentationLatch
{
    public const string Schema = "cide_presentation_latch/v1";
    public const string OriginAgent = "agent";
    public const string OriginHuman = "human";

    static readonly HashSet<string> AllowedInstrumentKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "pfd_primary",
        "mfd_primary",
        "pfd_status_strip",
        "forward_status_strip"
    };

    static readonly HashSet<string> AllowedTiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "auto", "compact", "cockpit"
    };

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

    public static string LatchPath => Path.Combine(StateRoot, "presentation-LATEST.json");

    /// <summary>Legacy topology-only publish.</summary>
    public static void Publish(string topology, string origin) =>
        Publish(new PresentationPatch { Topology = topology }, origin);

    public static void Publish(PresentationPatch patch, string origin)
    {
        if (patch is null || !patch.HasAny)
            return;
        if (!string.Equals(origin, OriginAgent, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(origin, OriginHuman, StringComparison.OrdinalIgnoreCase))
            return;

        if (patch.Tier is { } tierRaw && !AllowedTiers.Contains(tierRaw.Trim()))
            return;

        Dictionary<string, string>? instruments = null;
        if (patch.Instruments is { Count: > 0 })
        {
            instruments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in patch.Instruments)
            {
                if (string.IsNullOrWhiteSpace(k) || string.IsNullOrWhiteSpace(v))
                    continue;
                if (!AllowedInstrumentKeys.Contains(k.Trim()))
                    continue;
                instruments[k.Trim()] = v.Trim();
            }

            if (instruments.Count == 0)
                instruments = null;
        }

        var topology = string.IsNullOrWhiteSpace(patch.Topology) ? null : patch.Topology.Trim();
        var tier = string.IsNullOrWhiteSpace(patch.Tier) ? null : patch.Tier.Trim().ToLowerInvariant();
        var mfdPage = string.IsNullOrWhiteSpace(patch.MfdPage) ? null : patch.MfdPage.Trim();

        if (topology is null && tier is null && instruments is null && mfdPage is null)
            return;

        try
        {
            Directory.CreateDirectory(StateRoot);
            var doc = new PresentationLatchDoc
            {
                Schema = Schema,
                Topology = topology,
                Tier = tier,
                Instruments = instruments,
                MfdPage = mfdPage,
                Origin = origin.ToLowerInvariant(),
                StampedUtc = DateTimeOffset.UtcNow
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

    public static PresentationLatchDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<PresentationLatchDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            if (!doc.HasAny)
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public sealed class PresentationPatch
    {
        public string? Topology { get; init; }
        public string? Tier { get; init; }
        public Dictionary<string, string>? Instruments { get; init; }
        public string? MfdPage { get; init; }

        public bool HasAny =>
            !string.IsNullOrWhiteSpace(Topology)
            || !string.IsNullOrWhiteSpace(Tier)
            || (Instruments is { Count: > 0 })
            || !string.IsNullOrWhiteSpace(MfdPage);
    }

    public sealed class PresentationLatchDoc
    {
        public string Schema { get; set; } = CidePresentationLatch.Schema;
        public string? Topology { get; set; }
        public string? Tier { get; set; }
        public Dictionary<string, string>? Instruments { get; set; }
        public string? MfdPage { get; set; }
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }

        [JsonIgnore]
        public bool HasAny =>
            !string.IsNullOrWhiteSpace(Topology)
            || !string.IsNullOrWhiteSpace(Tier)
            || (Instruments is { Count: > 0 })
            || !string.IsNullOrWhiteSpace(MfdPage);
    }
}
