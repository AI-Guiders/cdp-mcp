#nullable enable

namespace CdpMcp;

internal static partial class IdeAlertChannel
{
    static string BuildPulse(Level level, Inputs i)
    {
        var head = level switch
        {
            Level.Fail => FirstFailHead(i),
            Level.Warn => FirstWarnHead(i),
            _ => "sa · clear"
        };

        if (i.Sit?.PhaseObject is { Length: > 0 } po && level == Level.Clear)
            return $"{head} · {po}";
        return head;
    }

    static string FirstFailHead(Inputs i)
    {
        if (i.Quality is { Enabled: true, Fail: > 0 })
            return $"sa FAIL · gates×{i.Quality.Fail}";
        if (i.ProblemErrors > 0)
            return $"sa FAIL · pe×{i.ProblemErrors}";
        if (i.ShellFailed > 0)
            return $"sa FAIL · shell×{i.ShellFailed}";
        return "sa FAIL";
    }

    static string FirstWarnHead(Inputs i)
    {
        if (i.Quality is { Enabled: true, Warn: > 0 })
            return $"sa WARN · gates×{i.Quality.Warn}";
        if (i.ProblemWarnings > 0)
            return $"sa WARN · pw×{i.ProblemWarnings}";
        if (i.DiskChanged > 0)
            return $"sa WARN · disk×{i.DiskChanged}";
        if (i.DapStopped)
            return "sa WARN · dap stopped";
        if (i.GitDirty)
            return "sa WARN · git dirty";
        if (i.StagePhaseMismatch is { Length: > 0 })
            return "sa WARN · phase mismatch";
        if (i.ChkOpenRequired > 0)
            return i.ChkPulse is { Length: > 0 } p ? $"sa WARN · {p}" : $"sa WARN · ecl×{i.ChkOpenRequired}";
        return "sa WARN";
    }
}
