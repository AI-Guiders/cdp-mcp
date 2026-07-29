#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.Channels;
using CdpMcp.Cockpit.Surface;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Channel role for cockpit BuildAsync (CIDE ADR 0036 / arch_desk wire).
/// Deferred soft organs via <see cref="DeferredSoftOrganChannel"/> → <see cref="IdeSoftOrganBoard"/>.
/// </summary>
internal static partial class IdeCockpit
{
    static readonly DeferredSoftOrganChannel SoftOrganChannel = new();
    static readonly SoftOrganBoardMetaCatalog DeferredSoftMeta = new();

    private readonly record struct DeferredSoftWants(
        bool Sys,
        bool Chk,
        bool Qrh,
        bool Alert,
        bool Problems,
        bool Plugins,
        bool Review);

    /// <summary>Peek go=sys|chk|qrh|alert|problems|plugins|review and clear goVerb.</summary>
    private static DeferredSoftWants PeekDeferredSoftWants(ref string? goVerb)
    {
        var (payload, residual) = SoftOrganChannel.Peek(goVerb);
        goVerb = residual;
        return new DeferredSoftWants(
            payload.Sys, payload.Chk, payload.Qrh, payload.Alert,
            payload.Problems, payload.Plugins, payload.Review);
    }

    /// <summary>
    /// Apply deferred soft organs from CDS snaps. Alert/sa is root EICAS — no PlaceOrgan.
    /// </summary>
    private static (object? GoResult, IdeAlertChannel.Snap AlertSnap, IdeAlertChannel.Inputs AlertInputs) ApplyDeferredSoftOrgans(
        DeferredSoftWants wants,
        object? goResult,
        SessionContext session,
        DocumentBufferStore docStore,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        IReadOnlyDictionary<string, JsonElement> args,
        JsonElement? git,
        ShellSnap shell,
        BufferSnap buffer,
        DebugSnap debug,
        TestSnap test,
        WorkSnap work,
        QualityGates.QualitySnap quality,
        IdeProblemsChannel.Snap problems,
        IdeChkChannel.ProbeCtx chkCtx,
        IdeChkChannel.Snap chkSnap)
    {
        var gitDirty = GitIsDirty(git);
        var testsFailed = test is { Available: true, LastRun: not null, Success: false };

        var alertInputs = BuildAlertInputs(
            session, quality, buffer, debug, shell, git, problems, work, workspaceStore, workspaceState, chkSnap);
        var alertSnap = IdeAlertChannel.Build(alertInputs);
        CideAlertLatch.Publish(alertSnap);
        CideQrhLatch.Publish(IdeQrhChannel.Build(chkCtx, chkSnap));
        CideEclLatch.Publish(chkSnap);
        IdePressureChannel.PublishGlass();
        IdeIgniteArmHost.PublishGlass();
        IdeScopeChannel.PublishGlass();
        IdeOpsPulse.PublishGlass();
        IdeOnboardChannel.PublishGlass(session);
        IdeArchBoardChannel.PublishGlass(session);
        McpOutletHabitat.Instance?.PublishGlass();
        IdeTaskManager.PublishGlass(workspaceStore, workspaceState, CdpEnumParse.ToWire(session.Phase));
        IdeReportBoard.PublishGlass(session);
        IdeCrmChannel.PublishGlass(session);
        IdeWebcamChannel.PublishGlass();
        IdeToolchainChannel.PublishGlass();
        IdePluginsChannel.PublishGlass();
        IdeRefactorPlanChannel.PublishGlass(docStore, session);

        var tile = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
        var board = new IdeSoftOrganBoard(new SoftOrganSeatBag(
            tile,
            session,
            docStore,
            workspaceStore,
            workspaceState,
            Extras: new SoftOrganSeatExtras(
                alertInputs,
                () => BuildSysOrgan(session, git, shell, buffer, debug, test, work),
                chkCtx,
                chkSnap,
                gitDirty,
                problems,
                testsFailed,
                quality)));

        // Soft organs that own a seat: place BEFORE SA fuse so same-turn map matches desk.
        if (wants.Problems)
            goResult = PlaceDeferred(board, SoftOrganKind.Problems);
        if (wants.Plugins)
            goResult = PlaceDeferred(board, SoftOrganKind.Plugins);
        if (wants.Review)
            goResult = PlaceDeferred(board, SoftOrganKind.Review);
        if (wants.Sys)
            goResult = PlaceDeferred(board, SoftOrganKind.Sys);
        if (wants.Chk)
            goResult = PlaceDeferred(board, SoftOrganKind.Ecl);
        if (wants.Qrh)
            goResult = PlaceDeferred(board, SoftOrganKind.Qrh);

        if (wants.Alert)
            goResult = board.Build(SoftOrganKind.Alert).Board;

        return (goResult, alertSnap, alertInputs);
    }

    static object PlaceDeferred(IdeSoftOrganBoard board, SoftOrganKind kind)
    {
        var hit = board.Build(kind);
        if (IdeDeskSeats.IsSeatsMode())
            IdeDeskSeats.PlaceOrgan(DeferredSoftMeta.Require(kind).Go);
        return hit.Board;
    }
}
