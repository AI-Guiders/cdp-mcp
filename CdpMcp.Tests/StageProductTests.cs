#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

public sealed class StageProductTests
{
    [Theory]
    [InlineData("cdp", "CDP")]
    [InlineData("#Cursor", "Cursor")]
    [InlineData("CIDE", "CIDE")]
    [InlineData("clear", null)]
    [InlineData("-", null)]
    public void NormalizeProduct_canonicalizes_known_tags(string raw, string? expected)
    {
        Assert.Equal(expected, IntentWorkspaceStore.NormalizeProduct(raw));
    }

    [Fact]
    public void Set_product_roundtrips_on_snapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-prod-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "product-feature", null);
            var stageId = store.StageUpsert(state, "product-task", null, null, null, "act").stage_id;
            store.FocusStage(state, stageId);

            var set = store.StageSetProduct(state, stageId, "cdp");
            Assert.Equal("CDP", set.product);

            var snap = store.TaskManagerSnapshot(state);
            var node = Assert.Single(Assert.Single(snap.Features).Stages);
            Assert.Equal("CDP", node.Product);
            Assert.Equal("act", node.PhaseAffinity);

            store.StageSetProduct(state, stageId, "clear");
            snap = store.TaskManagerSnapshot(state);
            Assert.Null(Assert.Single(Assert.Single(snap.Features).Stages).Product);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Repl_product_and_hash_tag_parse()
    {
        var empty = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var applied = IdeRepl.Apply("product CDP", empty);
        Assert.NotNull(applied);
        Assert.Equal("product", applied.Value.Args["tm_op"].GetString());
        Assert.Equal("CDP", applied.Value.Args["go_args"].GetProperty("product").GetString());

        var (title, phase, product) = IdeRepl.SplitTitleMeta(["ship-omit", "@act", "#cdp"]);
        Assert.Equal("ship-omit", title);
        Assert.Equal("act", phase);
        Assert.Equal("cdp", product);

        (title, phase, product) = IdeRepl.SplitTitleMeta(["ship-omit", "#Cursor", "@act"]);
        Assert.Equal("ship-omit", title);
        Assert.Equal("act", phase);
        Assert.Equal("Cursor", product);
    }

    [Fact]
    public void Board_line_includes_product_hash()
    {
        var id = Guid.NewGuid();
        var nodes = new List<IdeTaskManager.StageNode>
        {
            new(id, null, "tagged", "active", 0, "act", null, null, "CDP")
        };
        var line = Assert.Single(IdeTaskManager.FormatStageTree(nodes, id, indent: 0));
        Assert.Contains("@act", line, StringComparison.Ordinal);
        Assert.Contains("#CDP", line, StringComparison.Ordinal);
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
        return store;
    }
}
