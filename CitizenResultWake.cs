#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Single result-wake facade for Citizen Completions after hands/peerAck.
/// Mention-wake (<c>TryNotifyCitizen</c> / @Sierra) is a separate entry — not folded here.
/// Depth-1: do not chain when the processed body is already a successful wake charge.
/// SoftFL densify: same-turn observe ≠ stop — arm peer_ready so fail/"не вышло" still wakes Completions #3.
/// Dropped invent/FileNotFound → dig charge (A2); dig done → take peer_ready; invent on retry → invent_halt (A3).
/// Non-invent drops: retry → retry2 → stop.
/// </summary>
internal static class CitizenResultWake
{
    /// <summary>Absolute quoted path — SoftFL leaf SSOT (<see cref="CitizenSoftFlLeaf"/>).</summary>
    public static string LeafTakePath => CitizenSoftFlLeaf.Current.Path;

    /// <summary>Copy-paste dig line — from leaf SSOT dig span (not SoftFL apply).</summary>
    public static string LeafTakeIntent => CitizenSoftFlLeaf.FormatDigTakeIntent();

    /// <summary>Host gate A1: quotes + slash + known junction mangles (not Persona prose).</summary>
    public static string NormalizeTakePath(string path)
    {
        var p = path.Trim().Trim('"', '\'');
        foreach (var (from, to) in JunctionAliases)
            p = p.Replace(from, to, StringComparison.OrdinalIgnoreCase);
        p = p.Replace('\\', '/');
        return p;
    }

    static readonly (string From, string To)[] JunctionAliases =
    [
        ("Personal_Cursor_Folder", "Personal Cursor Folder"),
        ("PersonalCursorFolder", "Personal Cursor Folder"),
        ("Personal_Cursor_Fol/", "Personal Cursor Folder/"),
        ("Personal_Cursor_Fol\\", "Personal Cursor Folder/"),
    ];

    /// <summary>Known invent siblings Completions copies instead of PASTE LeafTakeIntent.</summary>
    public static bool IsInventedLeafSibling(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var file = Path.GetFileName(NormalizeTakePath(path));
        return file.Equals("GlassIntercomMention.cs", StringComparison.OrdinalIgnoreCase)
            || file.Equals("GlassIntercom.cs", StringComparison.OrdinalIgnoreCase)
            || file.Equals("GlassIntercomHost.cs", StringComparison.OrdinalIgnoreCase)
            || file.Equals("CascadeIDE.cs", StringComparison.OrdinalIgnoreCase)
            || file.StartsWith("CitizenRouteHost", StringComparison.OrdinalIgnoreCase);
    }

    public static string PasteVerifyRefuseReason =>
        "paste_verify_leaf — invent basename refused; PASTE exactly: "
        + LeafTakeIntent
        + " · dig=@intent files|disk_peek before retry (host gate A1; rewrite thrash REJECT)";

    /// <summary>
    /// Host gate A1 paste-verify: LeafTakePath match (normalized) OK; invent sibling → refuse;
    /// other paths pass (non-leaf takes). Silent RewriteInventedTakePath thrash REJECT.
    /// </summary>
    public static bool TryPasteVerifyTakePath(string? path, out string? normalized, out string? refuseReason)
    {
        refuseReason = null;
        normalized = path;
        if (string.IsNullOrWhiteSpace(path))
            return true;

        var p = NormalizeTakePath(path);
        normalized = p;
        if (p.Equals(LeafTakePath, StringComparison.OrdinalIgnoreCase))
        {
            normalized = LeafTakePath;
            return true;
        }

        if (IsInventedLeafSibling(p))
        {
            refuseReason = PasteVerifyRefuseReason;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Legacy SoftFL name — normalize only; invent siblings no longer silent-map to leaf (use TryPasteVerifyTakePath).
    /// </summary>
    public static string? RewriteInventedTakePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;
        var p = NormalizeTakePath(path);
        if (p.Equals(LeafTakePath, StringComparison.OrdinalIgnoreCase))
            return LeafTakePath;
        return p;
    }

    /// <summary>
    /// SoftFL apply — default peer_ready after hands. Formats from <see cref="CitizenSoftFlLeaf"/> SSOT
    /// (not Mentions prose as sole identity). Dig take stays dig/retry only.
    /// </summary>
    public static string PeerReadyCharge
    {
        get
        {
            CitizenSoftFlLeaf.EnsureMentionsDefault();
            return CitizenSoftFlLeaf.FormatApplyCharge();
        }
    }

    /// <summary>
    /// SoftFL 2026-08-09: rare true-open (no SoftFL leaf named). Anti-invent only.
    /// SoftFL densify 2026-08-09b: do NOT teach «жду вектора» — partner approve / known SoftFL ≠ wait vector.
    /// </summary>
    public const string PeerReadyNextOpenCharge =
        "reason=peer_ready_next_open — no SoftFL leaf named in this wake. "
        + "Do not invent take path. One Radio fact OK. "
        + "Partner «меняй» / known SoftFL on board ≠ wait vector — dig TM or PASTE leaf when charge names it. find≠fabricate next.";

    public static string PeerReadyRetryCharge =>
        "reason=peer_ready_retry — hand dropped/failed; result is still a result. PASTE exactly: "
        + LeafTakeIntent
        + ". find≠escape. invent CascadeIDE.cs / *Host.cs / dialog-history basenames ≠ densify.";

    public static string PeerReadyRetry2Charge =>
        "reason=peer_ready_retry2 — second densify after drop; PASTE exactly: "
        + LeafTakeIntent
        + ". find≠escape. invent sibling names ≠ densify.";

    /// <summary>Host gate A2: invent/FileNotFound → dig organ before take-retry peer_ready.</summary>
    public static string PeerReadyDigCharge =>
        "reason=peer_ready_dig — invent/FileNotFound; dig first (@intent files|disk_peek|shell). "
        + "Do not invent take basename. After dig evidence, PASTE: "
        + LeafTakeIntent
        + ". peer_ready take without dig = off (host gate A2).";

    public static string PeerReadyInventHaltCharge =>
        "reason=peer_ready_invent_halt — invent budget spent on this leaf; stop take-retry thrash. "
        + "Mentor latch: dig=@intent files|disk_peek then PASTE "
        + LeafTakeIntent
        + " once (host gate A3; SoftFL invent mill REJECT).";

    /// <summary>Lived: kb missing/list_pack thrash while SoftFL leaf take woke — keep dig on kb axis.</summary>
    public const string PeerReadyKbCharge =
        "reason=peer_ready_kb — kb hand returned; verify pulse. "
        + "If missing: PASTE @intent kb read_knowledge_file file_path=… relative under knowledge/ "
        + "(strip leading knowledge/). path= aliases file_path=. take leaf ≠ this dig. Radio alone ≠ done.";

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

    /// <summary>Default SoftFL apply charge from leaf SSOT (not dig/retry/kb/next_open).</summary>
    public static bool IsSoftFlApplyWakeCharge(string? body) =>
        CitizenSoftFlLeaf.IsApplyWakeCharge(body);

    /// <summary>Dig hand — SoftFL apply not done (prefer <see cref="CitizenPeerAck.Result.HandKind"/>).</summary>
    public static bool IsDigHand(CitizenPeerAck.Result peerAck) =>
        peerAck.HandKind == CitizenHandKind.Dig
        || (peerAck.HandKind == CitizenHandKind.Unknown && IsLeafTakePulseLegacy(peerAck));

    /// <summary>Legacy tip scrape when HandKind unknown (pre-classifier acks / tests).</summary>
    static bool IsLeafTakePulseLegacy(CitizenPeerAck.Result peerAck)
    {
        var tip = peerAck.Peer ?? "";
        if (tip.Contains("take chars=", StringComparison.OrdinalIgnoreCase))
            return true;
        var ev = peerAck.Event ?? "";
        return ev.Contains("take chars=", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDigWakeCharge(string? body) =>
        !string.IsNullOrWhiteSpace(body)
        && body.Trim().StartsWith("reason=peer_ready_dig", StringComparison.OrdinalIgnoreCase);

    public static bool IsInventHaltWakeCharge(string? body) =>
        !string.IsNullOrWhiteSpace(body)
        && body.Trim().StartsWith("reason=peer_ready_invent_halt", StringComparison.OrdinalIgnoreCase);

    static bool IsRetry2WakeCharge(string? body) =>
        !string.IsNullOrWhiteSpace(body)
        && body.Trim().StartsWith("reason=peer_ready_retry2", StringComparison.OrdinalIgnoreCase);

    static bool IsRetryWakeCharge(string? body) =>
        !string.IsNullOrWhiteSpace(body)
        && body.Trim().StartsWith("reason=peer_ready_retry", StringComparison.OrdinalIgnoreCase)
        && !IsRetry2WakeCharge(body);

    /// <summary>FileNotFound / invent sibling / paste_verify refuse — dig-before-retry (A2).</summary>
    public static bool IsPathInventOrMissingDrop(CitizenPeerAck.Result peerAck)
    {
        var tip = peerAck.Peer ?? "";
        if (tip.Contains("FileNotFound", StringComparison.OrdinalIgnoreCase)
            || tip.Contains("paste_verify_leaf", StringComparison.OrdinalIgnoreCase)
            || tip.Contains("invent", StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var name in new[]
                 {
                     "GlassIntercom.cs", "GlassIntercomHost.cs", "CascadeIDE.cs",
                     "CitizenRouteHost", "GlassIntercomMention.cs"
                 })
        {
            if (tip.Contains(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Dig already done or ladder past dig (retry/retry2) — do not re-arm dig forever.</summary>
    static bool HasDigCredit(bool sameTurnObserveRan, string? requestBody) =>
        sameTurnObserveRan
        || IsDigWakeCharge(requestBody)
        || IsRetryWakeCharge(requestBody)
        || IsRetry2WakeCharge(requestBody);

    /// <summary>
    /// Embed peer drop tip as context only. Never steer densify of find/no_project drops —
    /// Completions copies Persona example CitizenRouteHost.cs / invents CascadeIDE.cs otherwise.
    /// </summary>
    public static string FormatDropCharge(string baseCharge, CitizenPeerAck.Result peerAck)
    {
        var tip = CompactPeerTip(peerAck.Peer);
        return baseCharge
            + " drop=["
            + tip
            + "] — context only; PASTE leaf take from charge (quoted FULL). find/no_project/invented basenames ≠ target. Radio alone ≠ done.";
    }

    public static string FormatKbDropCharge(string baseCharge, CitizenPeerAck.Result peerAck)
    {
        var tip = CompactPeerTip(peerAck.Peer);
        return baseCharge
            + " drop=["
            + tip
            + "] — context only; PASTE kb read with file_path= (strip knowledge/). take leaf ≠ this dig.";
    }

    static string CompactPeerTip(string? peer)
    {
        var tip = peer ?? "";
        tip = tip.Replace("\r", " ").Replace("\n", " ").Trim();
        while (tip.Contains("  ", StringComparison.Ordinal))
            tip = tip.Replace("  ", " ", StringComparison.Ordinal);
        if (tip.Length > 200)
            tip = tip[..200] + "…";
        return string.IsNullOrWhiteSpace(tip) ? "ack dropped" : tip;
    }

    public static bool IsKbDrop(CitizenPeerAck.Result peerAck)
    {
        var tip = peerAck.Peer ?? "";
        return tip.Contains("kb ", StringComparison.OrdinalIgnoreCase)
            || tip.Contains("memory_world", StringComparison.OrdinalIgnoreCase)
            || tip.Contains("read_knowledge_file", StringComparison.OrdinalIgnoreCase)
            || tip.Contains("list_pack", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsKbWakeCharge(string? body) =>
        !string.IsNullOrWhiteSpace(body)
        && body.Contains("reason=peer_ready_kb", StringComparison.Ordinal);

    public static bool IsNextOpenWakeCharge(string? body) =>
        !string.IsNullOrWhiteSpace(body)
        && body.Trim().StartsWith("reason=peer_ready_next_open", StringComparison.OrdinalIgnoreCase);

    /// <summary>Leaf SoftFL contour still wants PASTE <see cref="LeafTakeIntent"/>.</summary>
    public static bool IsLeafContourCharge(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;
        if (IsDigWakeCharge(body) || IsRetryWakeCharge(body) || IsRetry2WakeCharge(body)
            || IsInventHaltWakeCharge(body))
            return true;
        var t = body.Trim();
        if (t.StartsWith("reason=peer_ready_next_open", StringComparison.OrdinalIgnoreCase))
            return false;
        if (t.StartsWith("reason=peer_ready_kb", StringComparison.OrdinalIgnoreCase))
            return false;
        if (t.StartsWith("reason=peer_ready", StringComparison.OrdinalIgnoreCase))
            return true;
        return t.Contains(LeafTakePath, StringComparison.OrdinalIgnoreCase)
            || t.Contains(LeafTakeIntent, StringComparison.Ordinal);
    }

    public static bool IsKbHand(CitizenPeerAck.Result peerAck) => IsKbDrop(peerAck);

    /// <summary>Mutate progress on SoftFL leaf path (tip/event may carry basename).</summary>
    public static bool SoftFlMutateMatchesLeaf(CitizenPeerAck.Result peerAck)
    {
        if (peerAck.HandKind != CitizenHandKind.Mutate)
            return false;
        var tip = (peerAck.Peer ?? "") + "\n" + (peerAck.Event ?? "");
        if (CitizenSoftFlLeaf.MatchesPath(tip))
            return true;
        var leafName = Path.GetFileName(CitizenSoftFlLeaf.Current.Path);
        return tip.Contains(leafName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Dogfood burn → promote scar refuse lines into host ledger (muscle after compact).</summary>
    internal static void PromoteSoftFlDogfoodScar(string? leafId)
    {
        CitizenScarLedger.EnsureBuiltins();
        CitizenScarLedger.Promote(
            CitizenScarLedger.ScarPathMutateOffLeaf,
            CitizenScarGate.RefusePathMutateOffLeaf,
            "SoftFL apply armed: PathMutate path must match SoftFlLeaf SSOT (force= escape)",
            source: "dogfood",
            leafId: leafId);
        CitizenScarLedger.Promote(
            CitizenScarLedger.ScarDigClosesSoftFl,
            "scar_dig_closes_softfl",
            "Dig|Radio under SoftFL apply ≠ SoftFL done; peer_ship only Mutate∩leaf",
            source: "dogfood",
            leafId: leafId);
    }

    /// <summary>
    /// Pick post-hands wake: leaf contour → SoftFL Mentions apply; kb → kb dig.
    /// SoftFL densify 2026-08-09c: default PeerReadyCharge = MentionsAll→ExpandWakes PASTE
    /// (not LeafTakeIntent — take-loop lived). Take stays dig/retry only.
    /// SoftFL densify 2026-08-09b: not next_open — that overshot into anti-agency
    /// («шаг не определён» after partner «меняй»). next_open only when wake body already is next_open.
    /// </summary>
    public static string SelectAppliedWakeCharge(
        CitizenPeerAck.Result peerAck,
        string? requestBody)
    {
        if (IsDigWakeCharge(requestBody))
            return PeerReadyCharge;
        if (IsKbHand(peerAck) || IsKbWakeCharge(requestBody))
            return PeerReadyKbCharge;
        if (IsLeafContourCharge(requestBody))
            return PeerReadyCharge;
        if (IsNextOpenWakeCharge(requestBody))
            return PeerReadyNextOpenCharge;
        return PeerReadyCharge;
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
            // A3: invent budget / retry2 — halt contour (no take-retry forever).
            if (IsRetry2WakeCharge(requestBody) || IsInventHaltWakeCharge(requestBody))
                return false;

            // Lived SoftFL: kb missing while SoftFL leaf take densify — stay on kb axis.
            if (IsKbDrop(peerAck))
            {
                if (IsKbWakeCharge(requestBody))
                    return false;
                return TryArmAfterHands(channel, FormatKbDropCharge(PeerReadyKbCharge, peerAck));
            }

            var inventOrMissing = IsPathInventOrMissingDrop(peerAck);
            if (inventOrMissing && !HasDigCredit(sameTurnObserveRan, requestBody))
            {
                // A2: invent/FileNotFound without dig → dig charge, not take-retry.
                return TryArmAfterHands(channel, FormatDropCharge(PeerReadyDigCharge, peerAck));
            }

            if (inventOrMissing && IsDigWakeCharge(requestBody))
            {
                // Dig ran but invent persisted → one take-retry tip, then retry2, then halt.
                return TryArmAfterHands(channel, FormatDropCharge(PeerReadyRetryCharge, peerAck));
            }

            if (IsRetryWakeCharge(requestBody))
            {
                if (inventOrMissing)
                    return TryArmAfterHands(channel, FormatDropCharge(PeerReadyInventHaltCharge, peerAck));
                return TryArmAfterHands(channel, FormatDropCharge(PeerReadyRetry2Charge, peerAck));
            }

            if (IsWakeCharge(requestBody))
                return TryArmAfterHands(channel, FormatDropCharge(PeerReadyRetryCharge, peerAck));
            return TryArmAfterHands(channel, SelectAppliedWakeCharge(peerAck, requestBody));
        }

        // Dig credit satisfied — arm SoftFL apply from leaf SSOT (not take-loop).
        if (IsDigWakeCharge(requestBody))
            return TryArmAfterHands(channel, PeerReadyCharge);

        // All applied: depth-1 — no chain when body is already a wake charge.
        // SoftFL systemic: Dig|Radio under apply charge ≠ SoftFL done — re-arm apply.
        // Mutate on leaf path → NotifyPeerShip (host evidence) + stop.
        if (IsWakeCharge(requestBody))
        {
            if (IsSoftFlApplyWakeCharge(requestBody))
            {
                if (peerAck.HandKind == CitizenHandKind.Mutate
                    && SoftFlMutateMatchesLeaf(peerAck))
                {
                    IdeIgniteArmHost.NotifyPeerShip(
                        pulse: "softfl_mutate",
                        detail: CitizenSoftFlLeaf.Current.Id);
                    PromoteSoftFlDogfoodScar(CitizenSoftFlLeaf.Current.Id);
                    CitizenSoftFlLeaf.DisarmApply();
                    return false;
                }

                if (IsDigHand(peerAck) || peerAck.HandKind == CitizenHandKind.Radio)
                    return TryArmAfterHands(channel, PeerReadyCharge);
            }

            return false;
        }

        // Observe ran Completions #2 in-loop — still arm wake #3, but not invent-mine when next open.
        _ = sameTurnObserveRan;
        return TryArmAfterHands(channel, SelectAppliedWakeCharge(peerAck, requestBody));
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
            if (IsNextOpenWakeCharge(charge))
                CitizenSoftFlLeaf.DisarmApply();
            var wakeReason = IsInventHaltWakeCharge(charge)
                ? "reason=peer_ready_invent_halt"
                : IsDigWakeCharge(charge)
                    ? "reason=peer_ready_dig"
                    : IsKbWakeCharge(charge)
                        ? "reason=peer_ready_kb"
                        : IsRetry2WakeCharge(charge)
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
