using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeChkChannelTests
{
    static IdeChkChannel.ProbeCtx Ctx(
        string phase = "act",
        string? intent = null,
        bool projectOpen = true,
        bool taskOpen = true,
        bool igniteIdle = true,
        bool gitKnown = true,
        bool gitDirty = false,
        bool dapStopped = false) =>
        new(
            projectOpen,
            taskOpen,
            igniteIdle,
            gitKnown,
            gitDirty,
            TestsGreen: false,
            TestsFailed: false,
            ProblemsClean: true,
            dapStopped,
            DapActive: dapStopped,
            SniperOk: true,
            phase,
            intent);

    [Fact]
    public void MatchLink_phase_and_compound_state()
    {
        var ctx = Ctx(phase: "handoff", gitDirty: true);
        Assert.True(IdeChkChannel.MatchLink("phase:handoff", ctx));
        Assert.True(IdeChkChannel.MatchLink("phase:handoff+state:git.dirty", ctx));
        Assert.False(IdeChkChannel.MatchLink("phase:handoff+state:git.clean", ctx));
        Assert.True(IdeChkChannel.MatchLink("always", ctx));
    }

    [Fact]
    public void Ship_active_on_handoff_with_open_required_when_dirty()
    {
        var snap = IdeChkChannel.Build(Ctx(phase: "handoff", gitDirty: true));
        Assert.Contains(snap.Active, r => r.Id == "ship");
        Assert.True(snap.OpenRequired > 0);
        Assert.Contains("ship", snap.Pulse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ship_commits_auto_done_when_clean()
    {
        var snap = IdeChkChannel.Build(Ctx(phase: "handoff", gitDirty: false));
        var ship = Assert.Single(snap.Active, r => r.Id == "ship");
        var commits = Assert.Single(ship.Items, i => i.Id == "commits");
        Assert.True(commits.Done);
        Assert.Equal(0, snap.OpenRequired);
        // standing allow — push clears without per-run ack
        var push = Assert.Single(ship.Items, i => i.Id == "push");
        Assert.Equal("allow", push.Kind);
        Assert.True(push.Done);
    }

    [Fact]
    public void DapHold_activates_on_state()
    {
        var snap = IdeChkChannel.Build(Ctx(phase: "explore", dapStopped: true));
        Assert.Contains(snap.Active, r => r.Id == "dap-hold");
    }

    [Fact]
    public void Intent_ship_activates_ship_checklist()
    {
        var snap = IdeChkChannel.Build(Ctx(phase: "act", intent: "ship"));
        Assert.Contains(snap.Active, r => r.Id == "ship");
    }

    [Fact]
    public void Plateau_active_when_act_has_no_task_focus()
    {
        var snap = IdeChkChannel.Build(Ctx(phase: "act", taskOpen: false));
        Assert.Contains(snap.Active, r => r.Id == "plateau");
        // OpenRequired=0 when ignite idle → pulse is "ecl · N clear" (Agent Dark Cockpit).
        Assert.Equal(0, snap.OpenRequired);
    }

    [Fact]
    public void Plateau_open_required_clear_when_ignite_idle()
    {
        var snap = IdeChkChannel.Build(Ctx(phase: "act", taskOpen: false, igniteIdle: true));
        Assert.Contains(snap.Active, r => r.Id == "plateau");
        Assert.Equal(0, snap.OpenRequired);
        Assert.Contains("clear", snap.Pulse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plateau_open_required_when_ignite_armed()
    {
        var snap = IdeChkChannel.Build(Ctx(phase: "act", taskOpen: false, igniteIdle: false));
        Assert.Contains(snap.Active, r => r.Id == "plateau");
        Assert.True(snap.OpenRequired > 0);
    }

    [Fact]
    public void Plateau_ignite_park_auto_clears_when_idle()
    {
        var snap = IdeChkChannel.Build(Ctx(phase: "act", taskOpen: false, igniteIdle: true));
        var plateau = Assert.Single(snap.Active, r => r.Id == "plateau");
        var parked = Assert.Single(plateau.Items, i => i.Id == "ignite-park");
        Assert.True(parked.Done);
    }

    [Fact]
    public void Customize_link_add_and_remove_roundtrip()
    {
        IdeSettingsStore.Unset(IdeChkChannel.OverlayKey);
        try
        {
            var add = IdeChkChannel.Handle(
                Ctx(phase: "plan"),
                new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["op"] = System.Text.Json.JsonSerializer.SerializeToElement("add"),
                    ["id"] = System.Text.Json.JsonSerializer.SerializeToElement("mine"),
                    ["title"] = System.Text.Json.JsonSerializer.SerializeToElement("Mine"),
                    ["link"] = System.Text.Json.JsonSerializer.SerializeToElement("phase:plan")
                });
            Assert.NotNull(add);

            var snap = IdeChkChannel.Build(Ctx(phase: "plan"));
            Assert.Contains(snap.Active, r => r.Id == "mine");

            IdeChkChannel.Handle(
                Ctx(phase: "plan"),
                new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["op"] = System.Text.Json.JsonSerializer.SerializeToElement("link"),
                    ["id"] = System.Text.Json.JsonSerializer.SerializeToElement("ship"),
                    ["link"] = System.Text.Json.JsonSerializer.SerializeToElement("phase:plan")
                });
            var linked = IdeChkChannel.Build(Ctx(phase: "plan"));
            Assert.Contains(linked.Active, r => r.Id == "ship");

            IdeChkChannel.Handle(
                Ctx(phase: "plan"),
                new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["op"] = System.Text.Json.JsonSerializer.SerializeToElement("remove"),
                    ["id"] = System.Text.Json.JsonSerializer.SerializeToElement("mine")
                });
        }
        finally
        {
            IdeSettingsStore.Unset(IdeChkChannel.OverlayKey);
            IdeSettingsStore.Unset(IdeChkChannel.AcksKey);
        }
    }
}
