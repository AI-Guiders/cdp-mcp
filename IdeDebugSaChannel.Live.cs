#nullable enable
using System.Text.Json;
using DotnetDebug.Core;
using DotnetDebugMcp;

namespace CdpMcp;

/// <summary>Live debug_desk latch on DAP stopped/continued — Glass SoftOrgan FSW without polling SA.</summary>
internal static partial class IdeDebugSaChannel
{
    static int _liveWired;

    public static void EnsureLiveLatchWired()
    {
        if (Interlocked.Exchange(ref _liveWired, 1) != 0)
            return;

        DebugSession.StoppedHook += OnLiveStopped;
        DebugSession.ContinuedHook += OnLiveContinued;
    }

    static void OnLiveStopped(int threadId, string? exceptionText) =>
        _ = PublishLiveAsync(forceStopped: true);

    static void OnLiveContinued() =>
        PublishLiveChrome(stopped: false, stack: null, locals: null);

    /// <summary>Best-effort publish from DebugSession state (no MCP session required).</summary>
    static void PublishLiveChrome(
        bool stopped,
        IReadOnlyList<CideDebugDeskLatch.StackFrameDoc>? stack,
        IReadOnlyList<CideDebugDeskLatch.LocalVarDoc>? locals)
    {
        try
        {
            var snap = CaptureLive();
            var effectiveStopped = stopped || snap.Stopped;
            var (verdict, _) = Decide(snap with { Stopped = effectiveStopped }, "session");
            var pulse = PulseLine(snap with { Stopped = effectiveStopped }, verdict);
            var active = effectiveStopped
                || snap.ActiveDap
                || verdict is "continue" or "step" or "fix_bp" or "stop_rebuild" or "need_more" or "attach";
            CideDebugDeskLatch.Publish(
                active,
                pulse,
                verdict,
                bpCount: snap.Breakpoints.Count,
                stopped: effectiveStopped,
                activeDap: snap.ActiveDap,
                stack: stack,
                locals: locals);
        }
        catch
        {
            /* best-effort */
        }
    }

    static async Task PublishLiveAsync(bool forceStopped)
    {
        try
        {
            var snap = CaptureLive();
            var stopped = forceStopped || snap.Stopped;
            PublishLiveChrome(stopped, stack: null, locals: null);
            if (!stopped || DebugSession.CurrentClient is null || snap.LastStoppedThreadId <= 0)
                return;

            var (stack, locals) = await TryCaptureFramesAsync(
                    DebugSession.CurrentClient,
                    snap.LastStoppedThreadId)
                .ConfigureAwait(false);
            if (stack.Count == 0 && locals.Count == 0)
                return;

            PublishLiveChrome(stopped: true, stack, locals);
        }
        catch
        {
            /* best-effort */
        }
    }

    static Snap CaptureLive()
    {
        var ws = DebugSession.WorkspacePath;
        var target = DebugSession.TargetPath;
        IReadOnlyList<BreakpointsStorage.BreakpointEntry> bps =
            Array.Empty<BreakpointsStorage.BreakpointEntry>();
        if (ws is { Length: > 0 } && target is { Length: > 0 })
        {
            try { bps = BreakpointsStorage.GetBreakpoints(ws, target); }
            catch { /* ignore */ }
        }

        string? launch = null;
        if (target is { Length: > 0 })
        {
            try { launch = LaunchTargetResolver.TryResolveBinary(target); }
            catch { /* ignore */ }
        }

        return new Snap(
            ws,
            target,
            launch,
            null,
            DebugSession.CurrentClient is not null,
            DebugSession.LastStoppedThreadId > 0,
            DebugSession.LastStoppedThreadId,
            DebugSession.LastExceptionText,
            bps);
    }

    static async Task<(
        IReadOnlyList<CideDebugDeskLatch.StackFrameDoc> Stack,
        IReadOnlyList<CideDebugDeskLatch.LocalVarDoc> Locals)> TryCaptureFramesAsync(
        DapClient client,
        int threadId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var ct = cts.Token;
        JsonElement? body;
        try
        {
            body = await DapShared.WithRetryAsync(() => client.StackTraceAsync(threadId, cancellationToken: ct))
                .ConfigureAwait(false);
        }
        catch
        {
            return ([], []);
        }

        if (body is null || !body.Value.TryGetProperty("stackFrames", out var framesEl)
            || framesEl.ValueKind != JsonValueKind.Array)
            return ([], []);

        var stack = new List<CideDebugDeskLatch.StackFrameDoc>(16);
        int? topFrameId = null;
        foreach (var f in framesEl.EnumerateArray())
        {
            var name = f.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?";
            var line = f.TryGetProperty("line", out var ln) && ln.TryGetInt32(out var li) ? li : 0;
            string? path = null;
            if (f.TryGetProperty("source", out var src) && src.TryGetProperty("path", out var p))
                path = p.GetString();
            if (topFrameId is null && f.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id))
                topFrameId = id;
            stack.Add(new CideDebugDeskLatch.StackFrameDoc { Name = name, File = path, Line = line });
            if (stack.Count >= 24)
                break;
        }

        var locals = new List<CideDebugDeskLatch.LocalVarDoc>(24);
        if (topFrameId is int frameId)
        {
            try
            {
                var scopesBody = await DapShared.WithRetryAsync(() => client.ScopesAsync(frameId, ct))
                    .ConfigureAwait(false);
                if (scopesBody is not null
                    && scopesBody.Value.TryGetProperty("scopes", out var scopes)
                    && scopes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var scope in scopes.EnumerateArray())
                    {
                        var scopeName = scope.TryGetProperty("name", out var sn) ? sn.GetString() : null;
                        if (scopeName is null
                            || (!scopeName.Contains("Local", StringComparison.OrdinalIgnoreCase)
                                && !scopeName.Equals("Locals", StringComparison.OrdinalIgnoreCase)))
                            continue;
                        if (!scope.TryGetProperty("variablesReference", out var vrefEl)
                            || !vrefEl.TryGetInt32(out var vref)
                            || vref <= 0)
                            continue;

                        var varsBody = await DapShared.WithRetryAsync(
                                () => client.VariablesAsync(vref, cancellationToken: ct))
                            .ConfigureAwait(false);
                        if (varsBody is null
                            || !varsBody.Value.TryGetProperty("variables", out var vars)
                            || vars.ValueKind != JsonValueKind.Array)
                            break;

                        foreach (var v in vars.EnumerateArray())
                        {
                            var vn = v.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "?" : "?";
                            var vv = v.TryGetProperty("value", out var valEl) ? valEl.GetString() ?? "" : "";
                            locals.Add(new CideDebugDeskLatch.LocalVarDoc { Name = vn, Value = vv });
                            if (locals.Count >= 32)
                                break;
                        }

                        break;
                    }
                }
            }
            catch
            {
                /* locals optional */
            }
        }

        return (stack, locals);
    }
}
