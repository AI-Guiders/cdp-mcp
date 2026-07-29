#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.ComputingUnits;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>SYS organ + alert inputs + session pulse.</summary>
internal static partial class IdeCockpit
{
    static object SessionPulse(SessionContext session) => new
    {
        phase = CdpEnumParse.ToWire(session.Phase),
        @object = CdpEnumParse.ToWire(session.Object),
        language = session.Language,
        project_root = session.ProjectRoot,
        scm_root = session.ScmRoot,
        solution_or_project_path = session.SolutionOrProjectPath
    };

    static IdeAlertChannel.Inputs BuildAlertInputs(
        SessionContext session,
        QualityGates.QualitySnap quality,
        BufferSnap buffer,
        DebugSnap debug,
        ShellSnap shell,
        JsonElement? git,
        IdeProblemsChannel.Snap problems,
        WorkSnap work,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        IdeChkChannel.Snap? chk = null)
    {
        var seats = IdeDeskSeats.IsSeatsMode()
            ? IdeDeskSeats.Snapshot()
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var (layoutHint, seatNote) = IdeAlertChannel.SuggestLayout(session.Phase, session.Object, seats);
        var intent = session.Intent is { } i
            ? CdpEnumParse.ToWire(i)
            : work.Pulse;
        var locus = ResolveLocusLine(buffer, session.ProjectRoot);
        var intercomPulse = CideIntercomVoiceLatch.DeskPulseLine();
        var seatWithIntercom = string.IsNullOrWhiteSpace(intercomPulse)
            ? seatNote
            : string.IsNullOrWhiteSpace(seatNote)
                ? intercomPulse
                : $"{seatNote} · {intercomPulse}";
        var sit = new IdeAlertChannel.Sit(
            $"{CdpEnumParse.ToWire(session.Phase)}/{CdpEnumParse.ToWire(session.Object)}",
            intent,
            locus,
            layoutHint,
            seatWithIntercom);

        string? stageMismatch = null;
        if (workspaceStore is not null
            && workspaceState.ActiveStageId is { } sid
            && workspaceStore.TryGetStagePhaseAffinity(sid) is { Length: > 0 } aff)
        {
            var sessionPhase = CdpEnumParse.ToWire(session.Phase);
            if (!aff.Equals(sessionPhase, StringComparison.OrdinalIgnoreCase))
                stageMismatch = $"phase mismatch task@{aff} · session={sessionPhase}";
        }

        return new IdeAlertChannel.Inputs(
            quality,
            buffer.DiskChangedCount,
            debug.ActiveDap,
            debug.Stopped,
            problems.Errors,
            problems.Warnings,
            shell.Running,
            shell.Failed,
            GitIsDirty(git),
            sit,
            stageMismatch,
            chk?.OpenRequired ?? 0,
            chk?.Pulse);
    }

    static string? ResolveLocusLine(BufferSnap buffer, string? projectRoot)
    {
        // Dual-cockpit glass: human locus wired into sit; agent only looks at desk.
        var human = NavigationFocusLatch.TryRead();
        if (human?.Path is { Length: > 0 } hp)
        {
            var suffix = human.Line > 0 ? $":{human.Line}" : "";
            return FormatLocusPath(hp, projectRoot, suffix);
        }

        if (buffer.Docs.Count == 0)
            return null;
        var hot = buffer.Docs.FirstOrDefault(d => d.DiskChanged)
            ?? buffer.Docs.FirstOrDefault(d => d.Dirty)
            ?? buffer.Docs[0];
        var mark = hot.DiskChanged ? " disk" : hot.Dirty ? " dirty" : "";
        return FormatLocusPath(hot.Path, projectRoot, mark);
    }

    static string FormatLocusPath(string path, string? projectRoot, string suffix)
    {
        if (projectRoot is { Length: > 0 }
            && path.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            var rel = path[projectRoot.Length..].TrimStart('\\', '/');
            if (rel.Length > 0) path = rel;
        }

        if (path.Length > 64)
            path = "…" + path[^60..];
        return $"{path}{suffix}";
    }

    static readonly DeskSysOrganUnit DeskSys = new();

    static object BuildSysOrgan(
        SessionContext session,
        JsonElement? gitRoot,
        ShellSnap shell,
        BufferSnap buffer,
        DebugSnap debug,
        TestSnap test,
        WorkSnap work) =>
        DeskSys.Build(new DeskSysOrganUnit.Input(
            ProjectRoot: session.ProjectRoot,
            OpsPulse: IdeOpsPulse.Line(),
            GitPulse: GitPulseLine(gitRoot),
            BufferCount: buffer.Count,
            BufferDirty: buffer.DirtyCount,
            BufferDiskChanged: buffer.DiskChangedCount,
            ShellTabCount: shell.TabCount,
            ShellRunning: shell.Running,
            ShellFailed: shell.Failed,
            DebugActiveDap: debug.ActiveDap,
            DebugStopped: debug.Stopped,
            DebugBreakpointCount: debug.BreakpointCount,
            TestAvailable: test.Available,
            TestReason: test.Reason,
            TestLastRun: test.LastRun,
            TestSuccess: test.Success,
            TestPassed: test.Passed,
            TestTotal: test.Total,
            WorkPulse: work.Pulse));
}
