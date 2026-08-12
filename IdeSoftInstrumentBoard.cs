#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.Surface;
using CdpMcp.IntentWorkspace;
using DotNetBuildTest.Core;

namespace CdpMcp;

/// <summary>ISoftInstrumentBoard: wraps Ide* Handle (+ plan/review/sys) for seats and SoftDispatch.</summary>
internal sealed class IdeSoftInstrumentBoard : ISoftInstrumentBoard
{
    readonly SoftInstrumentSeatBag _bag;

    public IdeSoftInstrumentBoard(in SoftInstrumentSeatBag bag) => _bag = bag;

        public SoftInstrumentBoardHit Build(SoftInstrumentKind kind) => kind switch
    {
        SoftInstrumentKind.Plan => BuildPlan(),
        SoftInstrumentKind.Report => Hit(IdeReportBoard.Handle(_bag.Session, _bag.TileArgs)),
        SoftInstrumentKind.FindDesk => Hit(IdeFindChannel.Handle(_bag.DocStore!, _bag.Session, _bag.TileArgs)),
        SoftInstrumentKind.SaDesk => Hit(IdeSaChannel.Handle(_bag.DocStore!, _bag.Session, _bag.TileArgs)),
        SoftInstrumentKind.DebugDesk => Hit(IdeDebugSaChannel.Handle(_bag.Session, _bag.TileArgs)),
        SoftInstrumentKind.TestDesk => Hit(IdeTestSaChannel.Handle(_bag.Session, _bag.TileArgs)),
        SoftInstrumentKind.BuildDesk => Hit(IdeBuildSaChannel.Handle(_bag.Session, _bag.TileArgs)),
        SoftInstrumentKind.Crm => Hit(IdeCrmChannel.Handle(
            _bag.Session, _bag.WorkspaceStore, _bag.WorkspaceState!, _bag.TileArgs)),
        SoftInstrumentKind.FilesDesk => Hit(IdeFilesChannel.Handle(_bag.DocStore!, _bag.Session, _bag.TileArgs)),
        SoftInstrumentKind.IgniteDesk => Hit(IdeIgniteChannel.Handle(_bag.TileArgs)),
        SoftInstrumentKind.WebcamDesk => Hit(IdeWebcamChannel.Handle(_bag.Session, _bag.TileArgs)),
        SoftInstrumentKind.PressureDesk => Hit(
            IdePressureChannel.Handle(_bag.Session, _bag.TileArgs),
            IdePressureChannel.PulseLine(),
            IdePressureChannel.SchemaVersion),
        SoftInstrumentKind.OnboardDesk => Hit(
            IdeOnboardChannel.Handle(_bag.Session, _bag.TileArgs),
            IdeOnboardChannel.PulseLine(_bag.Session),
            IdeOnboardChannel.SchemaVersion),
        SoftInstrumentKind.Toolchain => Hit(
            IdeToolchainChannel.Handle(_bag.Session, _bag.TileArgs),
            IdeToolchainChannel.PulseLine(_bag.Session),
            IdeToolchainChannel.SchemaVersion),
        SoftInstrumentKind.Alert => Hit(IdeAlertChannel.Handle(RequireExtras().AlertInputs, _bag.TileArgs)),
        SoftInstrumentKind.Problems => Hit(
            IdeProblemsChannel.Handle(_bag.DocStore!, _bag.Session, _bag.TileArgs),
            IdeProblemsChannel.Build(_bag.DocStore!, _bag.Session).Pulse),
        SoftInstrumentKind.Plugins => Hit(
            IdePluginsChannel.Handle(_bag.DocStore!, _bag.Session, _bag.TileArgs),
            IdePluginsChannel.Build().Pulse),
        SoftInstrumentKind.Quality => BuildQuality(),
        SoftInstrumentKind.ArchDesk => Hit(IdeArchBoardChannel.Handle(_bag.Session, _bag.TileArgs)),
        SoftInstrumentKind.RefactorPlan => Hit(
            IdeRefactorPlanChannel.Handle(_bag.DocStore!, _bag.Session, _bag.TileArgs)),
        SoftInstrumentKind.Ps1Desk => BuildPs1(),
        SoftInstrumentKind.Sys => Hit(RequireExtras().SysBoard()),
        SoftInstrumentKind.Ecl => Hit(IdeChkChannel.Handle(RequireExtras().ChkCtx, _bag.TileArgs)),
        SoftInstrumentKind.Qrh => Hit(IdeQrhChannel.Handle(
            RequireExtras().ChkCtx, _bag.TileArgs, RequireExtras().ChkSnap)),
        SoftInstrumentKind.Review => BuildReview(),
        SoftInstrumentKind.MdAuthor => Hit(
            IdeMdAuthorChannel.Handle(_bag.Session, _bag.TileArgs),
            IdeMdAuthorChannel.PulseLine(_bag.Session),
            IdeMdAuthorChannel.SchemaVersion),
        SoftInstrumentKind.Learn => Hit(
            IdeLearnChannel.Handle(_bag.Session, _bag.TileArgs),
            IdeLearnChannel.PulseLine(_bag.Session),
            IdeLearnChannel.SchemaVersion),
        SoftInstrumentKind.ProjectSwitch => Hit(
            IdeScopeChannel.Handle(_bag.Session, _bag.TileArgs),
            IdeScopeChannel.PulseLine(_bag.Session),
            IdeScopeChannel.SchemaVersion),
        SoftInstrumentKind.Domain => Hit(
            IdeDomainChannel.Handle(_bag.Session, _bag.TileArgs),
            IdeDomainChannel.PulseLine(_bag.Session),
            IdeDomainChannel.SchemaVersion),
        SoftInstrumentKind.Calendar => Hit(
            IdeCalendarChannel.Handle(_bag.Session, _bag.TileArgs),
            IdeCalendarChannel.PulseLine(_bag.Session),
            IdeCalendarChannel.SchemaVersion),
        SoftInstrumentKind.Rules => Hit(
            IdeRulesChannel.Handle(_bag.Session, _bag.TileArgs),
            IdeRulesChannel.PulseLine(_bag.Session),
            IdeRulesChannel.SchemaVersion),
        SoftInstrumentKind.Inventory => Hit(
            IdeInventoryChannel.Handle(_bag.Session, _bag.TileArgs),
            IdeInventoryChannel.PulseLine(_bag.Session),
            IdeInventoryChannel.SchemaVersion),
        SoftInstrumentKind.VerifyWave => Hit(
            IdeVerifyWaveChannel.Handle(_bag.Session, _bag.TileArgs),
            IdeVerifyWaveChannel.PulseLine(_bag.Session),
            IdeVerifyWaveChannel.SchemaVersion),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };


    static SoftInstrumentBoardHit Hit(object board, string? pulse = null, string? schema = null) =>
        new(board, pulse, schema);

    SoftInstrumentSeatExtras RequireExtras() =>
        _bag.Extras ?? throw new InvalidOperationException(
            "SoftInstrumentSeatExtras required for alert/sys/ecl/qrh/review boards.");

    SoftInstrumentBoardHit BuildPlan()
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

    SoftInstrumentBoardHit BuildQuality()
    {
        var store = _bag.DocStore!;
        var root = _bag.Session.ProjectRoot;
        var scope = OptTileString("scope") ?? OptTileString("scan");
        if (scope is "assert" or "assertions" or "adx")
        {
            var board = AdxAssertions.Evaluate(root);
            return Hit(board, AssertPulse(board));
        }

        if (scope is "disk" or "project" or "map")
        {
            var limit = OptTileInt("limit") ?? 40;
            var board = QualityGates.EvaluateDisk(root, limit);
            var pulse = DiskPulse(board);
            return Hit(board, pulse);
        }

        var path = OptTileString("path");
        var openBoard = string.IsNullOrWhiteSpace(path)
            ? QualityGates.EvaluateStore(store, root)
            : QualityGates.EvaluatePath(store, root, path!);
        return Hit(openBoard, QualityGates.Snap(store, root).Pulse);
    }

    static string AssertPulse(object board)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(board));
            if (doc.RootElement.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString() ?? "assert";
        }
        catch
        {
            /* pulse best-effort */
        }

        return "assert";
    }

    static string DiskPulse(object board)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(board));
            if (doc.RootElement.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString() ?? "disk";
        }
        catch
        {
            // fall through
        }

        return "disk";
    }

    SoftInstrumentBoardHit BuildPs1()
    {
        var (_, pulse) = Ps1Scene.Pulse(_bag.Session);
        return Hit(Ps1Scene.Board(_bag.Session), pulse, Ps1Scene.Schema);
    }

    string? OptTileString(string key) =>
        _bag.TileArgs.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    int? OptTileInt(string key)
    {
        if (!_bag.TileArgs.TryGetValue(key, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            return n;
        if (el.ValueKind == JsonValueKind.String
            && int.TryParse(el.GetString(), out var parsed))
            return parsed;
        return null;
    }


    SoftInstrumentBoardHit BuildReview()
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
internal readonly record struct SoftInstrumentSeatExtras(
    IdeAlertChannel.Inputs AlertInputs,
    Func<object> SysBoard,
    IdeChkChannel.ProbeCtx ChkCtx,
    IdeChkChannel.Snap ChkSnap,
    bool GitDirty,
    IdeProblemsChannel.Snap Problems,
    bool TestsFailed,
    QualityGates.QualitySnap Quality);

/// <summary>Core bag for <see cref="IdeSoftInstrumentBoard"/> (SoftDispatch + seats).</summary>
internal readonly record struct SoftInstrumentSeatBag(
    Dictionary<string, JsonElement> TileArgs,
    SessionContext Session,
    DocumentBufferStore? DocStore,
    IntentWorkspaceStore? WorkspaceStore,
    IntentWorkspaceState? WorkspaceState,
    string? GoVerb = null,
    SoftInstrumentSeatExtras? Extras = null);
