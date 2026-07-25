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
        Assert.Equal("alert WARN · gates×2", snap.Pulse);
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
}
