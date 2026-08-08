#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Single result-wake facade for Citizen Completions after hands/peerAck.
/// Mention-wake (<c>TryNotifyCitizen</c> / @Sierra) is a separate entry — not folded here.
/// Depth-1: do not chain when the processed body is already a wake charge.
/// SoftFL densify: skip latch arm when same-turn observe already ran Completions #2.
/// </summary>
internal static class CitizenResultWake
{
    public const string PeerReadyCharge =
        "reason=peer_ready — hands returned; verify @event peer pulse. Next hand now (@intent take|replace|find) — Radio alone ≠ done; Radio only if stuck (one fact).";

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
    /// Unified result-wake after host-execute. Call sites: Bridge, Autoi hands, <c>cdp_citizen</c> turn.
    /// </summary>
    public static bool AfterHands(
        CitizenPeerAck.Result? peerAck,
        string? channel = null,
        string? requestBody = null,
        bool sameTurnObserveRan = false)
    {
        if (peerAck is null)
            return false;
        if (IsWakeCharge(requestBody))
            return false;
        // Observe already covered Completions #2 in-loop — no third peer_ready latch.
        if (sameTurnObserveRan)
            return false;
        return TryArmAfterHands(channel);
    }

    /// <summary>
    /// Write pending citizen-dialog-request so bridge Loop wakes her on the result.
    /// Latch dedup: do not overwrite pending/running human Send; idempotent if peer_ready already pending.
    /// Prefer <see cref="AfterHands"/> from call sites.
    /// </summary>
    public static bool TryArmAfterHands(string? channel = null)
    {
        try
        {
            if (!IdeCitizenChannel.IsInviteReady())
                return false;

            if (HasProtectedPendingLatch())
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

    /// <summary>
    /// Pending/running latch that is not ours to overwrite (human Send or already peer_ready).
    /// Use JsonDocument — RequestDoc.Status defaults to "pending"; a failed property bind would false-protect a done latch.
    /// </summary>
    static bool HasProtectedPendingLatch()
    {
        try
        {
            var path = CitizenGlassDialogBridge.RequestPath;
            if (!File.Exists(path))
                return false;
            var raw = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var st)
                ? st.GetString()?.Trim().ToLowerInvariant() ?? ""
                : "";
            if (status is not ("pending" or "running"))
                return false;
            // Human Send or already-armed wake — do not overwrite.
            return true;
        }
        catch
        {
            return false;
        }
    }
}
