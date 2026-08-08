#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Single result-wake facade for Citizen Completions after hands/peerAck.
/// Mention-wake (<c>TryNotifyCitizen</c> / @Sierra) is a separate entry — not folded here.
/// Depth-1: do not chain when the processed body is already a successful wake charge.
/// SoftFL densify: same-turn observe ≠ stop — arm peer_ready so fail/"не вышло" still wakes Completions #3.
/// Dropped on primary peer_ready → retry with drop tip; Dropped on retry → one retry2 with tip; retry2 stops (anti-loop).
/// </summary>
internal static class CitizenResultWake
{
    public const string LeafTakePath = "CascadeIDE.GlassCore/Intercom/GlassIntercomMention.cs";

    /// <summary>Copy-paste line Completions must emit — not a buried leaf= hint.</summary>
    public const string LeafTakeIntent =
        "@intent take path=\"" + LeafTakePath + "\" start_line=60 end_line=120";

    public const string PeerReadyCharge =
        "reason=peer_ready — hands returned; verify @event peer pulse. PASTE next hand exactly: "
        + LeafTakeIntent
        + " — find≠next hand; do not invent CascadeIDE.cs / *Host.cs / GlassIntercom.cs / dialog-history basenames; Radio alone ≠ done; Radio only if stuck (one fact).";

    public const string PeerReadyRetryCharge =
        "reason=peer_ready_retry — hand dropped/failed; result is still a result. PASTE exactly: "
        + LeafTakeIntent
        + ". find≠escape. invent CascadeIDE.cs / *Host.cs / dialog-history basenames ≠ densify.";

    public const string PeerReadyRetry2Charge =
        "reason=peer_ready_retry2 — second densify after drop; PASTE exactly: "
        + LeafTakeIntent
        + ". find≠escape. invent sibling names ≠ densify.";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Same-turn observe nudge + peer_ready enqueue — no second arm on success.</summary>
    public static bool IsWakeCharge(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;
        var t = body.Trim();
        if (t.Equals(CitizenGlassDialogBridge.SameTurnObserveUser, StringComparison.Ordinal))
            return true;
        return t.StartsWith("reason=peer_ready", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsRetry2WakeCharge(string? body) =>
        !string.IsNullOrWhiteSpace(body)
        && body.Trim().StartsWith("reason=peer_ready_retry2", StringComparison.OrdinalIgnoreCase);

    static bool IsRetryWakeCharge(string? body) =>
        !string.IsNullOrWhiteSpace(body)
        && body.Trim().StartsWith("reason=peer_ready_retry", StringComparison.OrdinalIgnoreCase)
        && !IsRetry2WakeCharge(body);

    /// <summary>
    /// Embed peer drop tip as context only. Never steer densify of find/no_project drops —
    /// Completions copies Persona example CitizenRouteHost.cs / invents CascadeIDE.cs otherwise.
    /// </summary>
    public static string FormatDropCharge(string baseCharge, CitizenPeerAck.Result peerAck)
    {
        var tip = peerAck.Peer ?? "";
        tip = tip.Replace("\r", " ").Replace("\n", " ").Trim();
        while (tip.Contains("  ", StringComparison.Ordinal))
            tip = tip.Replace("  ", " ", StringComparison.Ordinal);
        if (tip.Length > 200)
            tip = tip[..200] + "…";
        if (string.IsNullOrWhiteSpace(tip))
            tip = "ack dropped";
        return baseCharge
            + " drop=["
            + tip
            + "] — context only; PASTE leaf take from charge (quoted FULL). find/no_project/invented basenames ≠ target. Radio alone ≠ done.";
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

        // Dropped/failed: still a result — wake again with densify tip.
        if (peerAck.Dropped > 0)
        {
            if (IsRetry2WakeCharge(requestBody))
                return false;
            if (IsRetryWakeCharge(requestBody))
                return TryArmAfterHands(channel, FormatDropCharge(PeerReadyRetry2Charge, peerAck));
            if (IsWakeCharge(requestBody))
                return TryArmAfterHands(channel, FormatDropCharge(PeerReadyRetryCharge, peerAck));
            return TryArmAfterHands(channel, PeerReadyCharge);
        }

        // All applied: depth-1 — no chain when body is already a wake charge.
        if (IsWakeCharge(requestBody))
            return false;

        // Observe ran Completions #2 in-loop — still arm peer_ready for #3 (contour self-flight).
        _ = sameTurnObserveRan;
        return TryArmAfterHands(channel, PeerReadyCharge);
    }

    /// <summary>
    /// Write pending citizen-dialog-request so bridge Loop wakes her on the result.
    /// Latch dedup: do not overwrite pending/running human Send; idempotent if peer_ready already pending.
    /// Prefer <see cref="AfterHands"/> from call sites.
    /// </summary>
    public static bool TryArmAfterHands(string? channel = null, string? body = null)
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

            var charge = string.IsNullOrWhiteSpace(body) ? PeerReadyCharge : body;
            var wakeReason = IsRetry2WakeCharge(charge)
                ? "reason=peer_ready_retry2"
                : IsRetryWakeCharge(charge)
                    ? "reason=peer_ready_retry"
                    : "reason=peer_ready";

            var doc = new CitizenGlassDialogBridge.RequestDoc
            {
                Schema = CitizenGlassDialogBridge.Schema,
                Id = id,
                Body = charge,
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
                wakeReason);
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
