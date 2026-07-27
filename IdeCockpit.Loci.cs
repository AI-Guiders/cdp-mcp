#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp;

/// <summary>Desk loci peel — adapts habitat snaps → DeskLociBuildUnit.</summary>
internal static partial class IdeCockpit
{
    static readonly DeskLociBuildUnit DeskLoci = new();

    static List<Locus> BuildLoci(
        SessionContext session,
        JsonElement? gitRoot,
        ShellSnap shell,
        InternetBrowserHabitat.BrowserPulse browser,
        IdeSettingsHabitat.SettingsPulse settings,
        BufferSnap buffer,
        DebugSnap debug,
        TestSnap test,
        WorkSnap work,
        QualityGates.QualitySnap quality)
    {
        DeskLociBuildUnit.GitFact gitFact;
        if (gitRoot is { } g)
        {
            gitFact = new DeskLociBuildUnit.GitFact(
                Available: true,
                Dirty: GitIsDirty(g),
                Branch: FirstGitBranch(g) ?? "?",
                Detail: CompactGit(g));
        }
        else
        {
            gitFact = new DeskLociBuildUnit.GitFact(false, false, "?", null);
        }

        DeskLociBuildUnit.ClipboardFact? clipFact = null;
        if (EditorComfort.ClipboardLocusDetail() is { } clip)
        {
            clipFact = new DeskLociBuildUnit.ClipboardFact(
                clip.Count, clip.CurrentId, clip.Chars, clip.From, clip.Preview);
        }

        var input = new DeskLociBuildUnit.Input(
            Session: new DeskLociBuildUnit.SessionFact(
                session.ProjectRoot, session.Language, SessionPulse(session)),
            Settings: new DeskLociBuildUnit.SettingsFact(
                settings.Line, settings.Ok, settings.UserCount, settings.UserPath, settings.ProcessPath),
            Git: gitFact,
            ShellTabs: shell.Tabs
                .Select(t => new DeskLociBuildUnit.ShellTabFact(
                    t.Id,
                    t.State,
                    t.LastExit,
                    t.Cwd is { } cwd ? DeskLociBuildUnit.ShortPath(cwd) : null,
                    t))
                .ToArray(),
            Browser: new DeskLociBuildUnit.BrowserFact(
                browser.Ok, browser.Line, browser.ActiveTab, browser.TabCount,
                browser.Url, browser.Preview, browser.LynxPath),
            Buffers: buffer.Docs
                .Select(d => new DeskLociBuildUnit.BufferDocFact(
                    d.DocId, DeskLociBuildUnit.ShortPath(d.Path), d.Dirty, d.DiskChanged, d))
                .ToArray(),
            BufferCount: buffer.Count,
            Clipboard: clipFact,
            Debug: new DeskLociBuildUnit.DebugFact(
                debug.ActiveDap, debug.Stopped, debug.BreakpointCount, debug),
            Test: new DeskLociBuildUnit.TestFact(
                test.Available, test.Reason, test.LastRun, test.Success,
                test.Total, test.Passed, test.Failed, test),
            Work: new DeskLociBuildUnit.WorkFact(work.Pulse, work),
            Quality: new DeskLociBuildUnit.QualityFact(
                quality.Enabled, quality.Warn, quality.Fail, quality.Pulse, quality),
            Analysis: new DeskLociBuildUnit.AnalysisFact(
                session.ProjectRoot is { Length: > 0 }));

        return DeskLoci.Build(input)
            .Select(r => new Locus(r.Id, r.Kind, r.Pulse, r.Drill, r.Go, r.Detail))
            .ToList();
    }
}
