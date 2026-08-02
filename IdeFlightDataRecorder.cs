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

    public static string TapePath =>
        PathOverrideForTests ?? Path.Combine(CdpProfile.StateRoot, "fdr-tape.jsonl");

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
        public string Outcome { get; set; } = "ok"; // ok|error|cancel
        public bool WakeExceeded { get; set; }
        public string? Error { get; set; }
        public int ResultChars { get; set; }
        public string? Phase { get; set; }
        public string? Object { get; set; }
        public string? Language { get; set; }
        public string? Project { get; set; }
        public string AtUtc { get; set; } = "";
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
        if (SuppressWriteForTests)
            return;
        if (string.Equals(tool, IdeFdrChannel.ToolName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tool, "cdp_fdr", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tool, IdeTeethChannel.ToolName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tool, "cdp_teeth", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tool, IdePostmortemChannel.ToolName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tool, "cdp_postmortem", StringComparison.OrdinalIgnoreCase))
            return;

        FdrContextSnap? snap = null;
        try { snap = s_context?.Invoke(); } catch { /* best-effort */ }

        var ev = new FdrEvent
        {
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


        public static object BuildStats(int lookback = 500)
    {
        var events = ReadTail(Math.Clamp(lookback, 10, DefaultMaxLines));
        var toolCalls = events
            .Where(e =>
            {
                var kind = e.Kind?.Trim() ?? "";
                return kind.Length == 0
                    || string.Equals(kind, "tool_call", StringComparison.OrdinalIgnoreCase);
            })
            .Where(e => !string.IsNullOrWhiteSpace(e.Tool))
            .ToArray();

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
            lookback,
            by_tool = byTool,
            slowest = slow,
            timeout_wake = IdeFdrThresholdPolicy.SuggestPayload(lookback),
            tape = TapePath
        };
    }

}
