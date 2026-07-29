#nullable enable
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CdpMcp;

/// <summary>
/// L2 harness wake-on-threshold: while sync CallTool runs, wall-clock timer arms a short Autoi once-wake
/// when elapsed exceeds per-tool / per-call threshold. Mid-Stop CDT inject cannot land — FireAsync waits idle.
/// Also drives L3 FDR tape (every call) via <see cref="IdeFlightDataRecorder"/>.
/// </summary>
internal static class IdeToolCallWatch
{
    public const string Schema = "tool_call_watch/v1";
    public const int DefaultThresholdSeconds = 45;

    /// <summary>Test hook — invoked when threshold fires (before latch/arm).</summary>
    internal static Action<ThresholdHit>? ThresholdHookForTests { get; set; }

    /// <summary>Test hook — skip Autoi arm.</summary>
    internal static bool SuppressArmForTests { get; set; }

    static readonly ConcurrentDictionary<string, byte> ArmedForCall = new(StringComparer.Ordinal);

    public readonly record struct ThresholdHit(
        string Tool,
        int ThresholdSeconds,
        DateTimeOffset StartedUtc,
        string CallId);

    public static async Task<string> RunAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonElement> args,
        Func<CancellationToken, Task<string>> execute,
        CancellationToken cancellationToken)
    {
        var threshold = ResolveThresholdSeconds(toolName, args);
        var callId = Guid.NewGuid().ToString("N")[..12];
        var started = DateTimeOffset.UtcNow;
        var exceededFlag = 0;
        string outcome = "ok";
        string? error = null;
        string text = "";

        if (threshold <= 0)
        {
            try
            {
                text = await execute(cancellationToken).ConfigureAwait(false);
                return text;
            }
            catch (OperationCanceledException)
            {
                outcome = "cancel";
                throw;
            }
            catch (Exception ex)
            {
                outcome = "error";
                error = ex.Message;
                throw;
            }
            finally
            {
                var elapsedMs = (int)Math.Max(0, (DateTimeOffset.UtcNow - started).TotalMilliseconds);
                IdeFlightDataRecorder.RecordToolCall(
                    toolName, callId, args, threshold, elapsedMs, outcome,
                    wakeExceeded: false, error, resultChars: text.Length);
            }
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Offload execute so sync-over-async organs cannot starve the wake timer on the CallTool thread.
        var exec = Task.Run(async () => await execute(linked.Token).ConfigureAwait(false), linked.Token);
        var watch = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(threshold), linked.Token).ConfigureAwait(false);
                if (!exec.IsCompleted && Interlocked.Exchange(ref exceededFlag, 1) == 0)
                    OnThreshold(new ThresholdHit(toolName, threshold, started, callId));
            }
            catch (OperationCanceledException)
            {
                /* execute finished under threshold or host cancelled */
            }
        }, CancellationToken.None);

        try
        {
            text = await exec.ConfigureAwait(false);
            var elapsed = (int)Math.Max(0, (DateTimeOffset.UtcNow - started).TotalSeconds);
            var exceededDuring = Volatile.Read(ref exceededFlag) == 1;
            if (elapsed >= threshold || exceededDuring)
                return AnnotateResult(text, toolName, threshold, elapsed, exceededDuring);
            return text;
        }
        catch (OperationCanceledException)
        {
            outcome = "cancel";
            throw;
        }
        catch (Exception ex)
        {
            outcome = "error";
            error = ex.Message;
            throw;
        }
        finally
        {
            ArmedForCall.TryRemove(callId, out _);
            linked.Cancel();
            try { await watch.ConfigureAwait(false); } catch { /* ignore */ }

            var elapsedMs = (int)Math.Max(0, (DateTimeOffset.UtcNow - started).TotalMilliseconds);
            var wakeExceeded = Volatile.Read(ref exceededFlag) == 1 || elapsedMs >= threshold * 1000;
            IdeFlightDataRecorder.RecordToolCall(
                toolName, callId, args, threshold, elapsedMs, outcome,
                wakeExceeded, error, resultChars: text.Length);
        }
    }

    /// <summary>Per-call override <c>timeout_wake</c>/<c>wake_after</c>; else organ default; 0 = off.</summary>
    public static int ResolveThresholdSeconds(
        string toolName,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (TryParseWakeOverride(args, out var over))
            return over;

        var name = (toolName ?? "").Trim();
        if (name.Length == 0)
            return DefaultThresholdSeconds;

        // Never nest wake on ignite itself / trivial pulse tools.
        if (name is "cdp_ignite" or "cdp_health" or "cdp_pressure" or "ping" or "cdp_fdr")
            return 0;

        return name switch
        {
            "cdp_cockpit" => 20,
            "cdp_work" => 20,
            "cdp_build" or "cdp_test" or "cdp_deploy" => 120,
            "cdp_shell_run" => 90,
            _ => DefaultThresholdSeconds
        };
    }

    public static bool TryParseWakeOverride(
        IReadOnlyDictionary<string, JsonElement> args,
        out int seconds)
    {
        seconds = 0;
        if (!args.TryGetValue("timeout_wake", out var el)
            && !args.TryGetValue("wake_after", out el)
            && !args.TryGetValue("wake_threshold", out el))
            return false;

        switch (el.ValueKind)
        {
            case JsonValueKind.False:
                seconds = 0;
                return true;
            case JsonValueKind.True:
                seconds = DefaultThresholdSeconds;
                return true;
            case JsonValueKind.Number:
                if (el.TryGetInt32(out var n))
                {
                    seconds = Math.Clamp(n, 0, 600);
                    return true;
                }
                return false;
            case JsonValueKind.String:
                var s = el.GetString()?.Trim() ?? "";
                if (s is "off" or "false" or "0" or "no")
                {
                    seconds = 0;
                    return true;
                }
                if (IdeIgniteArmHost.TryParseDuration(s, out var span))
                {
                    seconds = Math.Clamp((int)span.TotalSeconds, 0, 600);
                    return true;
                }
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    seconds = Math.Clamp(parsed, 0, 600);
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    static void OnThreshold(ThresholdHit hit)
    {
        ArmedForCall[hit.CallId] = 0;
        ThresholdHookForTests?.Invoke(hit);

        var pulse = $"tool-watch · {hit.Tool} >{hit.ThresholdSeconds}s · still running";
        CideToolWatchLatch.Publish(
            active: true,
            pulse: pulse,
            tool: hit.Tool,
            thresholdSeconds: hit.ThresholdSeconds,
            startedUtc: hit.StartedUtc);

        if (SuppressArmForTests)
            return;
        if (string.Equals(
                Environment.GetEnvironmentVariable("CDP_TOOL_WAKE_ARM"),
                "0",
                StringComparison.Ordinal))
            return;

        try
        {
            var charge = IdeIgniteChannel.ComposeToolWatchWakeCharge(hit.Tool, hit.ThresholdSeconds);
            var armArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["when"] = JsonSerializer.SerializeToElement("timer"),
                ["in"] = JsonSerializer.SerializeToElement("1s"),
                ["charge"] = JsonSerializer.SerializeToElement("custom"),
                ["message"] = JsonSerializer.SerializeToElement(charge),
                ["task"] = JsonSerializer.SerializeToElement($"tool-watch:{hit.Tool}"),
                ["once"] = JsonSerializer.SerializeToElement(true),
                ["last_once"] = JsonSerializer.SerializeToElement(false),
                ["settle_seconds"] = JsonSerializer.SerializeToElement(0),
                ["wait_seconds"] = JsonSerializer.SerializeToElement(180),
                ["force"] = JsonSerializer.SerializeToElement(true),
                ["id"] = JsonSerializer.SerializeToElement($"tool-wake-{hit.CallId}")
            };
            _ = IdeIgniteArmHost.Arm(armArgs);
        }
        catch
        {
            /* best-effort — latch already published */
        }
    }

    public static string AnnotateResult(
        string text,
        string tool,
        int thresholdSeconds,
        int elapsedSeconds,
        bool exceededDuring)
    {
        if (!exceededDuring && elapsedSeconds < thresholdSeconds)
            return text;
        if (string.IsNullOrWhiteSpace(text))
            return text;

        try
        {
            var node = JsonNode.Parse(text);
            if (node is not JsonObject obj)
                return text;

            obj["wake"] = new JsonObject
            {
                ["schema"] = Schema,
                ["exceeded"] = true,
                ["tool"] = tool,
                ["threshold_s"] = thresholdSeconds,
                ["elapsed_s"] = elapsedSeconds,
                ["during_call"] = exceededDuring,
                ["hint"] = "wall time past timeout_wake — Autoi once-wake armed if idle; prefer start+poll for long organs"
            };
            return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return text;
        }
    }
}
