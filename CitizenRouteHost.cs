#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Host execute for <see cref="CitizenIntentRouter.Route"/> — seat place + buffer open/replace + plan REPL + build + test + run + mcp + shell + debug + git + find + ignite.
/// Sync host path; <c>@intent build|test|run|script|calendar|land|pkg|project|sln|settings|options|restore|recent|mcp|shell|debug|git|kb|find|ignite</c> wait lifecycle/outlet/habitat/plane (bounded) — not cockpit W-spray.
/// </summary>
internal static partial class CitizenRouteHost
{
    public sealed record Applied(
        string Raw,
        string Verb,
        bool Ok,
        string? Action = null,
        string? Seat = null,
        string? Go = null,
        string? Path = null,
        string? DocId = null,
        string? Cmd = null,
        string? Pulse = null,
        string? Reason = null);

    public static IReadOnlyList<Applied> Execute(IEnumerable<CitizenIntentRouter.Route>? routes)
    {
        if (routes is null)
            return [];

        var list = new List<Applied>();
        foreach (var route in routes)
            list.Add(ApplyOne(route));
        return list;
    }

    static Applied ApplyOne(CitizenIntentRouter.Route route)
    {
        if (!route.Ok)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: route.Verb == CitizenIntentRouter.Verb.Refuse ? "refuse" : "skip",
                Cmd: route.Cmd,
                Reason: route.Reason ?? "route_not_ok");
        }

        return route.Verb switch
        {
            CitizenIntentRouter.Verb.Go
                or CitizenIntentRouter.Verb.Drill
                or CitizenIntentRouter.Verb.Detail
                => PlaceGo(route),
            CitizenIntentRouter.Verb.PaneFull => NotePaneFull(route),
            CitizenIntentRouter.Verb.Open => OpenPath(route),
            CitizenIntentRouter.Verb.Replace => ReplaceInPath(route),
            CitizenIntentRouter.Verb.Create => CreateInPath(route),
            CitizenIntentRouter.Verb.Append => AppendInPath(route),
            CitizenIntentRouter.Verb.Delete => DeleteInPath(route),
            CitizenIntentRouter.Verb.Build => RunBuild(route),
            CitizenIntentRouter.Verb.Test => RunTest(route),
            CitizenIntentRouter.Verb.Run => RunProject(route),
            CitizenIntentRouter.Verb.Mcp => RunMcp(route),
            CitizenIntentRouter.Verb.Kb => RunKb(route),
            CitizenIntentRouter.Verb.Shell => RunShell(route),
            CitizenIntentRouter.Verb.Debug => RunDebug(route),
            CitizenIntentRouter.Verb.Git => RunGit(route),
            CitizenIntentRouter.Verb.Find => RunFind(route),
            CitizenIntentRouter.Verb.Ide => RunIde(route),
            CitizenIntentRouter.Verb.Ignite => RunIgnite(route),
            CitizenIntentRouter.Verb.Pressure => RunPressure(route),
            CitizenIntentRouter.Verb.Browser => RunBrowser(route),
            CitizenIntentRouter.Verb.Script => RunScript(route),
            CitizenIntentRouter.Verb.Ps1 => RunPs1(route),
            CitizenIntentRouter.Verb.Icm => RunIcm(route),
            CitizenIntentRouter.Verb.Files => RunFiles(route),
            CitizenIntentRouter.Verb.Onboard => RunOnboard(route),
            CitizenIntentRouter.Verb.Peel => RunPeel(route),
            CitizenIntentRouter.Verb.EditPlan => RunEditPlan(route),
            CitizenIntentRouter.Verb.Analysis => RunAnalysis(route),
            CitizenIntentRouter.Verb.TestPlan => RunTestPlan(route),
            CitizenIntentRouter.Verb.TestScene => RunTestScene(route),
            CitizenIntentRouter.Verb.GotoAll => RunGotoAll(route),
            CitizenIntentRouter.Verb.EditorScene => RunEditorScene(route),
            CitizenIntentRouter.Verb.Man => RunMan(route),
            CitizenIntentRouter.Verb.Health => RunHealth(route),
            CitizenIntentRouter.Verb.Context => RunContext(route),
            CitizenIntentRouter.Verb.Quality => RunQuality(route),
            CitizenIntentRouter.Verb.Session => RunSession(route),
            CitizenIntentRouter.Verb.Tools => RunTools(route),
            CitizenIntentRouter.Verb.Capabilities => RunCapabilities(route),
            CitizenIntentRouter.Verb.Cockpit => RunCockpit(route),
            CitizenIntentRouter.Verb.Work => RunWork(route),
            CitizenIntentRouter.Verb.Sa => RunSa(route),
            CitizenIntentRouter.Verb.Learn => RunLearn(route),
            CitizenIntentRouter.Verb.Refactor => RunRefactor(route),
            CitizenIntentRouter.Verb.Elicit => RunElicit(route),
            CitizenIntentRouter.Verb.Calendar => RunCalendar(route),
            CitizenIntentRouter.Verb.Land => RunLand(route),
            CitizenIntentRouter.Verb.Pkg => RunPkg(route),
            CitizenIntentRouter.Verb.Project => RunProjSln(route),
            CitizenIntentRouter.Verb.Settings => RunSettings(route),
            CitizenIntentRouter.Verb.Restore => RunRestoreRecent(route),
            CitizenIntentRouter.Verb.Intercom => RunIntercom(route),
            CitizenIntentRouter.Verb.Presentation => RunPresentation(route),
            CitizenIntentRouter.Verb.Toolchain => RunToolchain(route),
            CitizenIntentRouter.Verb.CockpitHost => RunCockpitHost(route),
            CitizenIntentRouter.Verb.Qrh => RunQrh(route),
            CitizenIntentRouter.Verb.Webcam => RunWebcam(route),
            CitizenIntentRouter.Verb.Evidence => RunEvidence(route),
            CitizenIntentRouter.Verb.Domain => RunDomain(route),
            CitizenIntentRouter.Verb.Rules => RunRules(route),
            CitizenIntentRouter.Verb.Inventory => RunInventory(route),
            CitizenIntentRouter.Verb.VerifyWave => RunVerifyWave(route),
            CitizenIntentRouter.Verb.Arch => RunArch(route),
            CitizenIntentRouter.Verb.Crm => RunCrm(route),
            CitizenIntentRouter.Verb.MdAuthor => RunMdAuthor(route),
            CitizenIntentRouter.Verb.Scope => RunScope(route),
            CitizenIntentRouter.Verb.Glass => RunGlass(route),
            CitizenIntentRouter.Verb.Fdr => RunFdr(route),
            CitizenIntentRouter.Verb.Teeth => RunTeeth(route),
            CitizenIntentRouter.Verb.Postmortem => RunPostmortem(route),
            CitizenIntentRouter.Verb.Plugins => RunPlugins(route),
            CitizenIntentRouter.Verb.Problems => RunProblems(route),
            CitizenIntentRouter.Verb.Report => RunReport(route),
            CitizenIntentRouter.Verb.DebugSa => RunDebugSa(route),
            CitizenIntentRouter.Verb.TestSa => RunTestSa(route),
            CitizenIntentRouter.Verb.BuildSa => RunBuildSa(route),
            CitizenIntentRouter.Verb.Sys => RunSys(route),
            CitizenIntentRouter.Verb.Ecl => RunEcl(route),
            CitizenIntentRouter.Verb.Review => RunReview(route),
            CitizenIntentRouter.Verb.Alert => RunAlert(route),
            CitizenIntentRouter.Verb.Edit => RunEdit(route),
            CitizenIntentRouter.Verb.Deploy => RunDeploy(route),
            CitizenIntentRouter.Verb.Undo => RunUndo(route),
            CitizenIntentRouter.Verb.Clip => RunClip(route),
            CitizenIntentRouter.Verb.ReplaceAll => RunReplaceAll(route),
            CitizenIntentRouter.Verb.Nav => RunNav(route),
            CitizenIntentRouter.Verb.Put => RunPut(route),
            CitizenIntentRouter.Verb.Scratch => RunScratch(route),
            CitizenIntentRouter.Verb.Take => RunTake(route),
            CitizenIntentRouter.Verb.Share => RunShare(route),
            CitizenIntentRouter.Verb.Disk => RunDisk(route),
            CitizenIntentRouter.Verb.Sniper => RunSniper(route),
            CitizenIntentRouter.Verb.Buffer => RunBuffer(route),
            CitizenIntentRouter.Verb.FindBuf => RunFindBuf(route),
            CitizenIntentRouter.Verb.Cmd => RunPlanCmd(route),
            CitizenIntentRouter.Verb.Refuse => new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "refuse",
                Cmd: route.Cmd,
                Reason: route.Reason),
            _ => new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "skip",
                Reason: route.Reason ?? "unrecognized")
        };
    }


    static Applied PlaceGo(CitizenIntentRouter.Route route)
    {
        var go = route.Go;
        if (string.IsNullOrWhiteSpace(go))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "place",
                Reason: "go_empty");
        }

        try
        {
            var seat = IdeDeskSeats.PlaceOrgan(go);
            if (seat is null)
            {
                return new Applied(
                    route.Raw,
                    route.Verb.ToString(),
                    Ok: false,
                    Action: "place",
                    Go: go,
                    Reason: "place_failed");
            }

            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: true,
                Action: "place",
                Seat: seat,
                Go: IdeDeskSeats.CanonicalOrganPin(go));
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "place",
                Go: go,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Applied NotePaneFull(CitizenIntentRouter.Route route)
    {
        var seat = IdeDeskSeats.NormalizeSeatId(route.Organ);
        if (seat is null)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "pane_full",
                Reason: "pane_full_seat_invalid");
        }

        var placed = IdeDeskSeats.PlaceOrgan("cockpit");
        return new Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: true,
            Action: "pane_full",
            Seat: seat,
            Go: "cockpit",
            Reason: placed is null
                ? "seat_noted — cockpit pane_full=" + seat
                : "seat_noted + cockpit@" + placed + " — cockpit pane_full=" + seat);
    }

    static Applied OpenPath(CitizenIntentRouter.Route route)
    {
        var path = route.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "open",
                Reason: "open_path_empty");
        }

        var root = IdeCockpitHostChannel.ProjectRootResolver?.Invoke();
        if (!IdeLanguageTools.TryOpenDocument(path, root, out var full, out var docId, out var error))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "open",
                Path: path,
                Reason: error ?? "open_failed");
        }

        var seat = IdeDeskSeats.PlaceOrgan("editor_scene");
        PublishGlassLandOpen(full);
        return new Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: true,
            Action: "open",
            Seat: seat,
            Go: "editor_scene",
            Path: full,
            DocId: docId);
    }
}
