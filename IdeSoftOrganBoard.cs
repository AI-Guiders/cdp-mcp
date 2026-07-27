#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.Surface;
using CdpMcp.IntentWorkspace;
using DotNetBuildTest.Core;

namespace CdpMcp;

/// <summary>ISoftOrganBoard: wraps Ide* Handle (+ plan/review/sys) for seats and SoftDispatch.</summary>
internal sealed class IdeSoftOrganBoard : ISoftOrganBoard
{
    readonly SoftOrganSeatBag _bag;

    public IdeSoftOrganBoard(in SoftOrganSeatBag bag) => _bag = bag;

        public SoftOrganBoardHit Build(SoftOrganKind kind) => kind switch
    {
        SoftOrganKind.Plan => BuildPlan(),
        SoftOrganKind.Report => Hit(IdeReportBoard.Handle(_bag.Session, _bag.TileArgs)),
        SoftOrganKind.FindDesk => Hit(IdeFindChannel.Handle(_bag.DocStore!, _bag.Session, _bag.TileArgs)),
        SoftOrganKind.SaDesk => Hit(IdeSaChannel.Handle(_bag.DocStore!, _bag.Session, _bag.TileArgs)),
        SoftOrganKind.DebugDesk => Hit(IdeDebugSaChannel.Handle(_bag.Session, _bag.TileArgs)),
        SoftOrganKind.TestDesk => Hit(IdeTestSaChannel.Handle(_bag.Session, _bag.TileArgs)),
        SoftOrganKind.BuildDesk => Hit(IdeBuildSaChannel.Handle(_bag.Session, _bag.TileArgs)),
        SoftOrganKind.Crm => Hit(IdeCrmChannel.Handle(
            _bag.Session, _bag.WorkspaceStore, _bag.WorkspaceState!, _bag.TileArgs)),
        SoftOrganKind.FilesDesk => Hit(IdeFilesChannel.Handle(_bag.DocStore!, _bag.Session, _bag.TileArgs)),
        SoftOrganKind.IgniteDesk => Hit(IdeIgniteChannel.Handle(_bag.TileArgs)),
        SoftOrganKind.WebcamDesk => Hit(IdeWebcamChannel.Handle(_bag.Session, _bag.TileArgs)),
        SoftOrganKind.PressureDesk => Hit(
            IdePressureChannel.Handle(_bag.Session, _bag.TileArgs),
            IdePressureChannel.PulseLine(),
            IdePressureChannel.SchemaVersion),
        SoftOrganKind.OnboardDesk => Hit(
            IdeOnboardChannel.Handle(_bag.Session, _bag.TileArgs),
            IdeOnboardChannel.PulseLine(_bag.Session),
            IdeOnboardChannel.SchemaVersion),
        SoftOrganKind.Toolchain => Hit(
            IdeToolchainChannel.Handle(_bag.Session, _bag.TileArgs),
            IdeToolchainChannel.PulseLine(_bag.Session),
            IdeToolchainChannel.SchemaVersion),
        SoftOrganKind.Alert => Hit(IdeAlertChannel.Handle(RequireExtras().AlertInputs, _bag.TileArgs)),
        SoftOrganKind.Problems => Hit(
            IdeProblemsChannel.Handle(_bag.DocStore!, _bag.Session, _bag.TileArgs),
            IdeProblemsChannel.Build(_bag.DocStore!, _bag.Session).Pulse),
        SoftOrganKind.Plugins => Hit(
            IdePluginsChannel.Handle(_bag.DocStore!, _bag.Session, _bag.TileArgs),
            IdePluginsChannel.Build().Pulse),
        SoftOrganKind.Quality => BuildQuality(),
        SoftOrganKind.Sys => Hit(RequireExtras().SysBoard()),
        SoftOrganKind.Ecl => Hit(IdeChkChannel.Handle(RequireExtras().ChkCtx, _bag.TileArgs)),
        SoftOrganKind.Qrh => Hit(IdeQrhChannel.Handle(
            RequireExtras().ChkCtx, _bag.TileArgs, RequireExtras().ChkSnap)),
        SoftOrganKind.Review => BuildReview(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };


    static SoftOrganBoardHit Hit(object board, string? pulse = null, string? schema = null) =>
        new(board, pulse, schema);

    SoftOrganSeatExtras RequireExtras() =>
        _bag.Extras ?? throw new InvalidOperationException(
            "SoftOrganSeatExtras required for alert/sys/ecl/qrh/review boards.");

    SoftOrganBoardHit BuildPlan()
    {
        if (_bag.WorkspaceStore is null)
        {
            return Hit(new
            {
                ok = false,
                go = "plan",
                error = "no_workspace",
                hint = "Intent workspace WitDB unavailable."
            });
        }

        var tmArgs = new Dictionary<string, JsonElement>(_bag.TileArgs, StringComparer.Ordinal);
        if (_bag.Session.ProjectRoot is { Length: > 0 } pr)
            tmArgs["project_root"] = JsonSerializer.SerializeToElement(pr);
        tmArgs["session_phase"] = JsonSerializer.SerializeToElement(
            CdpEnumParse.ToWire(_bag.Session.Phase));

        var goVerb = _bag.GoVerb;
        if (!tmArgs.ContainsKey("tm_op")
            && goVerb is "feature" or "task" or "promote" or "confirm" or "reject"
            && (!tmArgs.TryGetValue("go_args", out var gax)
                || gax.ValueKind != JsonValueKind.Object
                || !gax.TryGetProperty("op", out _)))
        {
            tmArgs["tm_op"] = JsonSerializer.SerializeToElement(
                goVerb.Equals("feature", StringComparison.OrdinalIgnoreCase) ? "feature"
                : goVerb.Equals("task", StringComparison.OrdinalIgnoreCase) ? "task"
                : goVerb.Equals("promote", StringComparison.OrdinalIgnoreCase) ? "promote"
                : goVerb.Equals("confirm", StringComparison.OrdinalIgnoreCase) ? "confirm"
                : goVerb.Equals("reject", StringComparison.OrdinalIgnoreCase) ? "reject"
                : "board");
        }

        return Hit(IdeTaskManager.Handle(_bag.WorkspaceStore, _bag.WorkspaceState!, tmArgs));
    }
    SoftOrganBoardHit BuildQuality()
    {
        var store = _bag.DocStore!;
        var root = _bag.Session.ProjectRoot;
        var path = OptTileString("path");
        var board = string.IsNullOrWhiteSpace(path)
            ? QualityGates.EvaluateStore(store, root)
            : QualityGates.EvaluatePath(store, root, path!);
        return Hit(board, QualityGates.Snap(store, root).Pulse);
    }

    string? OptTileString(string key) =>
        _bag.TileArgs.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;


    SoftOrganBoardHit BuildReview()
    {
        var x = RequireExtras();
        var reviewInputs = new IdeReviewChannel.Inputs(
            _bag.Session,
            x.GitDirty,
            x.Problems.Errors,
            x.TestsFailed,
            x.Quality.Fail,
            x.Quality.Warn,
            x.ChkSnap);
        return Hit(IdeReviewChannel.Handle(reviewInputs, _bag.TileArgs));
    }
}

/// <summary>Seat/deferred extras — only needed for alert/sys/ecl/qrh/review.</summary>
internal readonly record struct SoftOrganSeatExtras(
    IdeAlertChannel.Inputs AlertInputs,
    Func<object> SysBoard,
    IdeChkChannel.ProbeCtx ChkCtx,
    IdeChkChannel.Snap ChkSnap,
    bool GitDirty,
    IdeProblemsChannel.Snap Problems,
    bool TestsFailed,
    QualityGates.QualitySnap Quality);

/// <summary>Core bag for <see cref="IdeSoftOrganBoard"/> (SoftDispatch + seats).</summary>
internal readonly record struct SoftOrganSeatBag(
    Dictionary<string, JsonElement> TileArgs,
    SessionContext Session,
    DocumentBufferStore? DocStore,
    IntentWorkspaceStore? WorkspaceStore,
    IntentWorkspaceState? WorkspaceState,
    string? GoVerb = null,
    SoftOrganSeatExtras? Extras = null);
