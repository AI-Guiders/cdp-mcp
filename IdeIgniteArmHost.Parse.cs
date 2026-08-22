#nullable enable
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    /// <summary>Parse arm args into a new IgniteArm (does not persist).</summary>
    static bool TryCreateArm(IReadOnlyDictionary<string, JsonElement> args, out IgniteArm arm, out object? err)
    {
        arm = null!;
        err = null;
        var message = Opt(args, "message") ?? Opt(args, "text") ?? Opt(args, "msg") ?? Opt(args, "prompt");
        var task = Opt(args, "task") ?? Opt(args, "next") ?? Opt(args, "label");
        if (string.IsNullOrWhiteSpace(message) && string.IsNullOrWhiteSpace(task))
        {
            err = Err("arm", "message_or_task_required", "arm task=… (TM label) and/or when=…; composer charge is canonical wake text");
            return false;
        }

        var when = NormalizeEvent(Opt(args, "when") ?? Opt(args, "event") ?? Opt(args, "on") ?? "timer");
        var inRaw = Opt(args, "in") ?? Opt(args, "after") ?? Opt(args, "delay");
        var port = OptInt(args, "port") ?? IdeIgniteChannel.DefaultPort;
        var chat = Opt(args, "chat") ?? Opt(args, "title");
        if (string.IsNullOrWhiteSpace(chat))
            chat = CdpTenantComposerLatch.ResolveDefaultChat(
                CdpTenantExecutionContext.CurrentSlice?.Key.BridgeSession,
                CdpTenantRoutingContext.CurrentConversationId);
        var once = OptBool(args, "once") ?? true;
        var lastOnce = ResolveLastOnce(args);
        if (lastOnce) once = true; // last_once implies once
        var okOnly = OptBool(args, "ok_only") ?? true;
        var settle = OptInt(args, "settle_seconds") ?? (when is "timer" or "manual" ? 2 : 8);
        var wait = OptInt(args, "wait_seconds") ?? 90;
        var force = OptBool(args, "force") == true;
        var id = Opt(args, "id");
        if (string.IsNullOrWhiteSpace(id))
            id = "arm-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                 Guid.NewGuid().ToString("N")[..6];

        if (lastOnce && !force && ProbeFlight() == ContinuityFlight.NoActiveTask)
        {
            err = Err(
                "arm",
                "no_active_task",
                "last_once on plateau needs an active TM task; focus/seed task first, or force=true for an explicit override");
            return false;
        }

        DateTimeOffset? due = null;
        string? clampNote = null;
        if (when == "timer")
        {
            if (!TryParseDue(inRaw, Opt(args, "at"), out due, out var perr))
            {
                err = Err("arm", "bad_timer", perr);
                return false;
            }

            var partnerAway = HildDetector.AwayLatched;
            var leafFlying = ProbeFlight() == ContinuityFlight.Fly;
            var inventOnlyHold = IsInventOnlyHoldTask(task);
            if (due is { } due0
                && TryClampAutonomousLastOnceDue(
                    due0,
                    lastOnce,
                    IsAutonomousArmed(),
                    force,
                    out var clampedDue,
                    out clampNote,
                    partnerAway,
                    leafFlying,
                    inventOnlyHold))
            {
                due = clampedDue;
                inRaw = string.IsNullOrWhiteSpace(inRaw) ? clampNote : $"{inRaw}→{clampNote}";
            }
        }
        else if (!string.IsNullOrWhiteSpace(inRaw) && TryParseDuration(inRaw!, out var d))
        {
            d = ClampAutonomousLastOnceInsurance(
                d, lastOnce, IsAutonomousArmed(), force, out clampNote,
                HildDetector.AwayLatched, ProbeFlight() == ContinuityFlight.Fly,
                IsInventOnlyHoldTask(task));
            due = DateTimeOffset.UtcNow + d;
            if (clampNote is not null)
                inRaw = $"{inRaw}→{clampNote}";
        }

        if (string.IsNullOrWhiteSpace(message))
            message = IdeIgniteChannel.CanonicalComposerCharge;

        var chargeMode = (Opt(args, "charge") ?? "minimal").Trim().ToLowerInvariant();

        arm = new IgniteArm
        {
            Id = id!,
            Event = when,
            Message = chargeMode is "custom" or "expand" or "legacy" ? message! : IdeIgniteChannel.CanonicalComposerCharge,
            ChargeMode = chargeMode,
            Task = task,
            Chat = chat,
            Port = port,
            Once = once,
            LastOnce = lastOnce,
            OkOnly = okOnly,
            SettleSeconds = Math.Clamp(settle, 0, 120),
            WaitSeconds = Math.Clamp(wait, 5, 600),
            DueUtc = due,
            InRaw = inRaw,
            Status = "armed",
            CreatedUtc = DateTimeOffset.UtcNow
        };
        StampTenantWire(arm);
        return true;
    }

    static bool ResolveLastOnce(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (OptBool(args, "last_once") == true) return true;
        if (OptBool(args, "await_partner") == true || OptBool(args, "await_operator") == true) return true;
        var mode = (Opt(args, "mode") ?? "").Trim().ToLowerInvariant().Replace('-', '_');
        if (mode is "await" or "await_partner" or "await_operator" or "last_once" or "idle" or "halt") return true;
        return false;
    }

        public static string NormalizeEvent(string? raw)
    {
        var e = (raw ?? "").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return e switch
        {
            "build" or "build_done" or "build_ok" or "build_finished" or "on_build" => "build_finished",
            "test" or "tests" or "test_done" or "test_finished" or "on_test" => "test_finished",
            "shell" or "shell_done" or "shell_finished" or "on_shell" => "shell_finished",
            "peer_ship" or "peer" or "leaf_done" or "leaf_ship" or "ship" or "shipped" or "hih_ship" => "peer_ship",
            "hild" or "human_away" or "hitl" or "human_idle" or "away" => "human_away",
            "time" or "delay" or "sleep" or "timer" or "in" => "timer",
            "manual" or "now" or "fire" => "manual",
            _ when e.Length == 0 => "timer",
            _ => e
        };
    }


    public static bool TryParseDuration(string raw, out TimeSpan span)
    {
        span = default;
        var s = raw.Trim().ToLowerInvariant();
        var m = Regex.Match(s, @"^(\d+)\s*(ms|s|m|h|d|sec|secs|second|seconds|min|mins|minute|minutes|hr|hrs|hour|hours|day|days)?$");
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, out var n) || n < 0) return false;
        var u = m.Groups[2].Value;
        span = u switch
        {
            "ms" => TimeSpan.FromMilliseconds(n),
            "" or "s" or "sec" or "secs" or "second" or "seconds" => TimeSpan.FromSeconds(n),
            "m" or "min" or "mins" or "minute" or "minutes" => TimeSpan.FromMinutes(n),
            "h" or "hr" or "hrs" or "hour" or "hours" => TimeSpan.FromHours(n),
            "d" or "day" or "days" => TimeSpan.FromDays(n),
            _ => TimeSpan.Zero
        };
        return span > TimeSpan.Zero || n == 0;
    }

    /// <summary>Under autonomous, last_once insurance must be short — 45m park looks like "working".</summary>
    internal static readonly TimeSpan AutonomousLastOnceInsuranceMax = TimeSpan.FromMinutes(3);

    /// <summary>Invent-only Hold insurance — longer than work last_once ≤3m. Lived: ≤3m → Recover/DIG REJECT mill every wake under overnight Hold (zombie MCP + Hold invent). SoftFL REJECT.</summary>
    internal static readonly TimeSpan InventOnlyHoldInsuranceMax = TimeSpan.FromMinutes(15);

    /// <summary>While HILD away_latched / on human_away edge — last_once work timers ≤3s (habit), not ≤3m.</summary>
    internal static readonly TimeSpan HildAwayContinuityMax = TimeSpan.FromSeconds(3);

    /// <summary>Hold invent-only leaf — DIG REJECT mill must not be forced by ≤3s leaf_pull / hild_pull (VL#47 park police stays for active work leaves).
    /// Match both "invent only" and hyphenated "invent-only" (lived Hold title 2026-08-05).</summary>
    internal static bool IsInventOnlyHoldTask(string? task) =>
        !string.IsNullOrWhiteSpace(task)
        && (task.Contains("invent only", StringComparison.OrdinalIgnoreCase)
            || task.Contains("invent-only", StringComparison.OrdinalIgnoreCase));

    /// <summary>Clamp long last_once timers under autonomous unless force=true.
    /// Partner-away or TM leaf Fly tightens to 3s (habit); invent-only Hold keeps ≤15m; else ≤3m.</summary>
    internal static TimeSpan ClampAutonomousLastOnceInsurance(
        TimeSpan requested,
        bool lastOnce,
        bool autonomous,
        bool force,
        out string? clampNote,
        bool partnerAway = false,
        bool leafFlying = false,
        bool inventOnlyHold = false)
    {
        clampNote = null;
        if (!lastOnce || !autonomous || force)
            return requested;
        TimeSpan max;
        string note;
        if (inventOnlyHold)
        {
            max = InventOnlyHoldInsuranceMax;
            note = "15m(invent_only_hold)";
        }
        else if (partnerAway)
        {
            max = HildAwayContinuityMax;
            note = "3s(hild_away)";
        }
        else if (leafFlying)
        {
            max = HildAwayContinuityMax;
            note = "3s(leaf_started)";
        }
        else
        {
            max = AutonomousLastOnceInsuranceMax;
            note = "3m(clamped)";
        }

        if (requested <= max)
            return requested;
        clampNote = note;
        return max;
    }

    /// <summary>Pure: pull long armed last_once work timer forward to habit ≤3s (not means arms).</summary>
    internal static bool TryComputeHabitPullForwardDue(
        DateTimeOffset? dueUtc,
        bool lastOnce,
        bool isAutonomyMeans,
        string status,
        string? eventKind,
        DateTimeOffset now,
        string pullNote,
        out DateTimeOffset newDue,
        out string? note)
    {
        newDue = default;
        note = null;
        if (!lastOnce || isAutonomyMeans || status is not "armed")
            return false;
        if (!string.Equals(eventKind, "timer", StringComparison.OrdinalIgnoreCase))
            return false;
        if (dueUtc is null)
            return false;
        var remaining = dueUtc.Value - now;
        if (remaining <= HildAwayContinuityMax)
            return false;
        newDue = now + HildAwayContinuityMax;
        note = pullNote;
        return true;
    }

    /// <summary>Pure: HILD away pull — note 3s(hild_pull).</summary>
    internal static bool TryComputeHildAwayPullForwardDue(
        DateTimeOffset? dueUtc,
        bool lastOnce,
        bool isAutonomyMeans,
        string status,
        string? eventKind,
        DateTimeOffset now,
        out DateTimeOffset newDue,
        out string? note) =>
        TryComputeHabitPullForwardDue(
            dueUtc, lastOnce, isAutonomyMeans, status, eventKind, now,
            "3s(hild_pull)", out newDue, out note);

    /// <summary>Pure: TM leaf Fly pull — note 3s(leaf_pull).</summary>
    internal static bool TryComputeLeafFlyPullForwardDue(
        DateTimeOffset? dueUtc,
        bool lastOnce,
        bool isAutonomyMeans,
        string status,
        string? eventKind,
        DateTimeOffset now,
        out DateTimeOffset newDue,
        out string? note) =>
        TryComputeHabitPullForwardDue(
            dueUtc, lastOnce, isAutonomyMeans, status, eventKind, now,
            "3s(leaf_pull)", out newDue, out note);

    internal static bool TryClampAutonomousLastOnceDue(
        DateTimeOffset dueUtc,
        bool lastOnce,
        bool autonomous,
        bool force,
        out DateTimeOffset clampedDue,
        out string? clampNote,
        bool partnerAway = false,
        bool leafFlying = false,
        bool inventOnlyHold = false)
    {
        clampNote = null;
        clampedDue = dueUtc;
        var remaining = dueUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return false;
        var clamped = ClampAutonomousLastOnceInsurance(
            remaining, lastOnce, autonomous, force, out clampNote, partnerAway, leafFlying, inventOnlyHold);
        if (clampNote is null)
            return false;
        clampedDue = DateTimeOffset.UtcNow + clamped;
        return true;
    }

    static bool TryParseDue(string? inRaw, string? atRaw, out DateTimeOffset? due, out string error)
    {
        due = null;
        error = "";
        if (!string.IsNullOrWhiteSpace(atRaw)
            && DateTimeOffset.TryParse(atRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at))
        {
            due = at;
            return true;
        }

        if (string.IsNullOrWhiteSpace(inRaw))
        {
            error = "timer requires in=30s|5m|2h or at=ISO-8601";
            return false;
        }

        if (!TryParseDuration(inRaw!, out var d))
        {
            error = $"bad duration '{inRaw}' (use 30s|5m|2h)";
            return false;
        }

        due = DateTimeOffset.UtcNow + d;
        return true;
    }

}
