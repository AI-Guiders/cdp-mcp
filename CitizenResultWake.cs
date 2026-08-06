#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Autoi-parity result-ready wake for Citizen (reason=peer_ready).
/// After host-execute returns peerAck she must get a new Glass dialog turn —
/// same class as remount|build_finished for Composer — not sleep until operator Send.
/// Depth-1: do not chain when the processed body is already a wake charge.
/// </summary>
internal static class CitizenResultWake
{
    public const string PeerReadyCharge =
        "reason=peer_ready — hands returned; verify @event peer pulse and continue if needed. One short Radio letter.";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Same-turn observe nudge + peer_ready enqueue — no second arm.</summary>
    public static bool IsWakeCharge(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;
        var t = body.Trim();
        if (t.Equals(CitizenGlassDialogBridge.SameTurnObserveUser, StringComparison.Ordinal))
            return true;
        return t.StartsWith("reason=peer_ready", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Write pending citizen-dialog-request so bridge Loop wakes her on the result.
    /// Call only after the prior latch is released (status=done), or from paths that do not own the latch.
    /// </summary>
    public static bool TryArmAfterHands(string? channel = null)
    {
        try
        {
            if (!IdeCitizenChannel.IsInviteReady())
                return false;

            Directory.CreateDirectory(CitizenGlassDialogBridge.StateRoot);
            var id = Guid.NewGuid().ToString("N")[..12];
            var channelCode = string.IsNullOrWhiteSpace(channel)
                ? "crew"
                : channel.Trim().ToLowerInvariant() switch
                {
                    "crew" or "#crew" => "crew",
                    "radio" => "radio",
                    "dm" or "direct" or "1:1" => "dm",
                    _ => "crew"
                };

            var doc = new CitizenGlassDialogBridge.RequestDoc
            {
                Schema = CitizenGlassDialogBridge.Schema,
                Id = id,
                Body = PeerReadyCharge,
                Channel = channelCode,
                Status = "pending",
                StampedUtc = DateTimeOffset.UtcNow
            };
            var json = JsonSerializer.Serialize(doc, JsonOpts);
            var path = CitizenGlassDialogBridge.RequestPath;
            var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
            IdeFlightDataRecorder.RecordWake(
                "wake_arm",
                "citizen-peer-ready-" + id,
                "citizen_result_wake",
                "reason=peer_ready");
            return true;
        }
        catch
        {
            return false;
        }
    }
}
