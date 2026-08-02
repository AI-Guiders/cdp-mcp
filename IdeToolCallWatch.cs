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
internal static partial class IdeToolCallWatch
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
            // Call finished — cancel pending Autoi wake so CDT does not inject a stale
            // "still running" charge minutes later (composer was busy / Stop).
            ClearWakeArm(callId, wakeExceeded);
            IdeFlightDataRecorder.RecordToolCall(
                toolName, callId, args, threshold, elapsedMs, outcome,
                wakeExceeded, error, resultChars: text.Length);
        }
    }

}
