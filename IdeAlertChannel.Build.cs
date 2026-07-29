#nullable enable

namespace CdpMcp;

internal static partial class IdeAlertChannel
{
    public static Snap Build(
        QualityGates.QualitySnap quality,
        int diskChanged,
        bool dapActive,
        bool dapStopped) =>
        Build(new Inputs(quality, diskChanged, dapActive, dapStopped));

    public static Snap Build(Inputs i)
    {
        var lines = new List<string>();
        var level = Level.Clear;
        IdeExplainability.ExplainCard? explain = null;

        void Raise(Level want)
        {
            if (level < want) level = want;
        }

        void Explain(string source, string reason, string authority, string nextStep)
        {
            explain ??= IdeExplainability.New(source, reason, authority, nextStep);
        }

        string Mark() => level == Level.Fail ? "|" : "*";

        if (i.Quality is { Enabled: true, Fail: > 0 })
        {
            Raise(Level.Fail);
            lines.Add($"!gates FAIL×{i.Quality.Fail} WARN×{i.Quality.Warn}");
            Explain("alert.gates", "quality_fail", $"quality gates report FAIL×{i.Quality.Fail}", "go=quality");
        }
        else if (i.Quality is { Enabled: true, Warn: > 0 })
        {
            // Quiet-band (solo Dark Cockpit): quality WARN is advisory until go=quality/gates/alert.
            // FAIL still screams. Matches StagePhaseMismatch soft-affinity pattern.
            if (i.QuietBandQuality)
            {
                lines.Add($"·gates WARN×{i.Quality.Warn}");
            }
            else
            {
                Raise(Level.Warn);
                lines.Add($"*gates WARN×{i.Quality.Warn}");
                Explain("alert.gates", "quality_warn", $"quality gates report WARN×{i.Quality.Warn}", "go=quality");
            }
        }

        if (i.ProblemErrors > 0)
        {
            Raise(Level.Fail);
            lines.Add($"{Mark()}problems E×{i.ProblemErrors} W×{i.ProblemWarnings}");
            Explain("alert.problems", "problem_errors", $"problems has E×{i.ProblemErrors}", "go=problems");
        }
        else if (i.ProblemWarnings > 0)
        {
            Raise(Level.Warn);
            lines.Add($"*problems W×{i.ProblemWarnings}");
            Explain("alert.problems", "problem_warnings", $"problems has W×{i.ProblemWarnings}", "go=problems");
        }

        if (i.ShellFailed > 0)
        {
            Raise(Level.Fail);
            lines.Add($"{Mark()}shell FAIL×{i.ShellFailed} run×{i.ShellRunning}");
            Explain("alert.shell", "shell_failed", $"shell has FAIL×{i.ShellFailed}", "go=shell_scene");
        }
        else if (i.ShellRunning > 0)
        {
            lines.Add($"·shell run×{i.ShellRunning}");
        }

        if (i.DiskChanged > 0)
        {
            Raise(Level.Warn);
            lines.Add($"{Mark()}disk×{i.DiskChanged} outside IDE");
            Explain("alert.disk", "outside_ide_mutation", $"disk changed outside IDE ×{i.DiskChanged}", "go=disk_peek");
        }

        if (i.DapStopped)
        {
            Raise(Level.Warn);
            lines.Add($"{Mark()}dap STOPPED");
            Explain("alert.debug", "dap_stopped", "debugger is stopped and waiting", "go=debug");
        }
        else if (i.DapActive)
        {
            lines.Add("·dap active");
        }

        if (i.GitDirty)
        {
            Raise(Level.Warn);
            lines.Add($"{Mark()}git dirty");
            Explain("alert.git", "git_dirty", "working tree has staged or unstaged changes", "go=git_scene");
        }

        // Soft @phase affinity ≠ session phase: advisory only (Agent Dark Cockpit).
        // Do not WARN / n-alert — catalog is not broken by affinity drift alone.
        if (i.StagePhaseMismatch is { Length: > 0 } mismatch)
            lines.Add($"·{mismatch}");

        if (i.ChkOpenRequired > 0)
        {
            Raise(Level.Warn);
            var chkLine = i.ChkPulse is { Length: > 0 } p
                ? p
                : $"ecl open×{i.ChkOpenRequired}";
            lines.Add($"{Mark()}{chkLine}");
            Explain("alert.ecl", "checklist_open", chkLine, "go=ecl");
        }

        if (i.Sit?.SeatNote is { Length: > 0 } note)
            lines.Add($"·{note}");

        if (lines.Count == 0)
            lines.Add("(clear — no sit beeps)");

        var pulse = BuildPulse(level, i);

        return new Snap(
            level,
            Ok: level != Level.Fail,
            pulse,
            lines.Take(16).ToArray(),
            explain,
            i.Quality.Fail,
            i.Quality.Warn,
            i.DiskChanged,
            i.DapStopped,
            i.DapActive,
            i.ProblemErrors,
            i.ProblemWarnings,
            i.ShellRunning,
            i.ShellFailed,
            i.GitDirty,
            i.Sit);
    }
}
