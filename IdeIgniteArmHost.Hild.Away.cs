#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// HILD away edge → escalate → seed wake (partial of <see cref="IdeIgniteArmHost"/>).
/// </summary>
internal static partial class IdeIgniteArmHost
{
    static void OnPartnerHere()
    {
        lock (HildGate)
        {
            AwayEscalateDueUtc = null;
            AwayEscalateDone = false;
        }

        IdeHildCrossProcessClaim.ClearAwayEpoch();
        IdeTeethTape.Record("partner_here", detail: "hild_latch_cleared");
        Console.Error.WriteLine("[ide_ignite] hild partner here — away latch cleared");
    }

    static void OnHumanAwayEdge()
    {
        lock (HildGate)
        {
            HildLastEdgeUtc = DateTimeOffset.UtcNow;
            HildEdgeCount++;
            AwayEscalateDueUtc = DateTimeOffset.UtcNow + AwayEscalateAfter;
            AwayEscalateDone = false;
        }

        // Partner gone — pull long last_once parks forward (HILD is not only a wake seed).
        PullForwardLongWorkTimersOnHildAway();

        // Zombie remounts: only one process seeds Composer wake for this absence.
        if (!IdeHildCrossProcessClaim.TryClaimAwayEdge())
        {
            IdeTeethTape.Record("partner_away", detail: "edge_claim_lost");
            return;
        }

        IdeTeethTape.Record("partner_away", detail: $"escalate_in={(int)AwayEscalateAfter.TotalSeconds}s");

        Console.Error.WriteLine(
            $"[ide_ignite] hild human_away edge #{HildEdgeCount} — status away; escalate@{AwayEscalateAfter.TotalSeconds:0}s");

        if (HasAwaitingOperatorLatch())
        {
            Console.Error.WriteLine("[ide_ignite] hild wake suppressed — await_operator latch");
            return;
        }

        if (HasArmedOomWake() || HasArmedRemountWake())
        {
            Console.Error.WriteLine("[ide_ignite] hild wake suppressed — oom/remount-wake armed");
            return;
        }

        // Invent-only Hold insurance already covers continuity — HILD away CDT = DIG REJECT thrash
        // (lived 2026-08-07: invent-only 15m armed → hild-away wake ~1m later).
        if (HasArmedInventOnlyHoldInsurance())
        {
            IdeTeethTape.Record("wake_suppress", armId: HildAwayArmId, reason: "hild", detail: "invent_only_hold_insurance");
            Console.Error.WriteLine("[ide_ignite] hild wake suppressed — invent-only Hold insurance armed");
            return;
        }

        SeedHildWake(out var hildArmId);
        IdeTeethTape.Record("wake_schedule", armId: hildArmId, reason: "hild", detail: "human_away");
    }

    /// <summary>
    /// Still away after <see cref="AwayEscalateAfter"/> → autonomy + escalate wake (reason=escalate).
    /// Autonomy latch alone is not enough — agent must receive a Composer charge if the first away turn ended.
    /// </summary>
    static void TryEscalateAwayToAutonomy()
    {
        // Claim under one lock — TOCTOU here scheduled a storm of escalate arms (dogfood 0.5.341).
        lock (HildGate)
        {
            if (AwayEscalateDone || AwayEscalateDueUtc is null || !HildDetector.AwayLatched)
                return;
            if (DateTimeOffset.UtcNow < AwayEscalateDueUtc.Value)
                return;
            AwayEscalateDone = true;
        }

        // Cross-process: only one of N zombie CdpMcp schedules escalate (dogfood 0.5.342).
        if (!IdeHildCrossProcessClaim.TryClaimEscalate())
        {
            IdeTeethTape.Record("partner_away_escalate", detail: "escalate_claim_lost");
            return;
        }

        IdeTeethTape.Record("partner_away_escalate", detail: "still_away→autonomy+wake");
        SetAutonomous(true, "hild_away_escalate");
        // Agent may have re-armed long last_once after first away wake — pull again.
        PullForwardLongWorkTimersOnHildAway();
        var scheduled = TryScheduleHildEscalateWake();
        if (TryArmId(scheduled) is { } aid)
            IdeTeethTape.Record("wake_schedule", armId: aid, reason: HildEscalateReason, detail: "away_escalate");
        Console.Error.WriteLine(
            $"[ide_ignite] hild away escalate — still away after {AwayEscalateAfter.TotalSeconds:0}s → autonomous on + escalate wake");
    }

    /// <summary>Pull armed last_once work timers with DueUtc &gt; 3s forward — HILD away ≠ license for 45m park.
    /// Skip invent-only Hold arms (≤15m insurance; DIG REJECT mill ≠ park).</summary>
    static void PullForwardLongWorkTimersOnHildAway() =>
        PullForwardLongWorkTimers(
            compute: TryComputeHildAwayPullForwardDue,
            lastError: "hild_away_pull_forward",
            tape: "hild_pull_forward",
            log: "hild",
            skip: static a => IsInventOnlyHoldTask(a.Task));

    /// <summary>Pull long last_once while TM ContinuityFlight.Fly under autonomous — agent-park police.
    /// Skip invent-only Hold arms (≤15m insurance; DIG REJECT mill ≠ park).</summary>
    static void PullForwardLongWorkTimersOnLeafFly() =>
        PullForwardLongWorkTimers(
            compute: TryComputeLeafFlyPullForwardDue,
            lastError: "leaf_fly_pull_forward",
            tape: "leaf_pull_forward",
            log: "leaf Fly",
            skip: static a => IsInventOnlyHoldTask(a.Task));

    delegate bool HabitPullForwardCompute(
        DateTimeOffset? dueUtc,
        bool lastOnce,
        bool isAutonomyMeans,
        string status,
        string? eventKind,
        DateTimeOffset now,
        out DateTimeOffset newDue,
        out string? note);

    static void PullForwardLongWorkTimers(
        HabitPullForwardCompute compute,
        string lastError,
        string tape,
        string log,
        Func<IgniteArm, bool>? skip = null)
    {
        EnsureLoaded();
        var now = DateTimeOffset.UtcNow;
        var pulled = 0;
        lock (Gate)
        {
            foreach (var a in Arms)
            {
                if (skip?.Invoke(a) == true)
                    continue;

                if (!compute(
                        a.DueUtc,
                        a.LastOnce,
                        IsAutonomyMeansArm(a),
                        a.Status,
                        a.Event,
                        now,
                        out var newDue,
                        out var note))
                    continue;

                a.DueUtc = newDue;
                a.InRaw = string.IsNullOrWhiteSpace(a.InRaw) ? note : $"{a.InRaw}→{note}";
                a.LastError = lastError;
                pulled++;
            }

            if (pulled > 0)
                PersistUnlocked();
        }

        if (pulled <= 0)
            return;

        IdeTeethTape.Record(tape, detail: $"count={pulled}");
        Console.Error.WriteLine(
            $"[ide_ignite] {log} pull-forward · {pulled} last_once work timer(s) → ≤{HildAwayContinuityMax.TotalSeconds:0}s");
    }

    /// <summary>One-shot timer charge_mode=escalate (system wake — not superseded).
    /// Skip when invent-only Hold insurance already armed (DIG REJECT thrash; autonomy still latched above).</summary>
    internal static object? TryScheduleHildEscalateWake()
    {
        EnsureLoaded();
        EnsureStarted();
        if (HasArmedInventOnlyHoldInsurance())
        {
            IdeTeethTape.Record(
                "wake_suppress", armId: HildEscalateArmId, reason: HildEscalateReason, detail: "invent_only_hold_insurance");
            Console.Error.WriteLine("[ide_ignite] hild escalate wake suppressed — invent-only Hold insurance armed");
            return null;
        }

        var dueSec = 2;
        var now = DateTimeOffset.UtcNow;

        IgniteArm arm;
        lock (Gate)
        {
            // Already armed/firing — do not reset DueUtc (TimerLoop storm under replace).
            var existing = Arms.FirstOrDefault(a =>
                a.Id.Equals(HildEscalateArmId, StringComparison.OrdinalIgnoreCase)
                && a.Status is "armed" or "firing");
            if (existing is not null)
                return Slim(existing);

            Arms.RemoveAll(a =>
                a.Id.StartsWith(HildEscalateArmIdPrefix, StringComparison.OrdinalIgnoreCase));

            arm = new IgniteArm
            {
                Id = HildEscalateArmId,
                Event = "timer",
                Message = IdeIgniteChannel.ComposeEscalateWakeCharge(),
                ChargeMode = HildEscalateChargeMode,
                Task = HildEscalateArmTask,
                Reason = HildEscalateReason,
                Once = true,
                LastOnce = false,
                OkOnly = true,
                SettleSeconds = 1,
                WaitSeconds = 90,
                DueUtc = now + TimeSpan.FromSeconds(dueSec),
                InRaw = $"{dueSec}s",
                Status = "armed",
                CreatedUtc = now,
                LastError = "hild_away_escalate"
            };
            Arms.Add(arm);
            PersistUnlocked();
        }

        return Slim(arm);
    }

    static bool HasArmedOomWake()
    {
        EnsureLoaded();
        lock (Gate)
            return Arms.Any(a =>
                a.Id.StartsWith(IdeOomWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase)
                && a.Status is "armed" or "firing");
    }

    internal static bool HasArmedRemountWake()
    {
        EnsureLoaded();
        lock (Gate)
            return Arms.Any(a =>
                a.Id.StartsWith(IdeRemountWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase)
                && a.Status is "armed" or "firing");
    }

    /// <summary>Invent-only Hold continuity timer armed/firing — HILD away/escalate Composer wake is DIG REJECT thrash.</summary>
    internal static bool HasArmedInventOnlyHoldInsurance()
    {
        EnsureLoaded();
        lock (Gate)
            return Arms.Any(a =>
                a.Status is "armed" or "firing"
                && string.Equals(a.Event, "timer", StringComparison.OrdinalIgnoreCase)
                && IsInventOnlyHoldTask(a.Task));
    }

    /// <summary>Any last_once timer insurance armed/firing — remount Composer wake after Recover mid SoftFL = DIG REJECT thrash.</summary>
    internal static bool HasArmedLastOnceInsurance()
    {
        EnsureLoaded();
        lock (Gate)
            return Arms.Any(a =>
                a.LastOnce
                && a.Status is "armed" or "firing"
                && string.Equals(a.Event, "timer", StringComparison.OrdinalIgnoreCase));
    }

    static bool HasAwaitingOperatorLatch()
    {
        EnsureLoaded();
        lock (Gate)
            return Arms.Any(a => a.Status == "awaiting");
    }

    static void SeedHildWake(out string armId)
    {
        armId = HildAwayArmId;
        try
        {
            EnsureLoaded();
            lock (Gate)
            {
                var live = Arms.FirstOrDefault(a =>
                    a.Id.Equals(HildAwayArmId, StringComparison.OrdinalIgnoreCase)
                    && a.Status is "armed" or "firing");
                if (live is not null)
                    return;

                Arms.RemoveAll(a =>
                    a.Id.Equals(HildAwayArmId, StringComparison.OrdinalIgnoreCase)
                    || a.Id.StartsWith(HildArmIdPrefix, StringComparison.OrdinalIgnoreCase));
            }

            var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["op"] = JsonSerializer.SerializeToElement("arm"),
                ["when"] = JsonSerializer.SerializeToElement("timer"),
                ["in"] = JsonSerializer.SerializeToElement("1s"),
                ["once"] = JsonSerializer.SerializeToElement(true),
                ["charge"] = JsonSerializer.SerializeToElement("minimal"),
                ["task"] = JsonSerializer.SerializeToElement("HILD human_away"),
                ["id"] = JsonSerializer.SerializeToElement(armId),
                ["settle_seconds"] = JsonSerializer.SerializeToElement(1)
            };
            _ = IdeIgniteChannel.HandleJson(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ide_ignite] hild seed wake failed: {ex.Message}");
        }
    }
}
