#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.ComputingUnits;
using DotNetBuildTest.Core;
using DotnetDebug.Core;
using DotnetDebugMcp;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>BuildAsync loci/next/focus/goVerbs peel.</summary>
internal static partial class IdeCockpit
{
    static (List<Locus> Loci, object[] Next, object? Focus, string[] GoVerbs) BuildDeskNavigation(
        SessionContext session,
        JsonElement? git,
        ShellSnap shell,
        InternetBrowserHabitat.BrowserPulse browser,
        IdeSettingsHabitat.SettingsPulse settingsPulse,
        BufferSnap buffer,
        DebugSnap debug,
        TestSnap test,
        WorkSnap work,
        QualityGates.QualitySnap quality,
        string? focusId,
        IdeAlertChannel.Snap alertSnap,
        IdeChkChannel.Snap chkSnap,
        IdeChkChannel.ProbeCtx chkCtx)
    {
        var loci = BuildLoci(session, git, shell, browser, settingsPulse, buffer, debug, test, work, quality);
        var next = BuildNext(
            session, git, shell, buffer, debug, test, work, focusId, quality, alertSnap, chkSnap, chkCtx);

        if (DeskSniperLocus.TryBuild(new DeskSniperLocusUnit.Input(
                EditSniper.HasHold, EditSniper.PulseLine, EditSniper.HoldCard())) is { } sniper)
        {
            loci.Insert(Math.Min(1, loci.Count), new Locus(
                sniper.Id, sniper.Kind, sniper.Pulse, sniper.Drill, sniper.Go, sniper.Detail));
        }

        object? focus = FocusLocus.Build(
            focusId,
            loci.Select(l => new FocusLocusUnit.LocusRef(l.Id, l.Kind, l.Pulse, l.Drill, l.Go, l.Detail)).ToArray());

        return (loci, next, focus, GoVerbsCatalog.Merge(GoMap.Keys));
    }
}
