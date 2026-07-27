#nullable enable
namespace CdpMcp.Cockpit.ComputingUnits;

/// <summary>CCU: assemble desk loci cards from habitat snaps (ADR 0097 / surface loci).</summary>
public sealed class DeskLociBuildUnit : ICockpitComputeUnit
{
    public readonly record struct SessionFact(string? ProjectRoot, string? Language, object Detail);

    public readonly record struct SettingsFact(
        string Line, bool Ok, int UserCount, string UserPath, string ProcessPath);

    public readonly record struct GitFact(bool Available, bool Dirty, string Branch, object? Detail);

    public readonly record struct ShellTabFact(
        string Id, string State, int? LastExit, string? CwdShort, object Detail);

    public readonly record struct BrowserFact(
        bool Ok, string Line, string? ActiveTab, int TabCount,
        string? Url, string? Preview, string? LynxPath);

    public readonly record struct BufferDocFact(
        string DocId, string PathShort, bool Dirty, bool DiskChanged, object Detail);

    public readonly record struct ClipboardFact(
        int Count, string? CurrentId, int Chars, string? From, string Preview);

    public readonly record struct DebugFact(
        bool ActiveDap, bool Stopped, int BreakpointCount, object Detail);

    public readonly record struct TestFact(
        bool Available, string? Reason, bool? LastRun, bool Success,
        int Total, int Passed, int Failed, object Detail);

    public readonly record struct WorkFact(string? Pulse, object Detail);

    public readonly record struct QualityFact(
        bool Enabled, int Warn, int Fail, string Pulse, object Detail);

    public readonly record struct AnalysisFact(bool HasProject);

    public readonly record struct Input(
        SessionFact Session,
        SettingsFact Settings,
        GitFact Git,
        IReadOnlyList<ShellTabFact> ShellTabs,
        BrowserFact Browser,
        IReadOnlyList<BufferDocFact> Buffers,
        int BufferCount,
        ClipboardFact? Clipboard,
        DebugFact Debug,
        TestFact Test,
        WorkFact Work,
        QualityFact Quality,
        AnalysisFact Analysis);

    public List<FocusLocusUnit.LocusRef> Build(in Input input)
    {
        var list = new List<FocusLocusUnit.LocusRef>();
        var s = input.Session;
        list.Add(new FocusLocusUnit.LocusRef(
            "session:project",
            "session",
            s.ProjectRoot is null
                ? "no project — cdp_open"
                : $"{s.Language ?? "?"} @ {ShortPath(s.ProjectRoot)}",
            "cdp_open / cdp_session",
            "project_scene",
            s.Detail));

        var set = input.Settings;
        list.Add(new FocusLocusUnit.LocusRef(
            "settings:ide",
            "settings",
            set.Line,
            "go=options → page=internet|desk|shell|mcp",
            "settings",
            new
            {
                ok = set.Ok,
                user_count = set.UserCount,
                user_path = set.UserPath,
                process_path = set.ProcessPath
            }));

        var git = input.Git;
        if (git.Available)
        {
            list.Add(new FocusLocusUnit.LocusRef(
                "git:scm",
                "git",
                git.Dirty ? $"dirty on {git.Branch}" : $"clean {git.Branch}",
                "go=git_scene → go=git_draft",
                git.Dirty ? "git_draft" : "git_scene",
                git.Detail));
        }
        else
        {
            list.Add(new FocusLocusUnit.LocusRef(
                "git:scm",
                "git",
                "unavailable — cdp_open scm_root",
                "go=git_scene",
                "git_scene",
                new { available = false }));
        }

        foreach (var tab in input.ShellTabs.Take(12))
        {
            var pulse = $"{tab.State}" +
                        (tab.LastExit is { } ex ? $" exit={ex}" : "") +
                        (tab.CwdShort is { } cwd ? $" @ {cwd}" : "");
            list.Add(new FocusLocusUnit.LocusRef(
                $"shell:{tab.Id}",
                "shell",
                pulse,
                "go=shell_scene / go=shell_last",
                "shell_scene",
                tab.Detail));
        }

        var br = input.Browser;
        list.Add(new FocusLocusUnit.LocusRef(
            "browser:net",
            "browser",
            br.Line,
            "go=browser / go=search q=… / layout=code+net",
            "browser",
            new
            {
                ok = br.Ok,
                active_tab = br.ActiveTab,
                tab_count = br.TabCount,
                url = br.Url,
                preview = br.Preview,
                lynx = br.LynxPath
            }));

        foreach (var doc in input.Buffers.Take(16))
        {
            var both = doc.DiskChanged && doc.Dirty;
            var pulse =
                (both ? "DIRTY+DISK " : doc.DiskChanged ? "DISK CHANGED " : doc.Dirty ? "DIRTY " : "") +
                doc.PathShort;
            list.Add(new FocusLocusUnit.LocusRef(
                $"buffer:{doc.DocId}",
                "buffer",
                pulse,
                doc.DiskChanged
                    ? (both
                        ? "go=disk_peek → reload loses edits; or keep_disk"
                        : "go=disk_peek → reload | keep_disk — modified outside")
                    : "go=editor_scene → go=edit_draft",
                doc.DiskChanged ? "disk_peek" : "editor_scene",
                doc.Detail));
        }

        if (input.BufferCount == 0)
        {
            list.Add(new FocusLocusUnit.LocusRef(
                "buffer:none",
                "buffer",
                "no open buffers",
                "cdp_buffer op=open → go=editor_scene",
                "buffer_scene",
                new { count = 0 }));
        }

        if (input.Clipboard is { } clip)
        {
            list.Add(new FocusLocusUnit.LocusRef(
                "clip:session",
                "clipboard",
                $"clip ×{clip.Count} ({clip.CurrentId})",
                "go=clipboard → paste frame= | clip_clear",
                "clipboard",
                new
                {
                    count = clip.Count,
                    current = clip.CurrentId,
                    chars = clip.Chars,
                    from = clip.From,
                    preview = clip.Preview
                }));
        }

        var dbg = input.Debug;
        list.Add(new FocusLocusUnit.LocusRef(
            "debug:session",
            "debug",
            dbg.ActiveDap
                ? (dbg.Stopped ? "STOPPED" : "dap running") + $" bp={dbg.BreakpointCount}"
                : $"idle bp={dbg.BreakpointCount}",
            "go=debug_scene",
            "debug_scene",
            dbg.Detail));

        var test = input.Test;
        list.Add(new FocusLocusUnit.LocusRef(
            "test:last",
            "test",
            !test.Available
                ? test.Reason ?? "unavailable"
                : test.LastRun is null
                    ? "no last_run"
                    : $"{(test.Success ? "ok" : "FAIL")} {test.Passed}/{test.Total}",
            test.Failed > 0 ? "go=test_plan" : "go=test_scene",
            test.Failed > 0 ? "test_plan" : "test_scene",
            test.Detail));

        var analysis = input.Analysis;
        list.Add(new FocusLocusUnit.LocusRef(
            "analysis:scene",
            "analysis",
            analysis.HasProject ? "analysis ready" : "no project",
            "go=analysis_scene → correspondence|semantic_map|clones",
            "analysis_scene",
            new { features = new[] { "correspondence", "semantic_map", "clones" } }));

        var work = input.Work;
        list.Add(new FocusLocusUnit.LocusRef(
            "plan:focus",
            "plan",
            work.Pulse ?? "no plan — feature <name>",
            "go=plan / cmd=\"feature X\" | task Y | done",
            "plan",
            work.Detail));

        list.Add(new FocusLocusUnit.LocusRef(
            "mfd:ecl",
            "mfd",
            "ECL (electronic checklist)",
            "go=ecl",
            "ecl",
            new { switch_to = "ecl" }));

        var quality = input.Quality;
        if (quality.Enabled)
        {
            list.Add(new FocusLocusUnit.LocusRef(
                "mfd:gates",
                "mfd",
                quality.Fail > 0 || quality.Warn > 0
                    ? $"quality {quality.Pulse}"
                    : "quality gates ok",
                "go=quality — project-tunable",
                "quality",
                quality.Detail));
        }

        return list;
    }

    public static string ShortPath(string path)
    {
        try
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var parent = Path.GetFileName(Path.GetDirectoryName(path));
            if (string.IsNullOrEmpty(name))
                return path;
            return string.IsNullOrEmpty(parent) ? name : $"{parent}/{name}";
        }
        catch
        {
            return path;
        }
    }
}
