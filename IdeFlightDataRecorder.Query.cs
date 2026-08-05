#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;
internal static partial class IdeFlightDataRecorder
{
    /// <summary>
    /// Starts without a later closed <see cref = "KindToolCall"/> for the same call_id —
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
            if (DateTimeOffset.TryParse(e.AtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started))
                ageMs = (int)Math.Max(0, (now - started).TotalMilliseconds);
            lastTickByCall.TryGetValue(e.CallId, out var lastTick);
            tickCountByCall.TryGetValue(e.CallId, out var ticks);
            open.Add(new { at = e.AtUtc, tool = e.Tool, op = e.Op, go = e.Go, call = e.CallId, threshold_s = e.ThresholdS, age_ms = ageMs, last_tick_ms = lastTick?.ElapsedMs, last_tick_at = lastTick?.AtUtc, ticks, wake = lastTick?.WakeExceeded ?? false, phase = e.Phase, @object = e.Object, outcome = e.Outcome, kind = e.Kind });
        }

        return open;
    }

    /// <summary>
    /// Write closed <c>outcome=cancel</c> for tape ghosts (tool_start without close) older than
    /// <paramref name = "minAgeSeconds"/> and not in <paramref name = "liveCallIds"/> (still in-process).
    /// Host kill / abandon leaves eternal opens — next CallTool reconciles.
    /// </summary>
    public static object CancelOrphanOpenFlights(IEnumerable<string>? liveCallIds = null, int minAgeSeconds = 90, int lookback = 500)
    {
        if (SuppressWriteForTests)
            return new
            {
                ok = true,
                cancelled = 0,
                skipped_live = 0,
                hint = "suppressed"
            };
        minAgeSeconds = Math.Clamp(minAgeSeconds, 0, 3600);
        var live = new HashSet<string>((liveCallIds ?? []).Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.Ordinal);
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

            if (!DateTimeOffset.TryParse(e.AtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started))
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
            RecordToolCall(e.Tool, e.CallId, closeArgs, e.ThresholdS, ageMs, outcome: "cancel", wakeExceeded: wake, error: "ghost_cancel · orphan", resultChars: 0);
            closedIds.Add(e.CallId);
            cancelled.Add(new { call = e.CallId, tool = e.Tool, go = e.Go, op = e.Op, age_ms = ageMs, at = e.AtUtc });
        }

        return new
        {
            ok = true,
            cancelled = cancelled.Count,
            skipped_live = skippedLive,
            min_age_s = minAgeSeconds,
            flights = cancelled,
            hint = cancelled.Count == 0 ? "No orphan ghosts older than min_age_s (or still live in-process)." : "Closed orphan tool_start rows with outcome=cancel · ghost_cancel · orphan."
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

        var events = ReadTail(Math.Clamp(lookback, 10, DefaultMaxLines)).Where(e => string.Equals(e.CallId, callId, StringComparison.Ordinal)).ToArray();
        return new
        {
            ok = true,
            call = callId,
            lookback,
            count = events.Length,
            events = events.Select(Slim).ToArray(),
            open = events.Any(e => string.Equals(e.Kind, KindToolStart, StringComparison.OrdinalIgnoreCase)) && !events.Any(IsClosedToolCall),
            hint = "Chronological dynamics for one flight. Ghost = start±ticks without closed tool_call."
        };
    }

    public static object BuildStats(int lookback = 500)
    {
        var events = ReadTail(Math.Clamp(lookback, 10, DefaultMaxLines));
        var toolCalls = events.Where(IsClosedToolCall).ToArray();
        var open = ListOpenFlights(lookback);
        var byTool = toolCalls.GroupBy(e => e.Tool, StringComparer.OrdinalIgnoreCase).Select(g =>
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
        }).OrderByDescending(x => x.max_ms).ThenByDescending(x => x.n).Take(25).ToArray();
        var slow = toolCalls.OrderByDescending(e => e.ElapsedMs).Take(15).Select(Slim).ToArray();
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