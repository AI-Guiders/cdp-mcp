using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeAlertChannelTests
{
    [Fact]
    public void Build_clear_when_no_gates_disk_or_dap()
    {
        var snap = IdeAlertChannel.Build(
            new QualityGates.QualitySnap(true, 0, 0, false, "ok"),
            diskChanged: 0,
            dapActive: false,
            dapStopped: false);
        Assert.Equal(IdeAlertChannel.Level.Clear, snap.Level);
        Assert.Contains("clear", snap.Pulse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_warn_pulse_uses_gate_file_count()
    {
        var snap = IdeAlertChannel.Build(
            new QualityGates.QualitySnap(true, Warn: 2, Fail: 0, SuggestSniper: false, Pulse: "WARN×2"),
            diskChanged: 0,
            dapActive: false,
            dapStopped: false);
        Assert.Equal(IdeAlertChannel.Level.Warn, snap.Level);
        Assert.Equal("sa WARN · gates×2", snap.Pulse);
    }

    [Fact]
    public void Build_fail_outranks_warn()
    {
        var snap = IdeAlertChannel.Build(
            new QualityGates.QualitySnap(true, Warn: 3, Fail: 1, SuggestSniper: false, Pulse: "FAIL×1"),
            diskChanged: 2,
            dapActive: false,
            dapStopped: false);
        Assert.Equal(IdeAlertChannel.Level.Fail, snap.Level);
        Assert.Contains("FAIL", snap.Pulse, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_problems_errors_are_fail()
    {
        var snap = IdeAlertChannel.Build(new IdeAlertChannel.Inputs(
            new QualityGates.QualitySnap(true, 0, 0, false, "ok"),
            DiskChanged: 0,
            DapActive: false,
            DapStopped: false,
            ProblemErrors: 2,
            ProblemWarnings: 1));
        Assert.Equal(IdeAlertChannel.Level.Fail, snap.Level);
        Assert.Equal("sa FAIL · pe×2", snap.Pulse);
    }

    [Fact]
    public void Build_problems_errors_exposes_explain_card()
    {
        var snap = IdeAlertChannel.Build(new IdeAlertChannel.Inputs(
            new QualityGates.QualitySnap(true, 0, 0, false, "ok"),
            DiskChanged: 0,
            DapActive: false,
            DapStopped: false,
            ProblemErrors: 2,
            ProblemWarnings: 1));
        Assert.NotNull(snap.Explain);
        Assert.Equal("alert.problems", snap.Explain!.Source);
        Assert.Equal("problem_errors", snap.Explain.Reason);
        Assert.Equal("go=problems", snap.Explain.NextStep);
    }

    [Fact]
    public void Build_git_dirty_and_shell_running_fuse()
    {
        var snap = IdeAlertChannel.Build(new IdeAlertChannel.Inputs(
            new QualityGates.QualitySnap(true, 0, 0, false, "ok"),
            DiskChanged: 0,
            DapActive: false,
            DapStopped: false,
            ShellRunning: 1,
            GitDirty: true,
            Sit: new IdeAlertChannel.Sit("act/code", "change", "IdeAlertChannel.cs", null, null)));
        Assert.Equal(IdeAlertChannel.Level.Warn, snap.Level);
        Assert.Equal("sa WARN · git dirty", snap.Pulse);
        Assert.Contains(snap.Lines, l => l.Contains("shell run", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_plateau_ecl_open_without_other_beeps_is_warn()
    {
        var snap = IdeAlertChannel.Build(new IdeAlertChannel.Inputs(
            new QualityGates.QualitySnap(true, 0, 0, false, "ok"),
            0, false, false,
            ChkOpenRequired: 1,
            ChkPulse: "ecl · plateau 4/5 (open×1)"));
        Assert.Equal(IdeAlertChannel.Level.Warn, snap.Level);
        Assert.Contains("plateau", snap.Pulse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_soft_phase_mismatch_alone_stays_clear()
    {
        var snap = IdeAlertChannel.Build(new IdeAlertChannel.Inputs(
            new QualityGates.QualitySnap(true, 0, 0, false, "ok"),
            0, false, false,
            StagePhaseMismatch: "phase mismatch task@act · session=explore",
            Sit: new IdeAlertChannel.Sit("explore/code", null, null, null, null)));
        Assert.Equal(IdeAlertChannel.Level.Clear, snap.Level);
        Assert.DoesNotContain("WARN", snap.Pulse, StringComparison.Ordinal);
    }

    [Fact]
    public void SuggestLayout_flags_stale_plugins_on_P()
    {
        var seats = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["p"] = "plugins",
            ["forward"] = "editor_scene",
            ["m"] = "browser",
        };
        var (hint, note) = IdeAlertChannel.SuggestLayout(CdpPhase.Act, CdpObjectKind.Code, seats);
        Assert.Equal("agent", hint);
        Assert.Contains("plugins", note, StringComparison.OrdinalIgnoreCase);

        var (exploreHint, _) = IdeAlertChannel.SuggestLayout(CdpPhase.Explore, CdpObjectKind.Code, seats);
        Assert.Equal("code+net", exploreHint);
    }

    [Fact]
    public void PulseCard_omits_zero_counts()
    {
        var snap = IdeAlertChannel.Build(new IdeAlertChannel.Inputs(
            new QualityGates.QualitySnap(true, 0, 0, false, "ok"),
            0, false, false,
            Sit: new IdeAlertChannel.Sit("explore/code", "find", "a.cs", "code+net", null)));
        var card = IdeAlertChannel.PulseCard(snap);
        var json = System.Text.Json.JsonSerializer.Serialize(card);
        Assert.Contains("explore/code", json, StringComparison.Ordinal);
        Assert.Contains("a.cs", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"pe\"", json, StringComparison.Ordinal);
    }
}
