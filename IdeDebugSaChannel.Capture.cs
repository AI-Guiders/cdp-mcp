#nullable enable
using Cdp.Core;
using DotnetDebug.Core;
using DotnetDebugMcp;

namespace CdpMcp;

internal static partial class IdeDebugSaChannel
{
    static Snap Capture(SessionContext session)
    {
        var ws = session.ProjectRoot ?? session.ScmRoot;
        var target = session.SolutionOrProjectPath;
        string? note = ws is null ? "No session project — cdp_open first." : null;

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
            note,
            DebugSession.CurrentClient is not null,
            DebugSession.LastStoppedThreadId > 0,
            DebugSession.LastStoppedThreadId,
            DebugSession.LastExceptionText,
            bps);
    }

}
