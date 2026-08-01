#nullable enable

namespace CdpMcp;

/// <summary>Candidate classification for FDR timeout_wake suggestions.</summary>
internal static partial class IdeFdrThresholdPolicy
{
    static Candidate BuildCandidate(
        string tool,
        IdeFlightDataRecorder.FdrEvent[] rows,
        Func<string, int> staticThreshold)
    {
        var ms = rows.Select(e => e.ElapsedMs).OrderBy(x => x).ToArray();
        var n = ms.Length;
        var wake = rows.Count(e => e.WakeExceeded);
        var p50 = Percentile(ms, 0.50);
        var p95 = Percentile(ms, 0.95);
        var max = ms.Length == 0 ? 0 : ms[^1];
        var current = staticThreshold(tool);
        var p50S = CeilMsToSeconds(p50);
        var p95S = CeilMsToSeconds(p95);
        var maxS = CeilMsToSeconds(max);
        var margin = Math.Max(5, p95S / 5);

        if (current == 0)
        {
            return new Candidate
            {
                Tool = tool,
                N = n,
                Wake = wake,
                P50Ms = p50,
                P95Ms = p95,
                MaxMs = max,
                CurrentS = 0,
                SuggestedS = 0,
                Action = "off",
                Why = "static timeout_wake off (pulse / self tools)"
            };
        }

        // Hang: wakes + max far above typical (p50). When the hang IS p95, max≈p95 — still hang vs median.
        var hangLike = wake > 0
            && maxS >= 60
            && maxS >= Math.Max(p50S * 10, 60);
        if (hangLike)
        {
            return new Candidate
            {
                Tool = tool,
                N = n,
                Wake = wake,
                P50Ms = p50,
                P95Ms = p95,
                MaxMs = max,
                CurrentS = current,
                SuggestedS = current,
                Action = "hang_outlier",
                Why = $"max {maxS}s >> p50 {p50S}s with wake×{wake} — keep {current}s; peel organ hang / async"
            };
        }

        // Legit long sync organs — surface async peel, don't auto-raise into minutes.
        if (IsLongOrgan(tool) && p95S >= 25 && wake == 0)
        {
            return new Candidate
            {
                Tool = tool,
                N = n,
                Wake = wake,
                P50Ms = p50,
                P95Ms = p95,
                MaxMs = max,
                CurrentS = current,
                SuggestedS = current,
                Action = "async_candidate",
                Why = $"p95 {p95S}s ok under {current}s — prefer start+poll over higher timeout_wake"
            };
        }

        var need = RoundUp5(p95S + margin);
        if (n >= MinSamplesForRaise && need > current && (wake >= 1 || p95S >= current))
        {
            var suggested = Math.Clamp(Math.Max(need, current + 5), current + 5, 600);
            return new Candidate
            {
                Tool = tool,
                N = n,
                Wake = wake,
                P50Ms = p50,
                P95Ms = p95,
                MaxMs = max,
                CurrentS = current,
                SuggestedS = suggested,
                Action = "raise",
                Why = $"p95 {p95S}s + margin → {suggested}s (was {current}s; wake×{wake})"
            };
        }

        return new Candidate
        {
            Tool = tool,
            N = n,
            Wake = wake,
            P50Ms = p50,
            P95Ms = p95,
            MaxMs = max,
            CurrentS = current,
            SuggestedS = current,
            Action = "ok",
            Why = $"p95 {p95S}s within {current}s"
        };
    }
}
