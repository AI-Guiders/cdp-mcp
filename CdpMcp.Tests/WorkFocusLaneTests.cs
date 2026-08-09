#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

public sealed class WorkFocusLaneTests
{
    [Fact]
    public void Two_who_lanes_do_not_stomp_each_others_focus()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-lane-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path, FocusLane = "Кир" };
            store.IntentUpsert(state, "multi-principal", null);
            var kirLeaf = store.StageUpsert(state, "kir-leaf", null, null, null, "act").stage_id;
            var sierraLeaf = store.StageUpsert(state, "sierra-leaf", null, null, null, "act").stage_id;

            store.FocusStage(state, kirLeaf);
            Assert.Equal(kirLeaf, state.ActiveStageId);
            Assert.Equal("Кир", state.FocusLane);

            store.WorkFocusSwitchLane(state, "Sierra");
            Assert.Equal("Sierra", state.FocusLane);
            store.FocusStage(state, sierraLeaf);
            Assert.Equal(sierraLeaf, state.ActiveStageId);

            store.WorkFocusSwitchLane(state, "Кир");
            Assert.Equal("Кир", state.FocusLane);
            Assert.Equal(kirLeaf, state.ActiveStageId);

            var other = store.WorkFocusOtherLaneStageIds(state);
            Assert.Contains(sierraLeaf, other);
            Assert.DoesNotContain(kirLeaf, other);

            var snap = store.TaskManagerSnapshot(state);
            var stages = Assert.Single(snap.Features).Stages;
            var kir = Assert.Single(stages, s => s.Id == kirLeaf);
            var sierra = Assert.Single(stages, s => s.Id == sierraLeaf);
            Assert.Equal("active", kir.Status);
            Assert.Equal("active", sierra.Status);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Board_marks_other_lane_focus_with_guillemet()
    {
        var kir = Guid.NewGuid();
        var sierra = Guid.NewGuid();
        var nodes = new List<IdeTaskManager.StageNode>
        {
            new(kir, null, "kir-leaf", "active", 0, "act", null, null, null, "Кир"),
            new(sierra, null, "sierra-leaf", "active", 1, "act", null, null, null, "Sierra")
        };
        var other = new HashSet<Guid> { sierra };
        var lines = IdeTaskManager.FormatStageTree(nodes, kir, indent: 0, other).ToList();
        Assert.Contains(lines, l => l.Contains("[>]", StringComparison.Ordinal) && l.Contains("kir-leaf", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("[»]", StringComparison.Ordinal) && l.Contains("sierra-leaf", StringComparison.Ordinal));
    }

    [Fact]
    public void Repl_lane_maps_to_tm_op()
    {
        var empty = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var applied = IdeRepl.Apply("lane Sierra", empty);
        Assert.NotNull(applied);
        Assert.Equal("lane", applied.Value.Args["tm_op"].GetString());
        Assert.Equal("Sierra", applied.Value.Args["go_args"].GetProperty("lane").GetString());
    }

    static IntentWorkspaceStore BootStore(string path)
    {
        var opts = new DbContextOptionsBuilder<IntentWorkspaceDbContext>()
            .UseWitDb($"Data Source={path}")
            .Options;
        using (var boot = new IntentWorkspaceDbContext(opts))
            boot.Database.EnsureCreated();
        var store = new IntentWorkspaceStore(opts, path);
        store.EnsureStageClockColumns();
        store.EnsureStageProductColumn();
        store.EnsureStageExecutorColumn();
        store.EnsureWorkFocusTable();
        return store;
    }
}
