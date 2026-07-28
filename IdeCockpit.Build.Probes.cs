#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;
using DotNetBuildTest.Core;
using DotnetDebug.Core;
using DotnetDebugMcp;

namespace CdpMcp;

/// <summary>BuildAsync probe collect peel — CDS snaps after habitat/go.</summary>
internal static partial class IdeCockpit
{
    readonly record struct DeskProbeBundle(
        DebugSnap Debug,
        TestSnap Test,
        WorkSnap Work,
        QualityGates.QualitySnap Quality,
        IdeProblemsChannel.Snap Problems,
        IdeChkChannel.ProbeCtx ChkCtx,
        IdeChkChannel.Snap ChkSnap,
        bool GitDirty,
        bool TestsFailed);

    static DeskProbeBundle CollectProbeBundle(
        SessionContext session,
        DocumentBufferStore docStore,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        JsonElement? git)
    {
        var debug = CollectDebug(session);
        var test = CollectTest(session);
        var work = CollectWork(workspaceStore, workspaceState, session);
        var quality = QualityGates.Snap(docStore, session.ProjectRoot);
        var problems = IdeProblemsChannel.Build(docStore, session);
        var gitKnown = git is not null;
        var gitDirty = GitIsDirty(git);
        var testsGreen = test is { Available: true, LastRun: not null, Success: true };
        var testsFailed = test is { Available: true, LastRun: not null, Success: false };
        var sniperOk = !quality.SuggestSniper || EditSniper.HasHold;
        var chkCtx = IdeChkChannel.CtxFrom(
            session, workspaceState.ActiveStageId is not null, !IdeIgniteArmHost.HasContinuityArms(), gitKnown, gitDirty, testsGreen, testsFailed,
            problems.Errors == 0, debug.Stopped, debug.ActiveDap, sniperOk);
        var chkSnap = IdeChkChannel.Build(chkCtx);
        return new DeskProbeBundle(
            debug, test, work, quality, problems, chkCtx, chkSnap, gitDirty, testsFailed);
    }
}
