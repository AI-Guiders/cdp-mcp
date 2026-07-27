#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Channel role for cockpit BuildAsync (CIDE ADR 0036 / arch_desk wire).
/// Deferred soft organs need Collect* snaps — CCU peeks wants early, Channel applies after CDS.
/// </summary>
internal static partial class IdeCockpit
{
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
        var wantSys = goVerb is { Length: > 0 }
            && goVerb.Equals("sys", StringComparison.OrdinalIgnoreCase);
        if (wantSys)
            goVerb = null;

        var wantChk = goVerb is { Length: > 0 }
            && (goVerb.Equals("chk", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("ecl", StringComparison.OrdinalIgnoreCase));
        if (wantChk)
            goVerb = null;

        var wantQrh = goVerb is { Length: > 0 }
            && (goVerb.Equals("qrh", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("eqrh", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("handbook", StringComparison.OrdinalIgnoreCase));
        if (wantQrh)
            goVerb = null;

        var wantAlert = goVerb is { Length: > 0 }
            && (goVerb.Equals("alert", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("eicas", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("sa", StringComparison.OrdinalIgnoreCase));
        if (wantAlert)
            goVerb = null;

        var wantProblems = goVerb is { Length: > 0 }
            && (goVerb.Equals("problems", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("problem", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("errlist", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("errorlist", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("err", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("diags", StringComparison.OrdinalIgnoreCase));
        if (wantProblems)
            goVerb = null;

        var wantPlugins = goVerb is { Length: > 0 }
            && (goVerb.Equals("plugins", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("plugin", StringComparison.OrdinalIgnoreCase)
                || goVerb.Equals("vsix", StringComparison.OrdinalIgnoreCase));
        if (wantPlugins)
            goVerb = null;

        var wantReview = goVerb is { Length: > 0 }
            && goVerb.Equals("review", StringComparison.OrdinalIgnoreCase);
        if (wantReview)
            goVerb = null;

        return new DeferredSoftWants(wantSys, wantChk, wantQrh, wantAlert, wantProblems, wantPlugins, wantReview);
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
