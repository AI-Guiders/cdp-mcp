#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using CdpMcp.Cockpit.Surface;

namespace CdpMcp;

/// <summary>
/// Agent desk seats → CIDE cabin tool map (instant).
/// Writes %LocalAppData%/cdp-mcp/seats-LATEST.json; CIDE projector applies mappable mfd_page.
/// <c>show_face</c> = PlaceOrgan human attention (BringCabin + SelectMfd/Prefer P) — not quiet layout pin.
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

    public static void Publish(
        IReadOnlyDictionary<string, string?> seats,
        bool showFace = false,
        string? faceSeat = null,
        string? webAiUrl = null)
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
            var faceKey = string.IsNullOrWhiteSpace(faceSeat)
                ? null
                : faceSeat.Trim().ToLowerInvariant();

            // ShowFace: project the placed seat only (do not steal MFD from sibling pins).
            if (showFace
                && faceKey is not null
                && map.TryGetValue(faceKey, out var facePin)
                && facePin is { Length: > 0 }
                && CabinGlassProjectionCatalog.TryResolve(facePin) is { } faceProj)
            {
                mfdPage = faceProj.MfdPage;
                chromeHint = faceProj.ChromeHint;
            }
            else
            {
                // Quiet republish: Prefer M (primary tool surface), then forward, then P.
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
            }

            // Unmapped M pin still gets a chrome hint so glass "knows the name".
            if (chromeHint is null
                && map.TryGetValue("m", out var mPin)
                && mPin is { Length: > 0 }
                && CabinGlassProjectionCatalog.TryResolve(mPin) is null)
            {
                chromeHint = "agent · M: " + mPin;
            }

            var face = showFace ? faceKey : null;

            Directory.CreateDirectory(StateRoot);
            var doc = new SeatsLatchDoc
            {
                Schema = Schema,
                Seats = map,
                MfdPage = mfdPage,
                ChromeHint = chromeHint,
                ShowFace = showFace,
                FaceSeat = face,
                WebAiUrl = string.IsNullOrWhiteSpace(webAiUrl) ? null : webAiUrl.Trim(),
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
        public bool ShowFace { get; set; }
        public string? FaceSeat { get; set; }
        /// <summary>Optional URL for Glass WebAiPortal navigate (Citizen browser open/search).</summary>
        public string? WebAiUrl { get; set; }
        public string Origin { get; set; } = OriginAgent;
        public DateTimeOffset StampedUtc { get; set; }
    }
}
