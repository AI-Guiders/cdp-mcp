#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Black-box FDR (L3): dense append-only tool-call flight tape under workspace state.
/// Storage-first — not a chat dump. Feeds incident recall + future auto timeout_wake.
/// </summary>
internal static class IdeFlightDataRecorder
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
            || string.Equals(tool, "cdp_fdr", StringComparison.OrdinalIgnoreCase))
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

    public static IReadOnlyList<FdrEvent> ReadTail(int limit = 40)
    {
        limit = Math.Clamp(limit, 1, 500);
        lock (Gate)
        {
            var path = TapePath;
            if (!File.Exists(path))
                return [];

            var lines = File.ReadAllLines(path);
            var list = new List<FdrEvent>(Math.Min(limit, lines.Length));
            for (var i = lines.Length - 1; i >= 0 && list.Count < limit; i--)
            {
                var line = lines[i].Trim();
                if (line.Length == 0)
                    continue;
                try
                {
                    var ev = JsonSerializer.Deserialize<FdrEvent>(line, JsonOpts);
                    if (ev is not null)
                        list.Add(ev);
                }
                catch
                {
                    /* skip corrupt */
                }
            }

            list.Reverse();
            return list;
        }
    }

    public static object BuildStats(int lookback = 500)
    {
        var events = ReadTail(Math.Clamp(lookback, 10, DefaultMaxLines));
        var byTool = events
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

        var slow = events
            .OrderByDescending(e => e.ElapsedMs)
            .Take(15)
            .Select(Slim)
            .ToArray();

        return new
        {
            count = events.Count,
            lookback,
            by_tool = byTool,
            slowest = slow,
            tape = TapePath
        };
    }

    public static object Slim(FdrEvent e) => new
    {
        at = e.AtUtc,
        tool = e.Tool,
        op = e.Op,
        go = e.Go,
        ms = e.ElapsedMs,
        outcome = e.Outcome,
        wake = e.WakeExceeded,
        phase = e.Phase,
        @object = e.Object,
        err = e.Error,
        call = e.CallId
    };

    static void Append(FdrEvent ev)
    {
        lock (Gate)
        {
            try
            {
                var path = TapePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                var line = JsonSerializer.Serialize(ev, JsonOpts);
                File.AppendAllText(path, line + "\n", Encoding.UTF8);
                RotateIfNeeded(path, DefaultMaxLines);
            }
            catch
            {
                /* never break CallTool on FDR I/O */
            }
        }
    }

    static void RotateIfNeeded(string path, int maxLines)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length <= maxLines)
                return;
            var keep = lines.AsSpan(lines.Length - maxLines).ToArray();
            File.WriteAllLines(path, keep, Encoding.UTF8);
        }
        catch
        {
            /* best-effort */
        }
    }

    static int Percentile(int[] sortedAsc, double p)
    {
        if (sortedAsc.Length == 0)
            return 0;
        var idx = (int)Math.Clamp(Math.Ceiling(p * sortedAsc.Length) - 1, 0, sortedAsc.Length - 1);
        return sortedAsc[idx];
    }

    static string? OptArg(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => Truncate(el.GetString(), 64),
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    static string? Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        return s.Length <= max ? s : s[..max];
    }
}
