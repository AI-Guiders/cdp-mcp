#nullable enable
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// FDR → timeout_wake candidates: classify hang vs raise vs async from tape p95/wake,
/// optional overlay so ResolveThreshold is evidence-driven (not hand-tuned guesses).
/// </summary>
internal static partial class IdeFdrThresholdPolicy
{
    public const string Schema = "fdr_timeout_wake_overlay/v1";
    public const string OverlayFileName = "fdr-timeout-wake-overlay.json";
    public const int MinSamplesForRaise = 5;

    static readonly object Gate = new();
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static Dictionary<string, int>? s_overlay;
    static string? s_overlayPath;
    static DateTime s_overlayMtimeUtc;

    internal static string? PathOverrideForTests { get; set; }

    public static string OverlayPath =>
        PathOverrideForTests ?? Path.Combine(CdpProfile.StateRoot, OverlayFileName);

    public sealed class Candidate
    {
        public string Tool { get; init; } = "";
        public int N { get; init; }
        public int Wake { get; init; }
        public int P50Ms { get; init; }
        public int P95Ms { get; init; }
        public int MaxMs { get; init; }
        public int CurrentS { get; init; }
        public int SuggestedS { get; init; }
        public string Action { get; init; } = "ok";
        public string Why { get; init; } = "";
    }

    public static IReadOnlyList<Candidate> SuggestFromEvents(
        IEnumerable<IdeFlightDataRecorder.FdrEvent> events,
        Func<string, int>? staticThreshold = null)
    {
        staticThreshold ??= IdeToolCallWatch.StaticThresholdSeconds;
        var toolCalls = events
            .Where(IsToolCall)
            .Where(e => !string.IsNullOrWhiteSpace(e.Tool))
            .ToArray();

        return toolCalls
            .GroupBy(e => e.Tool, StringComparer.OrdinalIgnoreCase)
            .Select(g => BuildCandidate(g.Key, g.ToArray(), staticThreshold))
            .OrderByDescending(c => ActionRank(c.Action))
            .ThenByDescending(c => c.MaxMs)
            .ThenBy(c => c.Tool, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<Candidate> SuggestFromTape(int lookback = 500)
    {
        var events = IdeFlightDataRecorder.ReadTail(
            Math.Clamp(lookback, 10, IdeFlightDataRecorder.DefaultMaxLines));
        return SuggestFromEvents(events);
    }

    public static bool TryGetOverlaySeconds(string tool, out int seconds)
    {
        seconds = 0;
        var name = (tool ?? "").Trim();
        if (name.Length == 0)
            return false;

        EnsureOverlayLoaded();
        lock (Gate)
        {
            if (s_overlay is null)
                return false;
            foreach (var kv in s_overlay)
            {
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    seconds = Math.Clamp(kv.Value, 0, 600);
                    return true;
                }
            }
        }

        return false;
    }

    public static object SuggestPayload(int lookback = 500)
    {
        var candidates = SuggestFromTape(lookback);
        EnsureOverlayLoaded();
        object? overlay = null;
        lock (Gate)
        {
            if (s_overlay is { Count: > 0 })
            {
                overlay = new
                {
                    path = OverlayPath,
                    tools = s_overlay.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase)
                };
            }
        }

        return new
        {
            lookback,
            candidates,
            overlay,
            apply = "op=apply — write raise suggestions into overlay; ResolveThreshold reads it",
            clear = "op=clear_overlay",
            hint =
                "hang_outlier = keep aggressive threshold (max>>p95 + wakes). " +
                "raise = p95 needs higher timeout_wake. " +
                "async_candidate = legit long sync — prefer start+poll. " +
                "Per-call timeout_wake= still wins."
        };
    }

    public static object Apply(int lookback = 500, bool dryRun = false)
    {
        var raises = SuggestFromTape(lookback)
            .Where(c => c.Action is "raise" && c.SuggestedS > c.CurrentS)
            .ToArray();

        if (dryRun)
        {
            return new
            {
                schema = Schema,
                ok = true,
                op = "apply",
                dry_run = true,
                would_write = raises.Select(SlimRaise).ToArray(),
                path = OverlayPath,
                hint = "dry_run=true — no write. Re-run without dry_run to arm overlay."
            };
        }

        var map = raises.ToDictionary(
            c => c.Tool,
            c => c.SuggestedS,
            StringComparer.OrdinalIgnoreCase);

        WriteOverlay(map, lookback, raises);
        return new
        {
            schema = Schema,
            ok = true,
            op = "apply",
            dry_run = false,
            written = raises.Select(SlimRaise).ToArray(),
            count = map.Count,
            path = OverlayPath,
            hint = map.Count == 0
                ? "No raise candidates on tape — overlay cleared/empty. hang/async stay informational."
                : "Overlay armed — ResolveThreshold uses these until clear_overlay or per-call override."
        };
    }

    public static object ClearOverlay()
    {
        var path = OverlayPath;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* best-effort */
        }

        lock (Gate)
        {
            s_overlay = null;
            s_overlayPath = null;
            s_overlayMtimeUtc = default;
        }

        return new
        {
            schema = Schema,
            ok = true,
            op = "clear_overlay",
            path,
            hint = "Overlay cleared — StaticThresholdSeconds only (+ per-call override)."
        };
    }


    static void WriteOverlay(
        Dictionary<string, int> map,
        int lookback,
        IReadOnlyList<Candidate> raises)
    {
        var path = OverlayPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var doc = new
        {
            schema = Schema,
            at_utc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            lookback,
            tools = map.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase),
            notes = raises.Select(c => new { tool = c.Tool, why = c.Why }).ToArray()
        };

        var json = JsonSerializer.Serialize(doc, JsonOpts);
        File.WriteAllText(path, json);
        lock (Gate)
        {
            s_overlay = new Dictionary<string, int>(map, StringComparer.OrdinalIgnoreCase);
            s_overlayPath = path;
            try { s_overlayMtimeUtc = File.GetLastWriteTimeUtc(path); }
            catch { s_overlayMtimeUtc = DateTime.UtcNow; }
        }
    }

    static void EnsureOverlayLoaded()
    {
        var path = OverlayPath;
        lock (Gate)
        {
            try
            {
                if (!File.Exists(path))
                {
                    s_overlay = null;
                    s_overlayPath = path;
                    s_overlayMtimeUtc = default;
                    return;
                }

                var mtime = File.GetLastWriteTimeUtc(path);
                if (s_overlay is not null
                    && string.Equals(s_overlayPath, path, StringComparison.OrdinalIgnoreCase)
                    && mtime == s_overlayMtimeUtc)
                    return;

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                if (!root.TryGetProperty("tools", out var toolsEl)
                    || toolsEl.ValueKind != JsonValueKind.Object)
                {
                    s_overlay = null;
                    return;
                }

                var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in toolsEl.EnumerateObject())
                {
                    if (p.Value.TryGetInt32(out var sec))
                        map[p.Name] = Math.Clamp(sec, 0, 600);
                }

                s_overlay = map;
                s_overlayPath = path;
                s_overlayMtimeUtc = mtime;
            }
            catch
            {
                s_overlay = null;
            }
        }
    }

    static bool IsToolCall(IdeFlightDataRecorder.FdrEvent e)
    {
        var kind = e.Kind?.Trim() ?? "";
        return kind.Length == 0
            || string.Equals(kind, "tool_call", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsLongOrgan(string tool) =>
        tool is "cdp_build" or "cdp_test" or "cdp_deploy" or "cdp_shell_run";

    static int CeilMsToSeconds(int ms) => ms <= 0 ? 0 : (ms + 999) / 1000;

    static int RoundUp5(int s) => s <= 0 ? 0 : ((s + 4) / 5) * 5;

    static int Percentile(int[] sortedAsc, double p)
    {
        if (sortedAsc.Length == 0)
            return 0;
        var idx = (int)Math.Clamp(Math.Ceiling(p * sortedAsc.Length) - 1, 0, sortedAsc.Length - 1);
        return sortedAsc[idx];
    }

    static int ActionRank(string action) => action switch
    {
        "raise" => 4,
        "hang_outlier" => 3,
        "async_candidate" => 2,
        "ok" => 1,
        _ => 0
    };

    static object SlimRaise(Candidate c) => new
    {
        tool = c.Tool,
        from_s = c.CurrentS,
        to_s = c.SuggestedS,
        why = c.Why
    };
}
