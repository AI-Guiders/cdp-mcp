#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using CdpMcp.Cockpit.Surface;

namespace CdpMcp;

/// <summary>
/// Agent desk seats → CIDE cabin tool map (instant).
/// Writes %LocalAppData%/cdp-mcp/seats-LATEST.json; CIDE projector applies mappable mfd_page.
/// Internal transport — agent looks desk, not JSON.
/// </summary>
internal static class CideSeatsLatch
{
    public const string Schema = "cide_seats_latch/v1";
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

    public static string LatchPath => Path.Combine(StateRoot, "seats-LATEST.json");

    public static void Publish(IReadOnlyDictionary<string, string?> seats)
    {
        if (seats is null || seats.Count == 0)
            return;

        try
        {
            var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (seat, pin) in seats)
            {
                if (string.IsNullOrWhiteSpace(seat))
                    continue;
                map[seat.Trim().ToLowerInvariant()] =
                    string.IsNullOrWhiteSpace(pin) ? null : pin.Trim();
            }

            if (map.Count == 0)
                return;

            string? mfdPage = null;
            string? chromeHint = null;
            // Prefer M (primary tool surface), then forward, then P.
            foreach (var seat in new[] { "m", "forward", "p" })
            {
                if (!map.TryGetValue(seat, out var pin) || pin is null)
                    continue;
                var proj = CabinGlassProjectionCatalog.TryResolve(pin);
                if (proj is null)
                    continue;
                if (mfdPage is null && proj.Value.MfdPage is { Length: > 0 })
                    mfdPage = proj.Value.MfdPage;
                if (chromeHint is null && proj.Value.ChromeHint is { Length: > 0 })
                    chromeHint = proj.Value.ChromeHint;
                if (mfdPage is not null && chromeHint is not null)
                    break;
            }

            // Unmapped M pin still gets a chrome hint so glass "knows the name".
            if (chromeHint is null
                && map.TryGetValue("m", out var mPin)
                && mPin is { Length: > 0 }
                && CabinGlassProjectionCatalog.TryResolve(mPin) is null)
            {
                chromeHint = "agent · M: " + mPin;
            }

            Directory.CreateDirectory(StateRoot);
            var doc = new SeatsLatchDoc
            {
                Schema = Schema,
                Seats = map,
                MfdPage = mfdPage,
                ChromeHint = chromeHint,
                Origin = OriginAgent,
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

    public static SeatsLatchDoc? TryRead()
    {
        try
        {
            if (!File.Exists(LatchPath))
                return null;
            var raw = File.ReadAllText(LatchPath);
            var doc = JsonSerializer.Deserialize<SeatsLatchDoc>(raw, ReadOpts);
            if (doc is null || !string.Equals(doc.Schema, Schema, StringComparison.OrdinalIgnoreCase))
                return null;
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public sealed class SeatsLatchDoc
    {
        public string Schema { get; set; } = CideSeatsLatch.Schema;
        public Dictionary<string, string?>? Seats { get; set; }
        public string? MfdPage { get; set; }
        public string? ChromeHint { get; set; }
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }
    }
}
