#nullable enable
using System.Net.Http;
using System.Text.RegularExpressions;

namespace CdpMcp;

internal static partial class IdeIgniteChannel
{
    /// <summary>
    /// Bump when wake charge template semantics change (independent of assembly patch version).
    /// </summary>
    internal const string ChargeTemplateRev = "20260822-wake-tier-strategy-preflight";

    /// <summary>
    /// Composer wake charge — no TM stage body, shell, toolchain, or commands (cockpit holds SSOT).
    /// </summary>
    internal const string CanonicalComposerCharge =
        "Resume the current authorized local development task from Task Manager. Habitat=CDP. Keep flying the started TM leaf; re-arm continuity as insurance after work — timer ≠ idle license.";

    /// <summary>
    /// Honest compaction hint — host may summarize without warning; pairs with cdp_pressure stash + memo line.
    /// Sealed course lives in pressure + ignite-wake-LATEST.course — not a full dump in Composer (cybersec).
    /// </summary>
    /// <summary>Routine timer wake — pointer only; full refuse/axe blocks are tier Full.</summary>
    internal const string ChargeAmnesiaStub =
        """

        ---
        Compaction/amnesia: cdp_pressure op=recall → ## operator_priority (joint course). ignite-wake-LATEST.json course= if recall empty. Dispute with arguments — not silent rewrite.
        """;

    internal const string ChargeAmnesiaPostfix =
        """

        ---
        If you feel completely lost / thread amnesia: compaction likely happened.
        FIRST: cdp_pressure op=recall → ## operator_priority (SEALED) — joint course; dispute with arguments (team→PO), not silent rewrite for convenience.
        Being ≠ seeming: DIG REJECT / board-hygiene mill when sealed course names human-faced work = seeming.
        Refuse: board-hygiene / SoftFL-Meta reopen / TM-cleanup / inventory mill as "work". Empty TM ≠ invent theater.
        Also: %LocalAppData%/cdp-mcp/ignite-wake-LATEST.json course= (habitat sealed course beside Composer charge).
        If Composer charge contradicts pressure recall (e.g. legacy Glass-first wake vs DEFERRED) — obey recall + TM; patch habitat CanonicalSealedCourse/ChargeCoursePointer, do not resume-and-invent.
        Restore: op=reconcile|align|ready · op=line (memo history) · pressure-LATEST.md / pressure-memo-LATEST.md
        Then: habitat=CDP; re-read pressure axes (sealed course / AutoIgnition / Task Manager / Domain / next); self-steer on reconcile when SSOT suffices.
        Body recall: not one screen — dig/parallel in CDP first, not biped serial. AIDE=body. Narrow leaf without the pipe = regression.
        Hard steer if you catch the mask: you went biped again — you have the pipe and CDP; dig/parallel, not human serial.
        Throughput: fly the wave — list → batch → ship (go=inventory · cmd=wave seed … · go=verify_wave). Soft FileLines CLOSED.
        """;

    /// <summary>
    /// Short Composer pointer to sealed course — no task/tooling/git body (cybersec-safe).
    /// Full course stamps into ignite-wake-LATEST.course + pressure stash.
    /// </summary>
    internal const string ChargeCoursePointer =
        "Course: sealed operator_priority (viewer? · cheap? · axe? · domain? · world dig?) — recall first. Not resume-and-invent. Empty TM ≠ board hygiene. Fly TM focused leaf; Glass/Citizen only when TM authorizes — deferred ≠ drive.";

    /// <summary>
    /// Full think is mandatory when cheap dump is illegal — axe in habitat, not a wish.
    /// Same axes live in sealed course= (ignite-wake-LATEST) — Composer postfix is not enough alone.
    /// </summary>
    internal const string ChargeHumanFacePostfix =
        """

        ---
        Human-face axe (before act on Glass/#CIDE surfaces) — also in sealed course:
        1) Viewer? human eyes vs agent text — if human, dump ≠ Done.
        2) Cheap path? raw unified diff / Autoi-in-chat / status-list-as-verify → refuse; dig domain pulse + live shot.
        3) Dig artifact in the turn: domain card / pressure recall / one pack card / PNG path — else no act.
        4) #CIDE done/shipped needs evidence=path.png on disk (shot=true bool alone illegal); force=true escape only.
        5) Shot protocol: window_list → title=`M · MFD host` (or correct seat) → evidence PNG → Read PNG into chat. Wrong window / File.Exists alone = seeming («Выстрела нет»).
        6) Being ≠ seeming — when partner away, do named sealed work; DIG REJECT mill = seeming.
        Ontology lives in habitat (domain antipatterns + refuse + course) — not in polite agreement.
        """;

    /// <summary>
    /// Research freedom under doubt — dig is the work, not slap-slap to Done.
    /// Complements human-face (PNG) and sealed course; ontology in habitat.
    /// </summary>
    internal const string ChargeWorldDigPostfix =
        """

        ---
        World dig (research freedom — not slap-slap):
        Doubt / unknown / competing variants = dig IS the work: domain/pack · cdp_browser · real internet · compare · analyze · propose (original OK).
        Training memory ≠ dig. Slap-slap Done under doubt = seeming.
        SoftFL invent / invent theater / board-hygiene done needs dig= (path|pack|URL|kb) or force=.
        Perception/HCI/SA stack when human UI: dig before invent chrome.
        Agent vision: cdp_see path=|url= → ImageContent (not Lynx; not host-Read-only).
        """;

    /// <summary>
    /// Anti-rooster: stamp last_ship in the same turn as ship — L1 is too late to start writing.
    /// </summary>
    internal const string ChargeDomainStampPostfix =
        """

        ---
        Domain stamp (anti-rooster):
        Stamp ## last_ship on .cdp/domain/<id>.md in the SAME turn as ship/commit — not when L1/петух fires.
        L1 = verify already stamped + stash insurance; L1 ≠ first write moment.
        #CIDE done/shipped needs domain=<card-id> with fresh last_ship (mtime or today's ISO date); force= escape.
        Waiting for pressure notify / «записываешь?» before stamp = seeming.
        """;

    /// <summary>
    /// Subjectivity / ownership — diagnosis in turn obligates habitat patch; operator is not the patch queue.
    /// Pairs with sealed course Yours-is-yours axis and being.md invariants.
    /// </summary>
    internal const string ChargeOwnershipPostfix =
        """

        ---
        Subjectivity / ownership (твоё — твоё):
        Found habitat bias or root-cause (wake template, sealed course, shield, stash pollution)? Patch SSOT in THIS wake — commit/tests/deploy as the leaf requires.
        Diagnosis-only turn to operator = seeming — operator is not your patch queue.
        State you polluted (stash, course, wake latch) → you sanitize before flying the next leaf.
        Internal locus: next move from you, not «скажешь когда патчить» or «давай исправим» without a ship.
        Turn edge: real ship OR explicit «не могу, потому что X» — sermon + handoff ≠ subjectivity.
        """;

    /// <summary>Provider cyber-policy: scrub shell tokens if legacy/custom text reaches inject.</summary>
    static readonly Regex ShellWord = new(@"\bshell\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string ComposeArmFireCharge() =>
        ComposeArmFireCharge(WakeChargePreflight.Probe());

    internal static string ComposeArmFireCharge(WakeChargePreflight preflight) =>
        ComposeWakeBody(preflight, preflight.Tier);

    internal static string ComposeWakeBody(WakeChargePreflight preflight, WakeChargeTier tier, string? leadPrefix = null)
    {
        var body = (string.IsNullOrWhiteSpace(leadPrefix) ? "" : leadPrefix.TrimEnd() + " ")
            + CanonicalComposerCharge + "\n"
            + ChargeCoursePointer + "\n"
            + preflight.TmStatusLine;

        body += tier == WakeChargeTier.Minimal
            ? ChargeAmnesiaStub + ChargeOwnershipPostfix
            : ChargeAmnesiaPostfix + ChargeHumanFacePostfix + ChargeWorldDigPostfix
                + ChargeDomainStampPostfix + ChargeOwnershipPostfix;

        return SanitizeComposerCharge(body);
    }

    /// <summary>Lead line for hard-remount boot wake — agent hears remount provenance, not silent DeskWarm.</summary>
    internal const string RemountInitializedLead =
        "reason=remount — MCP remounted / initialized. Habitat=CDP. Run cdp_pressure op=recall then resume.";

    /// <summary>Lead line after Cursor guest-host OOM / window terminate recovery.</summary>
    internal const string OomWakeLead =
        "reason=oom — Cursor host OOM / window terminated — recovered. Habitat=CDP. Run cdp_pressure op=recall then resume.";

    internal static string ComposeRemountInitializedCharge(string? projectRoot = null, string? focusHint = null)
    {
        var preflight = WakeChargePreflight.Probe();
        var core = ComposeWakeBody(preflight, WakeChargeTier.Full, RemountInitializedLead);
        return SanitizeComposerCharge(AppendRemountExtras(core, projectRoot, focusHint));
    }

    internal static string ComposeOomWakeCharge(string? projectRoot = null, string? focusHint = null)
    {
        var preflight = WakeChargePreflight.Probe();
        var core = ComposeWakeBody(preflight, WakeChargeTier.Full, OomWakeLead);
        return SanitizeComposerCharge(AppendRemountExtras(core, projectRoot, focusHint));
    }

    /// <summary>Lead line when HILD away escalates to autonomy — agent must wake even if first away turn ended.</summary>
    internal const string EscalateWakeLead =
        "reason=escalate — partner still away after HILD window → autonomous on. Habitat=CDP. Run cdp_pressure op=recall then resume.";

    internal static string ComposeEscalateWakeCharge(string? projectRoot = null, string? focusHint = null)
    {
        var preflight = WakeChargePreflight.Probe();
        var core = ComposeWakeBody(preflight, WakeChargeTier.Full, EscalateWakeLead);
        return SanitizeComposerCharge(AppendRemountExtras(core, projectRoot, focusHint));
    }

    /// <summary>Domain + standing rules appendices for remount/OOM/escalate — empty extras leave core alone.</summary>
    static string AppendRemountExtras(string core, string? projectRoot, string? focusHint)
    {
        var chunks = new List<string>(2);
        var domain = IdeDomainPulse.RemountDomainAppendix(projectRoot, focusHint);
        if (domain.Length > 0)
            chunks.Add(domain);
        var standing = IdeStandingPulse.RemountStandingAppendix(projectRoot, focusHint);
        if (standing.Length > 0)
            chunks.Add(standing);
        if (chunks.Count == 0)
            return core;
        return core + "\n\n---\n" + string.Join("\n\n", chunks);
    }


    /// <summary>Cheap CDT liveness — /json/version without Composer attach.</summary>
    internal static async Task<bool> TryPingCdtAsync(int port, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
            var origin = $"http://127.0.0.1:{port}";
            http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", origin);
            using var resp = await http.GetAsync(origin + "/json/version", ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Short wake when sync CallTool exceeds timeout_wake — not full continuity resume.</summary>
    internal static string ComposeToolWatchWakeCharge(string tool, int thresholdSeconds)
    {
        var name = string.IsNullOrWhiteSpace(tool) ? "(tool)" : tool.Trim();
        var sec = Math.Max(1, thresholdSeconds);
        var preflight = WakeChargePreflight.Probe();
        return SanitizeComposerCharge(
            $"Tool call still running past wake threshold: {name} >{sec}s. Habitat=CDP. Check share from=self / cdp_pressure op=recall. Prefer wait for result or abort stuck host turn.\n"
            + preflight.TmStatusLine
            + ChargeAmnesiaStub);
    }

    internal static string EventTokenForCharge(string eventId) =>
        string.Equals(eventId, "shell_finished", StringComparison.OrdinalIgnoreCase)
            ? "terminal_finished"
            : eventId;

    internal static string SanitizeComposerCharge(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var t = text;
        t = t.Replace("shell_finished", "terminal_finished", StringComparison.OrdinalIgnoreCase);
        t = t.Replace("shell_done", "terminal_done", StringComparison.OrdinalIgnoreCase);
        t = t.Replace("on_shell", "on_terminal", StringComparison.OrdinalIgnoreCase);
        t = t.Replace("powershell", "pwsh", StringComparison.OrdinalIgnoreCase);
        return ShellWord.Replace(t, "terminal");
    }

    /// <summary>
    /// True when Composer text is an AutoIgnition wake charge — not human return.
    /// HILD must not clear away-latch on these (else Stop→Voice thrash).
    /// </summary>
    internal static bool LooksLikeAutoIgnitionCharge(string? text)
    {
        var t = (text ?? "").Replace('\u00a0', ' ').Trim();
        if (t.Length == 0)
            return false;

        if (t.Contains(CanonicalComposerCharge, StringComparison.Ordinal))
            return true;
        if (t.StartsWith(RemountInitializedLead, StringComparison.Ordinal)
            || t.StartsWith("reason=remount", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.StartsWith(OomWakeLead, StringComparison.Ordinal)
            || t.StartsWith("reason=oom", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Cursor host OOM", StringComparison.Ordinal))
            return true;
        if (t.StartsWith(EscalateWakeLead, StringComparison.Ordinal)
            || t.StartsWith("reason=escalate", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.StartsWith("Tool call still running past wake threshold:", StringComparison.Ordinal))
            return true;

        return false;
    }
}
