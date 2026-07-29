#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeToolCallWatchTests
{
    public IdeToolCallWatchTests()
    {
        IdeToolCallWatch.ThresholdHookForTests = null;
        IdeToolCallWatch.SuppressArmForTests = true;
    }

    [Fact]
    public void ResolveThreshold_cockpit_default_20()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        Assert.Equal(20, IdeToolCallWatch.ResolveThresholdSeconds("cdp_cockpit", args));
    }

    [Fact]
    public void ResolveThreshold_ignite_off()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        Assert.Equal(0, IdeToolCallWatch.ResolveThresholdSeconds("cdp_ignite", args));
    }

    [Fact]
    public void ResolveThreshold_timeout_wake_override()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["timeout_wake"] = JsonSerializer.SerializeToElement("3s")
        };
        Assert.Equal(3, IdeToolCallWatch.ResolveThresholdSeconds("cdp_cockpit", args));
    }

    [Fact]
    public void ResolveThreshold_timeout_wake_off()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["timeout_wake"] = JsonSerializer.SerializeToElement(false)
        };
        Assert.Equal(0, IdeToolCallWatch.ResolveThresholdSeconds("cdp_cockpit", args));
    }

    [Fact]
    public void AnnotateResult_merges_wake_object()
    {
        var raw = """{"ok":true,"pulse":"x"}""";
        var annotated = IdeToolCallWatch.AnnotateResult(raw, "cdp_cockpit", 20, 25, exceededDuring: true);
        using var doc = JsonDocument.Parse(annotated);
        Assert.True(doc.RootElement.GetProperty("wake").GetProperty("exceeded").GetBoolean());
        Assert.Equal("cdp_cockpit", doc.RootElement.GetProperty("wake").GetProperty("tool").GetString());
        Assert.Equal(25, doc.RootElement.GetProperty("wake").GetProperty("elapsed_s").GetInt32());
    }

    [Fact]
    public async Task RunAsync_fires_threshold_hook_when_execute_sync_blocks()
    {
        IdeToolCallWatch.ThresholdHit? hit = null;
        IdeToolCallWatch.ThresholdHookForTests = h => hit = h;
        var root = Path.Combine(Path.GetTempPath(), "cdp-tool-watch-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        CideToolWatchLatch.RootOverrideForTests = root;
        try
        {
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["timeout_wake"] = JsonSerializer.SerializeToElement(1)
            };
            var text = await IdeToolCallWatch.RunAsync(
                "cdp_shell_run",
                args,
                _ =>
                {
                    // Sync block before first await — old order starved Delay.
                    Thread.Sleep(1500);
                    return Task.FromResult("""{"ok":true}""");
                },
                CancellationToken.None);

            Assert.NotNull(hit);
            Assert.True(CideToolWatchLatch.TryRead()!.Active);
            using var doc = JsonDocument.Parse(text);
            Assert.True(doc.RootElement.GetProperty("wake").GetProperty("during_call").GetBoolean());
        }
        finally
        {
            CideToolWatchLatch.RootOverrideForTests = null;
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task RunAsync_fires_threshold_hook_when_slow()
    {
        IdeToolCallWatch.ThresholdHit? hit = null;
        IdeToolCallWatch.ThresholdHookForTests = h => hit = h;
        var root = Path.Combine(Path.GetTempPath(), "cdp-tool-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        CideToolWatchLatch.RootOverrideForTests = root;
        try
        {
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["timeout_wake"] = JsonSerializer.SerializeToElement(1)
            };
            var text = await IdeToolCallWatch.RunAsync(
                "cdp_cockpit",
                args,
                async ct =>
                {
                    await Task.Delay(1500, ct);
                    return """{"ok":true}""";
                },
                CancellationToken.None);

            Assert.NotNull(hit);
            Assert.Equal("cdp_cockpit", hit!.Value.Tool);
            Assert.Equal(1, hit.Value.ThresholdSeconds);
            using var doc = JsonDocument.Parse(text);
            Assert.True(doc.RootElement.GetProperty("wake").GetProperty("exceeded").GetBoolean());
            var latch = CideToolWatchLatch.TryRead();
            Assert.NotNull(latch);
            Assert.True(latch!.Active);
            Assert.Equal("cdp_cockpit", latch.Tool);
        }
        finally
        {
            CideToolWatchLatch.RootOverrideForTests = null;
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ComposeToolWatchWakeCharge_mentions_tool()
    {
        var charge = IdeIgniteChannel.ComposeToolWatchWakeCharge("cdp_cockpit", 20);
        Assert.Contains("cdp_cockpit", charge, StringComparison.Ordinal);
        Assert.Contains(">20s", charge, StringComparison.Ordinal);
        Assert.Contains("Habitat=CDP", charge, StringComparison.Ordinal);
    }
}
