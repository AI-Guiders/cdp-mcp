#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

public sealed class StageExecutorTests
{
    [Theory]
    [InlineData("sierra", "Sierra")]
    [InlineData("~Кир", "Кир")]
    [InlineData("kir", "Кир")]
    [InlineData("@Света", "Света")]
    [InlineData("clear", null)]
    [InlineData("-", null)]
    public void NormalizeExecutor_canonicalizes_known_who(string raw, string? expected)
    {
        Assert.Equal(expected, IntentWorkspaceStore.NormalizeExecutor(raw));
    }

    [Fact]
    public void Set_executor_roundtrips_on_snapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-exec-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "executor-feature", null);
            var stageId = store.StageUpsert(state, "executor-task", null, null, null, "act").stage_id;
            store.FocusStage(state, stageId);

            var set = store.StageSetExecutor(state, stageId, "kir");
            Assert.Equal("Кир", set.executor);

            var snap = store.TaskManagerSnapshot(state);
            var node = Assert.Single(Assert.Single(snap.Features).Stages);
            Assert.Equal("Кир", node.Executor);
            Assert.Equal("act", node.PhaseAffinity);

            store.StageSetExecutor(state, stageId, "clear");
            snap = store.TaskManagerSnapshot(state);
            Assert.Null(Assert.Single(Assert.Single(snap.Features).Stages).Executor);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Repl_executor_and_tilde_tag_parse()
    {
        var empty = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var applied = IdeRepl.Apply("executor Sierra", empty);
        Assert.NotNull(applied);
        Assert.Equal("executor", applied.Value.Args["tm_op"].GetString());
        Assert.Equal("Sierra", applied.Value.Args["go_args"].GetProperty("executor").GetString());

        var (title, phase, product, executor) = IdeRepl.SplitTitleMeta(["ship-omit", "@act", "#cdp", "~kir"]);
        Assert.Equal("ship-omit", title);
        Assert.Equal("act", phase);
        Assert.Equal("cdp", product);
        Assert.Equal("kir", executor);
    }

    [Fact]
    public void Board_line_includes_executor_tilde()
    {
        var id = Guid.NewGuid();
        var nodes = new List<IdeTaskManager.StageNode>
        {
            new(id, null, "tagged", "active", 0, "act", null, null, "CDP", "Sierra")
        };
        var line = Assert.Single(IdeTaskManager.FormatStageTree(nodes, id, indent: 0));
        Assert.Contains("@act", line, StringComparison.Ordinal);
        Assert.Contains("#CDP", line, StringComparison.Ordinal);
        Assert.Contains("~Sierra", line, StringComparison.Ordinal);
        Assert.Contains("[>]", line, StringComparison.Ordinal);
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
        return store;
    }
}
