#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CdpProfileIsolation")]
public sealed class IdeFdrChannelTests
{
    [Fact]
    public async Task Record_and_stats_roundtrip()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var iso = Path.Combine(Path.GetTempPath(), "cdp-fdr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iso);
        CdpProfile.ApplyClientRoots([iso]);
        IdeFlightDataRecorder.PathOverrideForTests = Path.Combine(iso, "fdr-tape.jsonl");
        IdeFlightDataRecorder.SuppressWriteForTests = false;
        IdeToolCallWatch.SuppressArmForTests = true;
        IdeFlightDataRecorder.BindContext(() => new IdeFlightDataRecorder.FdrContextSnap(
            "act", "code", "csharp", "fixture"));
        try
        {
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("scene"),
                ["go"] = JsonSerializer.SerializeToElement("plan"),
                ["timeout_wake"] = JsonSerializer.SerializeToElement(false)
            };

            var text = await IdeToolCallWatch.RunAsync(
                "cdp_cockpit",
                args,
                async ct =>
                {
                    await Task.Delay(25, ct);
                    return "{\"ok\":true}";
                },
                CancellationToken.None);

            Assert.Contains("ok", text, StringComparison.Ordinal);

            var session = new SessionContext { ProjectRoot = iso };
            using var scene = JsonDocument.Parse(IdeFdrChannel.HandleJson(session));
            Assert.True(scene.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("fdr_channel/v1", scene.RootElement.GetProperty("schema").GetString());

            var stats = IdeFdrChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("stats")
            });
            using var statsDoc = JsonDocument.Parse(JsonSerializer.Serialize(stats));
            Assert.True(statsDoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(statsDoc.RootElement.GetProperty("stats").GetProperty("count").GetInt32() >= 1);

            var tail = IdeFlightDataRecorder.ReadTail(10);
            Assert.Contains(tail, e => e.Tool == "cdp_cockpit" && e.Kind == IdeFlightDataRecorder.KindToolStart
                && e.Outcome == IdeFlightDataRecorder.OutcomeRunning);
            Assert.Contains(tail, e => e.Tool == "cdp_cockpit" && e.Outcome == "ok" && e.Go == "plan"
                && e.Kind == IdeFlightDataRecorder.KindToolCall);
            Assert.Contains(tail, e => e.Phase == "act" && e.Project == "fixture");
            Assert.Empty(IdeFlightDataRecorder.ListOpenFlights(50));

            // FDR desk itself must not pollute the tape.
            _ = IdeFdrChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("tail")
            });
            Assert.DoesNotContain(IdeFlightDataRecorder.ReadTail(50), e => e.Tool == "cdp_fdr");
        }
        finally
        {
            IdeFlightDataRecorder.PathOverrideForTests = null;
            IdeFlightDataRecorder.BindContext(null);
            IdeToolCallWatch.SuppressArmForTests = false;
            CdpProfile.ApplyClientRoots([Path.Combine(Path.GetTempPath(), "cdp-fdr-cleanup")]);
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task Record_error_outcome()
    {
        var iso = Path.Combine(Path.GetTempPath(), "cdp-fdr-err-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iso);
        IdeFlightDataRecorder.PathOverrideForTests = Path.Combine(iso, "fdr-tape.jsonl");
        IdeToolCallWatch.SuppressArmForTests = true;
        try
        {
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["timeout_wake"] = JsonSerializer.SerializeToElement(false)
            };
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await IdeToolCallWatch.RunAsync(
                    "cdp_health",
                    args,
                    _ => throw new InvalidOperationException("boom"),
                    CancellationToken.None));

            var tail = IdeFlightDataRecorder.ReadTail(5);
            Assert.Contains(tail, e => e.Tool == "cdp_health" && e.Outcome == "error" && e.Error == "boom");
        }
        finally
        {
            IdeFlightDataRecorder.PathOverrideForTests = null;
            IdeToolCallWatch.SuppressArmForTests = false;
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void RecordWake_appends_kind_on_tape()
    {
        var iso = Path.Combine(Path.GetTempPath(), "cdp-fdr-wake-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iso);
        IdeFlightDataRecorder.PathOverrideForTests = Path.Combine(iso, "fdr-tape.jsonl");
        try
        {
            IdeFlightDataRecorder.RecordWake("wake_arm", "tool-wake-abc", "cdp_shell_run", "threshold=1s");
            IdeFlightDataRecorder.RecordWake("wake_cancel", "tool-wake-abc", "cdp_shell_run", "clear_wake_arm_after_call");
            var tail = IdeFlightDataRecorder.ReadTail(10);
            Assert.Contains(tail, e => e.Kind == "wake_arm" && e.CallId == "tool-wake-abc");
            Assert.Contains(tail, e => e.Kind == "wake_cancel" && e.Tool == "cdp_shell_run");
        }
        finally
        {
            IdeFlightDataRecorder.PathOverrideForTests = null;
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Open_lists_start_without_close_ghost()
    {
        var iso = Path.Combine(Path.GetTempPath(), "cdp-fdr-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iso);
        IdeFlightDataRecorder.PathOverrideForTests = Path.Combine(iso, "fdr-tape.jsonl");
        IdeFlightDataRecorder.SuppressWriteForTests = false;
        try
        {
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("run")
            };
            IdeFlightDataRecorder.RecordToolStart("cdp_csx_run", "ghostcall01", args, thresholdSeconds: 45);
            var open = IdeFlightDataRecorder.ListOpenFlights(20);
            Assert.Contains(open, o =>
            {
                var json = JsonSerializer.Serialize(o);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.GetProperty("call").GetString() == "ghostcall01"
                    && doc.RootElement.GetProperty("tool").GetString() == "cdp_csx_run";
            });

            IdeFlightDataRecorder.RecordToolCall(
                "cdp_csx_run", "ghostcall01", args, 45, elapsedMs: 12,
                outcome: "cancel", wakeExceeded: false, error: "host_abort", resultChars: 0);
            Assert.DoesNotContain(
                IdeFlightDataRecorder.ListOpenFlights(20),
                o => JsonSerializer.Serialize(o).Contains("ghostcall01", StringComparison.Ordinal));

            var session = new SessionContext { ProjectRoot = iso };
            var payload = IdeFdrChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("open")
            });
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("open", doc.RootElement.GetProperty("op").GetString());
        }
        finally
        {
            IdeFlightDataRecorder.PathOverrideForTests = null;
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task Cancel_records_start_and_closed_cancel()
    {
        var iso = Path.Combine(Path.GetTempPath(), "cdp-fdr-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iso);
        IdeFlightDataRecorder.PathOverrideForTests = Path.Combine(iso, "fdr-tape.jsonl");
        IdeToolCallWatch.SuppressArmForTests = true;
        try
        {
            using var cts = new CancellationTokenSource();
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["timeout_wake"] = JsonSerializer.SerializeToElement(false)
            };
            var run = IdeToolCallWatch.RunAsync(
                "cdp_health",
                args,
                async ct =>
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return "{}";
                },
                cts.Token);
            await Task.Delay(30);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await run);

            var tail = IdeFlightDataRecorder.ReadTail(10);
            Assert.Contains(tail, e => e.Tool == "cdp_health" && e.Kind == IdeFlightDataRecorder.KindToolStart);
            Assert.Contains(tail, e => e.Tool == "cdp_health" && e.Outcome == "cancel"
                && e.Kind == IdeFlightDataRecorder.KindToolCall);
            Assert.Empty(IdeFlightDataRecorder.ListOpenFlights(20));
        }
        finally
        {
            IdeFlightDataRecorder.PathOverrideForTests = null;
            IdeToolCallWatch.SuppressArmForTests = false;
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }
}
