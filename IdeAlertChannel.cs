#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=alert|sa|eicas</c> — fused Situation Awareness / EICAS-lite (ADR 0193).
/// Severity = Clear/Warn/Fail (sit beeps). Sit/locus/layout = attention zones, not severity.
/// Partials: Models (DTO), Build (snap fuse), Layout (desk hint).
/// </summary>
internal static partial class IdeAlertChannel
{
    public static object Handle(
        QualityGates.QualitySnap quality,
        int diskChanged,
        bool dapActive,
        bool dapStopped,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        Handle(new Inputs(quality, diskChanged, dapActive, dapStopped), args);

    public static object Handle(Inputs inputs, IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        _ = args;
        var snap = Build(inputs);
        return new
        {
            ok = snap.Ok,
            schema = SchemaVersion,
            role = "alert",
            go = "alert",
            detail = "pulse",
            level = snap.Level.ToString().ToLowerInvariant(),
            pulse = snap.Pulse,
            view = new { schema = SchemaVersion, lines = snap.Lines },
            explain = snap.Explain is null ? null : new
            {
                source = snap.Explain.Source,
                reason = snap.Explain.Reason,
                authority = snap.Explain.Authority,
                next_step = snap.Explain.NextStep,
                why = snap.Explain.WhyLine
            },
            sit = SitCard(snap.Sit),
            quality = new { fail = snap.QualityFail, warn = snap.QualityWarn, pulse = inputs.Quality.Pulse },
            problems = new { errors = snap.ProblemErrors, warnings = snap.ProblemWarnings },
            shell = new { running = snap.ShellRunning, failed = snap.ShellFailed },
            disk_changed = snap.DiskChanged,
            git_dirty = snap.GitDirty,
            dap = new { active = snap.DapActive, stopped = snap.DapStopped },
            hint = snap.Level == Level.Clear
                ? "SA clear (root EICAS — does not steal a seat). Drill: go=problems|quality|disk_peek|debug. layout= when seat beeps."
                : "Sit beep (root EICAS — seat map unchanged). Drill: go=problems|quality|disk_peek|debug|shell_scene."
        };
    }

    public static object PulseCard(Snap snap)
    {
        var counts = new Dictionary<string, object>(StringComparer.Ordinal);
        if (snap.ProblemErrors > 0) counts["pe"] = snap.ProblemErrors;
        if (snap.ProblemWarnings > 0) counts["pw"] = snap.ProblemWarnings;
        if (snap.QualityFail > 0) counts["qf"] = snap.QualityFail;
        if (snap.QualityWarn > 0) counts["qw"] = snap.QualityWarn;
        if (snap.DiskChanged > 0) counts["disk"] = snap.DiskChanged;
        if (snap.ShellRunning > 0) counts["sh_run"] = snap.ShellRunning;
        if (snap.ShellFailed > 0) counts["sh_fail"] = snap.ShellFailed;
        if (snap.GitDirty) counts["git_dirty"] = true;
        if (snap.DapStopped) counts["dap_stopped"] = true;

        return new
        {
            schema = SchemaVersion,
            level = snap.Level.ToString().ToLowerInvariant(),
            ok = snap.Ok,
            pulse = snap.Pulse,
            sit = snap.Sit?.PhaseObject,
            intent = snap.Sit?.Intent,
            locus = snap.Sit?.Locus,
            layout = snap.Sit?.LayoutHint,
            seat = snap.Sit?.SeatNote,
            explain = snap.Explain is null ? null : new
            {
                source = snap.Explain.Source,
                reason = snap.Explain.Reason,
                authority = snap.Explain.Authority,
                next_step = snap.Explain.NextStep,
                why = snap.Explain.WhyLine
            },
            counts = counts.Count == 0 ? null : counts
        };
    }

    static object? SitCard(Sit? sit) =>
        sit is null
            ? null
            : new
            {
                phase_object = sit.PhaseObject,
                intent = sit.Intent,
                locus = sit.Locus,
                layout = sit.LayoutHint,
                seat = sit.SeatNote
            };

}
