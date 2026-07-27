#nullable enable
using CdpMcp.Cockpit.Composition;
using CdpMcp.Cockpit.ComputingUnits;
using CdpMcp.Cockpit.DataBus;
using CdpMcp.Cockpit.Ids;
using CdpMcp.Cockpit.Instrument;
using CdpMcp.Cockpit.Surface;
using CdpMcp.Cockpit.Transport;
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class DeskWireParityTests
{
    [Fact]
    public void DeskIngestionBus_publishes_on_channel()
    {
        using var bus = new DeskIngestionBus(8);
        Assert.True(bus.TryPublish(new DeskIngressEvent("t", "cmd", "sa", DateTimeOffset.UtcNow)));
        Assert.Equal(1, bus.Published);
        Assert.True(bus.Reader.TryRead(out var evt));
        Assert.Equal("t", evt.Source);
    }

    [Fact]
    public void AttentionRoutingUnit_forces_nav_desk_detail()
    {
        var unit = new AttentionRoutingUnit();
        var snap = unit.Compute(new AttentionRoutingUnit.Input("sys", "nav", true, null));
        Assert.Equal("nav", snap.Mfd);
        Assert.Null(snap.GoVerb);
        Assert.True(snap.DeskDetailNavForced);
    }

    [Fact]
    public void DeskInstrumentMountRegistry_syncs_seats()
    {
        var reg = new DeskInstrumentMountRegistry();
        reg.SyncFromSeats(new Dictionary<string, string?> { ["p"] = "plan", ["forward"] = "editor_scene", ["m"] = null },
            ["p", "forward", "m"]);
        var deck = reg.Describe();
        Assert.Equal(2, deck.OrderedInstrumentIds.Count);
        Assert.Contains("p:plan", deck.OrderedInstrumentIds);
    }

    [Fact]
    public void DeskDataBusHost_publishes_surface_built()
    {
        DeskSurfaceBuiltEvent? got = null;
        using var sub = DeskDataBusHost.Current.Subscribe<DeskSurfaceBuiltEvent>(e => got = e);
        DeskDataBusHost.Current.Publish(new DeskSurfaceBuiltEvent("seats", 3, null, DateTimeOffset.UtcNow));
        Assert.NotNull(got);
        Assert.Equal("seats", got!.Value.Mode);
        Assert.Equal(3, got.Value.SeatCount);
    }

    [Fact]
    public void DeskDetailUnit_focus_forces_nav()
    {
        var unit = new DeskDetailUnit();
        var snap = unit.Compute(new DeskDetailUnit.Input("slim", "buf"));
        Assert.Equal("nav", snap.DeskDetail);
        Assert.True(snap.WantNav);
    }

    [Fact]
    public void SeatsSurfaceCompositor_publishes_and_sets_desk_detail()
    {
        DeskSurfaceBuiltEvent? got = null;
        using var sub = DeskDataBusHost.Current.Subscribe<DeskSurfaceBuiltEvent>(e => got = e);
        var comp = new SeatsSurfaceCompositor();
        var decision = new DeskDetailUnit.Snapshot("nav", true);
        var dict = comp.Compose(
            new SeatsSurfaceScene(
                "cockpit/v1.20", "sa", new { }, new { }, new { },
                null, null, null, new { }, null,
                null, null, ["p"], ["agent"], null,
                Array.Empty<object>(), ["sa"]),
            new SeatsSurfacePayload(2),
            decision);
        Assert.Equal("nav", dict["desk_detail"]);
        Assert.NotNull(dict["loci"]);
        Assert.NotNull(got);
        Assert.Equal(2, got!.Value.SeatCount);
    }

    [Fact]
    public void FeatureSearchUnit_ranks_exact_prefix_contains()
    {
        var unit = new FeatureSearchUnit();
        var catalog = new (string Go, string Tool)[]
        {
            ("sa", "cockpit"),
            ("sys", "soft"),
            ("system", "soft"),
            ("plan", "tasks")
        };
        var hits = unit.Search("sys", 10, catalog);
        Assert.Equal(2, hits.Length);
        Assert.Equal("sys", hits[0].Go);
        Assert.Equal(1000, hits[0].Score);
        Assert.Equal("system", hits[1].Go);
        Assert.Equal(800, hits[1].Score);
    }

    [Fact]
    public void TilesSurfaceCompositor_sets_tiles_mode_without_seats()
    {
        var comp = new TilesSurfaceCompositor();
        var decision = new DeskDetailUnit.Snapshot("slim", false);
        var dict = comp.Compose(
            new TilesSurfaceScene(
                "cockpit/v1.20", "sa", new { }, new { pin = "git" }, null,
                new { }, null, null, null,
                ["git"], ["agent"], null, null),
            new TilesSurfacePayload(1),
            decision);
        Assert.Equal("tiles", dict["mode"]);
        Assert.Null(dict["seats"]);
        Assert.NotNull(dict["tiles"]);
        Assert.False(dict.ContainsKey("loci"));
    }

    [Fact]
    public void WorldSceneGoUnit_short_circuits_pulse_world_go()
    {
        var unit = new WorldSceneGoUnit();
        var yes = unit.Compute(new WorldSceneGoUnit.Input("git_scene", "pulse", false, true));
        Assert.True(yes.UseWorldSnap);
        Assert.Equal("git_scene", yes.Pin);
        var no = unit.Compute(new WorldSceneGoUnit.Input("git_scene", "full", false, true));
        Assert.False(no.UseWorldSnap);
    }

    [Fact]
    public void FocusLocusUnit_unknown_and_hit()
    {
        var unit = new FocusLocusUnit();
        var loci = new[] { new FocusLocusUnit.LocusRef("buf", "buffer", "open", "drill", "buffer", null) };
        Assert.Null(unit.Build(null, loci));
        dynamic miss = unit.Build("nope", loci)!;
        Assert.False((bool)miss.ok);
        dynamic hit = unit.Build("buf", loci)!;
        Assert.True((bool)hit.ok);
        Assert.Equal("buf", (string)hit.locus);
    }

    [Fact]
    public void GoVerbsCatalogUnit_merges_and_sorts()
    {
        var unit = new GoVerbsCatalogUnit();
        var verbs = unit.Merge(["zz", "sa"], ["aa"]);
        Assert.Equal(["aa", "sa", "zz"], verbs);
    }

    [Fact]
    public void SeatsDetailGateUnit_refuses_wspray_without_pane_full()
    {
        var unit = new SeatsDetailGateUnit();
        var snap = unit.Compute(new SeatsDetailGateUnit.Input("full", null, false, true));
        Assert.Equal("compact", snap.SeatsDetail);
        Assert.NotNull(snap.ThrashNote);
        Assert.False(snap.WantPanes);
    }

    [Fact]
    public void SeatOrganArgsSanitizer_strips_steer_keys()
    {
        var sanitizer = new SeatOrganArgsSanitizer();
        var args = new Dictionary<string, JsonElement>
        {
            ["go"] = JsonSerializer.SerializeToElement("browser"),
            ["op"] = JsonSerializer.SerializeToElement("scene")
        };
        var clean = sanitizer.Sanitize(args, wantFull: false);
        Assert.False(clean.ContainsKey("go"));
        Assert.True(clean.ContainsKey("op"));
        Assert.Equal("pulse", clean["go_detail"].GetString());
    }

    [Fact]
    public void GoResultSlimUnit_keeps_full_and_slims_fat()
    {
        var unit = new GoResultSlimUnit();
        var fat = new { ok = true, go = "arch_desk", board = new { roles = 1 }, pulse = "x" };
        var kept = unit.Slim(fat, "full", _ => new GoResultSlimUnit.OrganPulseSnap(true, "p", null, null, null));
        Assert.Same(fat, kept);
        dynamic slimmed = unit.Slim(fat, "pulse", _ => new GoResultSlimUnit.OrganPulseSnap(true, "arch · ok", null, null, null))!;
        Assert.True((bool)slimmed.slimmed);
        Assert.Equal("arch · ok", (string)slimmed.pulse);
    }

    [Fact]
    public void SeatFullPaneMatchUnit_matches_seat_organ_alias()
    {
        var unit = new SeatFullPaneMatchUnit();
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ed"] = "editor_scene" };
        Assert.True(unit.Matches("p", "p", "plan", aliases));
        Assert.True(unit.Matches("plan", "p", "plan", aliases));
        Assert.True(unit.Matches("ed", "f", "editor_scene", aliases));
        Assert.False(unit.Matches(null, "p", "plan", aliases));
    }

    [Fact]
    public void SeatOrganPanePresenter_full_or_passthrough()
    {
        var board = new { ok = true };
        Assert.Same(board, SeatOrganPanePresenter.FullOr(board, false, "x", "t"));
        dynamic full = SeatOrganPanePresenter.FullOr(board, true, "x", "t");
        Assert.Equal("full", (string)full.detail);
        Assert.Equal("x", (string)full.go);
    }
}
