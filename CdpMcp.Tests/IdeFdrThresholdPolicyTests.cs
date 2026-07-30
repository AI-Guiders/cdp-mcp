#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeFdrThresholdPolicyTests
{
    [Fact]
    public void Suggest_hang_outlier_keeps_threshold()
    {
        var events = Enumerable.Range(0, 10)
            .Select(i => new IdeFlightDataRecorder.FdrEvent
            {
                Kind = "tool_call",
                Tool = "cdp_cockpit",
                ElapsedMs = i < 9 ? 500 : 300_000,
                WakeExceeded = i >= 9,
                Outcome = "ok"
            })
            .ToArray();

        var c = Assert.Single(IdeFdrThresholdPolicy.SuggestFromEvents(events));
        Assert.Equal("hang_outlier", c.Action);
        Assert.Equal(20, c.SuggestedS);
        Assert.Equal(20, c.CurrentS);
    }

    [Fact]
    public void Suggest_raise_when_p95_above_current()
    {
        // 10 samples ~55s — p95 above default 45s, with wakes, not hang-like max.
        var events = Enumerable.Range(0, 10)
            .Select(_ => new IdeFlightDataRecorder.FdrEvent
            {
                Kind = "tool_call",
                Tool = "cdp_buffer",
                ElapsedMs = 55_000,
                WakeExceeded = true,
                Outcome = "ok"
            })
            .ToArray();

        var c = Assert.Single(IdeFdrThresholdPolicy.SuggestFromEvents(events));
        Assert.Equal("raise", c.Action);
        Assert.True(c.SuggestedS > 45, $"suggested={c.SuggestedS}");
        Assert.True(c.SuggestedS <= 600);
    }

    [Fact]
    public void Suggest_async_candidate_for_long_organ()
    {
        var events = Enumerable.Range(0, 8)
            .Select(_ => new IdeFlightDataRecorder.FdrEvent
            {
                Kind = "tool_call",
                Tool = "cdp_test",
                ElapsedMs = 30_000,
                WakeExceeded = false,
                Outcome = "ok"
            })
            .ToArray();

        var c = Assert.Single(IdeFdrThresholdPolicy.SuggestFromEvents(events));
        Assert.Equal("async_candidate", c.Action);
        Assert.Equal(120, c.SuggestedS);
    }

    [Fact]
    public void Suggest_ignores_wake_kind_rows()
    {
        var events = new[]
        {
            new IdeFlightDataRecorder.FdrEvent
            {
                Kind = "wake_arm",
                Tool = "cdp_cockpit",
                ElapsedMs = 0,
                Outcome = "wake_arm"
            },
            new IdeFlightDataRecorder.FdrEvent
            {
                Kind = "tool_call",
                Tool = "cdp_cockpit",
                ElapsedMs = 400,
                Outcome = "ok"
            }
        };

        var c = Assert.Single(IdeFdrThresholdPolicy.SuggestFromEvents(events));
        Assert.Equal(1, c.N);
        Assert.Equal("ok", c.Action);
    }

    [Fact]
    public void Overlay_roundtrip_affects_ResolveThreshold()
    {
        var iso = Path.Combine(Path.GetTempPath(), "cdp-fdr-ovl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iso);
        IdeFdrThresholdPolicy.PathOverrideForTests = Path.Combine(iso, "overlay.json");
        try
        {
            File.WriteAllText(
                IdeFdrThresholdPolicy.PathOverrideForTests,
                """
                {"schema":"fdr_timeout_wake_overlay/v1","tools":{"cdp_buffer":70}}
                """);

            Assert.True(IdeFdrThresholdPolicy.TryGetOverlaySeconds("cdp_buffer", out var sec));
            Assert.Equal(70, sec);
            Assert.Equal(70, IdeToolCallWatch.ResolveThresholdSeconds(
                "cdp_buffer",
                new Dictionary<string, System.Text.Json.JsonElement>()));
            Assert.Equal(45, IdeToolCallWatch.StaticThresholdSeconds("cdp_buffer"));

            _ = IdeFdrThresholdPolicy.ClearOverlay();
            Assert.False(IdeFdrThresholdPolicy.TryGetOverlaySeconds("cdp_buffer", out _));
            Assert.Equal(45, IdeToolCallWatch.ResolveThresholdSeconds(
                "cdp_buffer",
                new Dictionary<string, System.Text.Json.JsonElement>()));
        }
        finally
        {
            IdeFdrThresholdPolicy.PathOverrideForTests = null;
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }
}
