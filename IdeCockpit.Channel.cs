#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.Channels;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Channel role for cockpit BuildAsync (CIDE ADR 0036 / arch_desk wire).
/// Deferred soft organs via <see cref="DeferredSoftOrganChannel"/>.
/// </summary>
internal static partial class IdeCockpit
{
    static readonly DeferredSoftOrganChannel SoftOrganChannel = new();

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

        // Soft organs that own a seat: place BEFORE SA fuse so same-turn map matches desk.
        if (wants.Problems)
        {
            goResult = IdeProblemsChannel.Handle(docStore, session, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("problems");
        }

        if (wants.Plugins)
        {
            goResult = IdePluginsChannel.Handle(docStore, session, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("plugins");
        }

        if (wants.Review)
        {
            var reviewInputs = new IdeReviewChannel.Inputs(
                session,
                gitDirty,
                problems.Errors,
                testsFailed,
                quality.Fail,
                quality.Warn,
                chkSnap);
            goResult = IdeReviewChannel.Handle(reviewInputs, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("review");
        }

        if (wants.Sys)
        {
            goResult = BuildSysOrgan(session, git, shell, buffer, debug, test, work);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("sys");
        }

        if (wants.Chk)
        {
            goResult = IdeChkChannel.Handle(chkCtx, args);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("ecl");
        }

        if (wants.Qrh)
        {
            goResult = IdeQrhChannel.Handle(chkCtx, args, chkSnap);
            if (IdeDeskSeats.IsSeatsMode())
                IdeDeskSeats.PlaceOrgan("qrh");
        }

        var alertInputs = BuildAlertInputs(
            session, quality, buffer, debug, shell, git, problems, work, workspaceStore, workspaceState, chkSnap);
        var alertSnap = IdeAlertChannel.Build(alertInputs);

        if (wants.Alert)
            goResult = IdeAlertChannel.Handle(alertInputs, args);

        return (goResult, alertSnap, alertInputs);
    }
}
