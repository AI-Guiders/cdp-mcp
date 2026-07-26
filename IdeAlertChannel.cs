#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=alert|sa|eicas</c> — fused Situation Awareness / EICAS-lite (ADR 0193).
/// Severity = Clear/Warn/Fail (sit beeps). Sit/locus/layout = attention zones, not severity.
/// Context-dump cost stays a separate W/C/A axis (<c>man tool=context_budget</c>).
/// </summary>
internal static class IdeAlertChannel
{
    public const string SchemaVersion = "alert_channel/v1.1";

    public enum Level
    {
        Clear = 0,
        Warn = 1,
        Fail = 2
    }

    /// <summary>Attention zones — phase/intent/locus/layout — not EICAS severity.</summary>
    public sealed record Sit(
        string PhaseObject,
        string? Intent,
        string? Locus,
        string? LayoutHint,
        string? SeatNote);

    public sealed record Inputs(
        QualityGates.QualitySnap Quality,
        int DiskChanged,
        bool DapActive,
        bool DapStopped,
        int ProblemErrors = 0,
        int ProblemWarnings = 0,
        int ShellRunning = 0,
        int ShellFailed = 0,
        bool GitDirty = false,
        Sit? Sit = null,
        string? StagePhaseMismatch = null);

    public sealed record Snap(
        Level Level,
        bool Ok,
        string Pulse,
        string[] Lines,
        int QualityFail,
        int QualityWarn,
        int DiskChanged,
        bool DapStopped,
        bool DapActive,
        int ProblemErrors,
        int ProblemWarnings,
        int ShellRunning,
        int ShellFailed,
        bool GitDirty,
        Sit? Sit);

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
        return "sa WARN";
    }

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

    /// <summary>
    /// Phase-aware desk layout hint — suggest, never auto-mutate sticky seats.
    /// </summary>
            public static (string? LayoutHint, string? SeatNote) SuggestLayout(
        CdpPhase phase,
        CdpObjectKind obj,
        IReadOnlyDictionary<string, string?> seats)
    {
        var p = Seat(seats, "p");
        var m = Seat(seats, "m");
        var codeish = obj is CdpObjectKind.Code or CdpObjectKind.Repo or CdpObjectKind.Issue;

        // Sticky plugins after dogfood while doing code work — common SA trap.
        if (IsPlugins(p) && codeish)
        {
            return phase is CdpPhase.Explore
                ? ("code+net", "P=plugins stale — layout=code+net")
                : ("agent", "P=plugins stale — layout=agent");
        }

        if ((phase is CdpPhase.Verify or CdpPhase.Act) && codeish)
        {
            if (m is not null && IsBrowser(m))
                return ("code+shell", "M=browser — layout=code+shell for act/verify");
            if (m is not null && IsPlan(m))
                return ("code+shell", $"M={m} — layout=code+shell for act/verify");
        }

        if (phase is CdpPhase.Explore && obj is CdpObjectKind.Code
            && m is not null && IsPlan(m))
            return ("code+net", "M=plan — layout=code+net for explore");

        if (phase is CdpPhase.Plan && p is not null && !IsPlan(p))
            return ("agent", $"P={p} — layout=agent (plan|editor|script)");

        return (null, null);
    }



    static string? Seat(IReadOnlyDictionary<string, string?> seats, string id) =>
        seats.TryGetValue(id, out var v) && v is { Length: > 0 } ? v : null;

    static bool IsPlugins(string? pin) =>
        pin is "plugins" or "plugin" or "vsix";

    static bool IsPlan(string? pin) =>
        pin is "plan" or "work" or "tasks" or "tm" or "feature" or "task";

    static bool IsBrowser(string? pin) =>
        pin is "browser" or "scene_internet_browser" or "internet_browser";

    static bool IsShellOrGitOrTest(string? pin) =>
        pin is "shell_scene" or "shell" or "git_scene" or "git" or "test_scene" or "test" or "chk";
}
