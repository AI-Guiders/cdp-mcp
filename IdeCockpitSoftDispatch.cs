#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Soft-organ <c>go=</c> dispatch extracted from <see cref="IdeCockpit.BuildAsync"/>.
/// Partials keep the routing map thin while preserving behavior.
/// </summary>
internal static partial class IdeCockpitSoftDispatch
{
    public static void TryDispatch(
        ref string? goVerb,
        ref object? goResult,
        ref string mfd,
        SessionContext session,
        DocumentBufferStore docStore,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (TryDispatchQuality(ref goVerb, ref goResult, ref mfd, session, docStore, args)) return;
        if (TryDispatchReport(ref goVerb, ref goResult, session, args)) return;
        if (TryDispatchFind(ref goVerb, ref goResult, docStore, session, args)) return;
        if (TryDispatchCodeSa(ref goVerb, ref goResult, docStore, session, args)) return;
        if (TryDispatchRefactorPlan(ref goVerb, ref goResult, docStore, session, args)) return;
        if (TryDispatchDebugSa(ref goVerb, ref goResult, session, args)) return;
        if (TryDispatchTestSa(ref goVerb, ref goResult, session, args)) return;
        if (TryDispatchBuildSa(ref goVerb, ref goResult, session, args)) return;
        if (TryDispatchCrm(ref goVerb, ref goResult, session, workspaceStore, workspaceState, args)) return;
        if (TryDispatchPlan(ref goVerb, ref goResult, session, workspaceStore, workspaceState, args)) return;
        if (TryDispatchArch(ref goVerb, ref goResult, session, args)) return;
        if (TryDispatchOnboard(ref goVerb, ref goResult, session, args)) return;
        if (TryDispatchToolchain(ref goVerb, ref goResult, session, args)) return;
        if (TryDispatchFiles(ref goVerb, ref goResult, docStore, session, args)) return;
        if (TryDispatchMdAuthor(ref goVerb, ref goResult, session, args)) return;
        if (TryDispatchLearn(ref goVerb, ref goResult, session, args)) return;
        if (TryDispatchProjectSwitch(ref goVerb, ref goResult, session, args)) return;
        if (TryDispatchDomain(ref goVerb, ref goResult, session, args)) return;
        if (TryDispatchCalendar(ref goVerb, ref goResult, session, args)) return;
        if (TryDispatchIgnite(ref goVerb, ref goResult, session, args)) return;
        if (TryDispatchWebcam(ref goVerb, ref goResult, session, args)) return;
        if (TryDispatchPs1(ref goVerb, ref goResult, session, args)) return;
        TryDispatchPressure(ref goVerb, ref goResult, session, args);
    }
}
