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

        void Raise(Level want)
        {
            if (level < want) level = want;
        }

        string Mark() => level == Level.Fail ? "|" : "*";

        if (i.Quality is { Enabled: true, Fail: > 0 })
        {
            Raise(Level.Fail);
            lines.Add($"!gates FAIL×{i.Quality.Fail} WARN×{i.Quality.Warn}");
        }
        else if (i.Quality is { Enabled: true, Warn: > 0 })
        {
            Raise(Level.Warn);
            lines.Add($"*gates WARN×{i.Quality.Warn}");
        }

        if (i.ProblemErrors > 0)
        {
            Raise(Level.Fail);
            lines.Add($"{Mark()}problems E×{i.ProblemErrors} W×{i.ProblemWarnings}");
        }
        else if (i.ProblemWarnings > 0)
        {
            Raise(Level.Warn);
            lines.Add($"*problems W×{i.ProblemWarnings}");
        }

        if (i.ShellFailed > 0)
        {
            Raise(Level.Fail);
            lines.Add($"{Mark()}shell FAIL×{i.ShellFailed} run×{i.ShellRunning}");
        }
        else if (i.ShellRunning > 0)
        {
            lines.Add($"·shell run×{i.ShellRunning}");
        }

        if (i.DiskChanged > 0)
        {
            Raise(Level.Warn);
            lines.Add($"{Mark()}disk×{i.DiskChanged} outside IDE");
        }

        if (i.DapStopped)
        {
            Raise(Level.Warn);
            lines.Add($"{Mark()}dap STOPPED");
        }
        else if (i.DapActive)
        {
            lines.Add("·dap active");
        }

        if (i.GitDirty)
        {
            Raise(Level.Warn);
            lines.Add($"{Mark()}git dirty");
        }

        if (i.StagePhaseMismatch is { Length: > 0 } mismatch)
        {
            Raise(Level.Warn);
            lines.Add($"{Mark()}{mismatch}");
        }

        if (i.ChkOpenRequired > 0)
        {
            Raise(Level.Warn);
            var chkLine = i.ChkPulse is { Length: > 0 } p
                ? p
                : $"ecl open×{i.ChkOpenRequired}";
            lines.Add($"{Mark()}{chkLine}");
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
