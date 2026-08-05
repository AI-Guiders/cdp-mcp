#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Black-box FDR (L3): dense append-only tool-call flight tape under workspace state.
/// Storage-first — not a chat dump. Feeds incident recall + timeout_wake suggest/apply overlay.
/// </summary>
internal static partial class IdeFlightDataRecorder
{
    public const string Schema = "fdr_event/v1";
    public const int DefaultMaxLines = 2000;

    static readonly object Gate = new();
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static Func<FdrContextSnap>? s_context;
    internal static string? PathOverrideForTests { get; set; }
    internal static bool SuppressWriteForTests { get; set; }

    /// <summary>
    /// Seat-local tape (ADR dual-seat): <c>StateRoot/{seat}/fdr-tape.jsonl</c>.
    /// Legacy workspace-root path migrates once on primary (see Tape.cs).
    /// </summary>
    public static string TapePath =>
        PathOverrideForTests
        ?? Path.Combine(CdpProfile.StateRoot, IdeIgniteArmHost.Seat, "fdr-tape.jsonl");

    /// <summary>Pre-seat-layout path (workspace StateRoot only).</summary>
    public static string LegacyTapePath =>
        Path.Combine(CdpProfile.StateRoot, "fdr-tape.jsonl");

    public static void BindContext(Func<FdrContextSnap>? snap) => s_context = snap;

    public readonly record struct FdrContextSnap(
        string? Phase,
        string? Object,
        string? Language,
        string? ProjectLeaf);

    public sealed class FdrEvent
    {
        public string Schema { get; set; } = IdeFlightDataRecorder.Schema;
        public string Kind { get; set; } = "tool_call";
        public string CallId { get; set; } = "";
        public string Tool { get; set; } = "";
        public string? Op { get; set; }
        public string? Go { get; set; }
        public string[]? ArgKeys { get; set; }
        public int ThresholdS { get; set; }
        public int ElapsedMs { get; set; }
        public string Outcome { get; set; } = "ok"; // ok|error|cancel|running
        public bool WakeExceeded { get; set; }
        public string? Error { get; set; }
        public int ResultChars { get; set; }
        public string? Phase { get; set; }
        public string? Object { get; set; }
        public string? Language { get; set; }
        public string? Project { get; set; }
        public string AtUtc { get; set; } = "";
    }

    public const string KindToolCall = "tool_call";
    public const string KindToolStart = "tool_start";
    public const string KindToolTick = "tool_tick";
    public const string OutcomeRunning = "running";

    /// <summary>
    /// Closed flight row only (latency/outcome). Starts/ticks are dynamics — excluded from p50/p95.
    /// </summary>
    public static bool IsClosedToolCall(FdrEvent e)
    {
        var kind = e.Kind?.Trim() ?? "";
        if (kind.Length != 0
            && !string.Equals(kind, KindToolCall, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(e.Outcome, OutcomeRunning, StringComparison.OrdinalIgnoreCase))
            return false;
        return !string.IsNullOrWhiteSpace(e.Tool);
    }

    public static bool IsDynamicsEvent(FdrEvent e)
    {
        var kind = e.Kind?.Trim() ?? "";
        return string.Equals(kind, KindToolStart, StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, KindToolTick, StringComparison.OrdinalIgnoreCase);
    }

    static bool IsSelfDeskTool(string? tool) =>
        string.Equals(tool, IdeFdrChannel.ToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(tool, "cdp_fdr", StringComparison.OrdinalIgnoreCase)
        || string.Equals(tool, IdeTeethChannel.ToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(tool, "cdp_teeth", StringComparison.OrdinalIgnoreCase)
        || string.Equals(tool, IdePostmortemChannel.ToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(tool, "cdp_postmortem", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// In-flight stamp at CallTool begin — survives host abort that never reaches finally
    /// (ghost hang dig via open flights / matching call_id close).
    /// </summary>
    public static void RecordToolStart(
        string tool,
        string callId,
        IReadOnlyDictionary<string, JsonElement> args,
        int thresholdSeconds)
    {
        if (SuppressWriteForTests || IsSelfDeskTool(tool))
            return;

        FdrContextSnap? snap = null;
        try { snap = s_context?.Invoke(); } catch { /* best-effort */ }

        Append(new FdrEvent
        {
            Kind = KindToolStart,
            CallId = callId,
            Tool = tool ?? "",
            Op = OptArg(args, "op") ?? OptArg(args, "cmd"),
            Go = OptArg(args, "go"),
            ArgKeys = args.Count == 0
                ? null
                : args.Keys.OrderBy(k => k, StringComparer.Ordinal).Take(24).ToArray(),
            ThresholdS = thresholdSeconds,
            ElapsedMs = 0,
            Outcome = OutcomeRunning,
            Phase = snap?.Phase,
            Object = snap?.Object,
            Language = snap?.Language,
            Project = snap?.ProjectLeaf,
            AtUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        });
    }

    /// <summary>
    /// Mid-flight dynamics sample (real FDR path) — elapsed while still running.
    /// Crash dig reads the tick trail even when close never lands.
    /// </summary>
    public static void RecordToolTick(
        string tool,
        string callId,
        IReadOnlyDictionary<string, JsonElement> args,
        int thresholdSeconds,
        int elapsedMs,
        bool wakeExceeded)
    {
        if (SuppressWriteForTests || IsSelfDeskTool(tool))
            return;

        FdrContextSnap? snap = null;
        try { snap = s_context?.Invoke(); } catch { /* best-effort */ }

        Append(new FdrEvent
        {
            Kind = KindToolTick,
            CallId = callId,
            Tool = tool ?? "",
            Op = OptArg(args, "op") ?? OptArg(args, "cmd"),
            Go = OptArg(args, "go"),
            ArgKeys = args.Count == 0
                ? null
                : args.Keys.OrderBy(k => k, StringComparer.Ordinal).Take(24).ToArray(),
            ThresholdS = thresholdSeconds,
            ElapsedMs = Math.Max(0, elapsedMs),
            Outcome = OutcomeRunning,
            WakeExceeded = wakeExceeded,
            Phase = snap?.Phase,
            Object = snap?.Object,
            Language = snap?.Language,
            Project = snap?.ProjectLeaf,
            AtUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        });
    }

    public static void RecordToolCall(
        string tool,
        string callId,
        IReadOnlyDictionary<string, JsonElement> args,
        int thresholdSeconds,
        int elapsedMs,
        string outcome,
        bool wakeExceeded,
        string? error,
        int resultChars)
    {
        if (SuppressWriteForTests || IsSelfDeskTool(tool))
            return;

        FdrContextSnap? snap = null;
        try { snap = s_context?.Invoke(); } catch { /* best-effort */ }

        var ev = new FdrEvent
        {
            Kind = KindToolCall,
            CallId = callId,
            Tool = tool ?? "",
            Op = OptArg(args, "op") ?? OptArg(args, "cmd"),
            Go = OptArg(args, "go"),
            ArgKeys = args.Count == 0 ? null : args.Keys.OrderBy(k => k, StringComparer.Ordinal).Take(24).ToArray(),
            ThresholdS = thresholdSeconds,
            ElapsedMs = Math.Max(0, elapsedMs),
            Outcome = outcome,
            WakeExceeded = wakeExceeded,
            Error = Truncate(error, 240),
            ResultChars = Math.Max(0, resultChars),
            Phase = snap?.Phase,
            Object = snap?.Object,
            Language = snap?.Language,
            Project = snap?.ProjectLeaf,
            AtUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };

        Append(ev);
    }

    /// <summary>Wake lifecycle on FDR tape — arm / cancel / suppress (not tool_call).</summary>
    public static void RecordWake(string kind, string callOrArmId, string? tool, string? detail = null)
    {
        if (SuppressWriteForTests)
            return;

        FdrContextSnap? snap = null;
        try { snap = s_context?.Invoke(); } catch { /* best-effort */ }

        Append(new FdrEvent
        {
            Kind = string.IsNullOrWhiteSpace(kind) ? "wake" : kind.Trim(),
            CallId = callOrArmId ?? "",
            Tool = tool ?? "",
            Outcome = kind ?? "",
            Error = Truncate(detail, 240),
            Phase = snap?.Phase,
            Object = snap?.Object,
            Language = snap?.Language,
            Project = snap?.ProjectLeaf,
            AtUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        });
    }


    /// <summary>
    /// Starts without a later closed <see cref="KindToolCall"/> for the same call_id —
    /// ghost hang when host aborts without finally.
    /// Includes last mid-flight tick when present (dynamics trail).
    /// </summary>
    public static IReadOnlyList<object> ListOpenFlights(int lookback = 500)
    {
        var events = ReadTail(Math.Clamp(lookback, 10, DefaultMaxLines));
        var closedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in events)
        {
            if (!IsClosedToolCall(e) || string.IsNullOrWhiteSpace(e.CallId))
                continue;
            closedIds.Add(e.CallId);
        }

        var lastTickByCall = new Dictionary<string, FdrEvent>(StringComparer.Ordinal);
        var tickCountByCall = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in events)
        {
            if (!string.Equals(e.Kind, KindToolTick, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(e.CallId))
                continue;
            lastTickByCall[e.CallId] = e;
            tickCountByCall[e.CallId] = tickCountByCall.GetValueOrDefault(e.CallId) + 1;
        }

        var now = DateTimeOffset.UtcNow;
        var open = new List<object>();
        foreach (var e in events)
        {
            if (!string.Equals(e.Kind, KindToolStart, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(e.CallId) || closedIds.Contains(e.CallId))
                continue;

            var ageMs = 0;
            if (DateTimeOffset.TryParse(e.AtUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var started))
                ageMs = (int)Math.Max(0, (now - started).TotalMilliseconds);

            lastTickByCall.TryGetValue(e.CallId, out var lastTick);
            tickCountByCall.TryGetValue(e.CallId, out var ticks);

            open.Add(new
            {
                at = e.AtUtc,
                tool = e.Tool,
                op = e.Op,
                go = e.Go,
                call = e.CallId,
                threshold_s = e.ThresholdS,
                age_ms = ageMs,
                last_tick_ms = lastTick?.ElapsedMs,
                last_tick_at = lastTick?.AtUtc,
                ticks,
                wake = lastTick?.WakeExceeded ?? false,
                phase = e.Phase,
                @object = e.Object,
                outcome = e.Outcome,
                kind = e.Kind
            });
        }

        return open;
    }

    /// <summary>
    /// Write closed <c>outcome=cancel</c> for tape ghosts (tool_start without close) older than
    /// <paramref name="minAgeSeconds"/> and not in <paramref name="liveCallIds"/> (still in-process).
    /// Host kill / abandon leaves eternal opens — next CallTool reconciles.
    /// </summary>
    public static object CancelOrphanOpenFlights(
        IEnumerable<string>? liveCallIds = null,
        int minAgeSeconds = 90,
        int lookback = 500)
    {
        if (SuppressWriteForTests)
            return new { ok = true, cancelled = 0, skipped_live = 0, hint = "suppressed" };

        minAgeSeconds = Math.Clamp(minAgeSeconds, 0, 3600);
        var live = new HashSet<string>(
            (liveCallIds ?? []).Where(s => !string.IsNullOrWhiteSpace(s)),
            StringComparer.Ordinal);
        var events = ReadTail(Math.Clamp(lookback, 10, DefaultMaxLines));
        var closedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in events)
        {
            if (!IsClosedToolCall(e) || string.IsNullOrWhiteSpace(e.CallId))
                continue;
            closedIds.Add(e.CallId);
        }

        var now = DateTimeOffset.UtcNow;
        var cancelled = new List<object>();
        var skippedLive = 0;

        foreach (var e in events)
        {
            if (!string.Equals(e.Kind, KindToolStart, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(e.CallId) || closedIds.Contains(e.CallId))
                continue;
            if (live.Contains(e.CallId))
            {
                skippedLive++;
                continue;
            }

            if (!DateTimeOffset.TryParse(e.AtUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var started))
                continue;
            var age = now - started;
            if (age.TotalSeconds < minAgeSeconds)
                continue;

            var ageMs = (int)Math.Max(0, age.TotalMilliseconds);
            var wake = e.ThresholdS > 0 && ageMs >= e.ThresholdS * 1000;
            var closeArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(e.Op))
                closeArgs["op"] = JsonSerializer.SerializeToElement(e.Op);
            if (!string.IsNullOrWhiteSpace(e.Go))
                closeArgs["go"] = JsonSerializer.SerializeToElement(e.Go);
            RecordToolCall(
                e.Tool,
                e.CallId,
                closeArgs,
                e.ThresholdS,
                ageMs,
                outcome: "cancel",
                wakeExceeded: wake,
                error: "ghost_cancel · orphan",
                resultChars: 0);
            closedIds.Add(e.CallId);
            cancelled.Add(new
            {
                call = e.CallId,
                tool = e.Tool,
                go = e.Go,
                op = e.Op,
                age_ms = ageMs,
                at = e.AtUtc
            });
        }

        return new
        {
            ok = true,
            cancelled = cancelled.Count,
            skipped_live = skippedLive,
            min_age_s = minAgeSeconds,
            flights = cancelled,
            hint = cancelled.Count == 0
                ? "No orphan ghosts older than min_age_s (or still live in-process)."
                : "Closed orphan tool_start rows with outcome=cancel · ghost_cancel · orphan."
        };
    }

    /// <summary>
    /// Full dynamics trail for one call_id — start + ticks + close (crash dig).
    /// </summary>
    public static object TraceFlight(string callId, int lookback = 500)
    {
        callId = (callId ?? "").Trim();
        if (callId.Length == 0)
        {
            return new
            {
                ok = false,
                reason = "call_required",
                hint = "op=trace call=<call_id>"
            };
        }

        var events = ReadTail(Math.Clamp(lookback, 10, DefaultMaxLines))
            .Where(e => string.Equals(e.CallId, callId, StringComparison.Ordinal))
            .ToArray();

        return new
        {
            ok = true,
            call = callId,
            lookback,
            count = events.Length,
            events = events.Select(Slim).ToArray(),
            open = events.Any(e => string.Equals(e.Kind, KindToolStart, StringComparison.OrdinalIgnoreCase))
                && !events.Any(IsClosedToolCall),
            hint = "Chronological dynamics for one flight. Ghost = start±ticks without closed tool_call."
        };
    }

    public static object BuildStats(int lookback = 500)
    {
        var events = ReadTail(Math.Clamp(lookback, 10, DefaultMaxLines));
        var toolCalls = events.Where(IsClosedToolCall).ToArray();
        var open = ListOpenFlights(lookback);

        var byTool = toolCalls
            .GroupBy(e => e.Tool, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var ms = g.Select(e => e.ElapsedMs).OrderBy(x => x).ToArray();
                return new
                {
                    tool = g.Key,
                    n = g.Count(),
                    errors = g.Count(e => e.Outcome is "error"),
                    cancels = g.Count(e => e.Outcome is "cancel"),
                    wake = g.Count(e => e.WakeExceeded),
                    p50_ms = Percentile(ms, 0.50),
                    p95_ms = Percentile(ms, 0.95),
                    max_ms = ms.Length == 0 ? 0 : ms[^1]
                };
            })
            .OrderByDescending(x => x.max_ms)
            .ThenByDescending(x => x.n)
            .Take(25)
            .ToArray();

        var slow = toolCalls
            .OrderByDescending(e => e.ElapsedMs)
            .Take(15)
            .Select(Slim)
            .ToArray();

        return new
        {
            count = toolCalls.Length,
            open_count = open.Count,
            open,
            lookback,
            by_tool = byTool,
            slowest = slow,
            timeout_wake = IdeFdrThresholdPolicy.SuggestPayload(lookback),
            tape = TapePath
        };
    }

}
