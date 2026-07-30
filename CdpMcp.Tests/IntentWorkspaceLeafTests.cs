using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

public class IntentWorkspaceLeafTests
{
    [Fact]
    public void Flat_roots_first_and_next_leaf()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-leaf-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "Leaf Feature Flat", null);
            var t1 = store.StageUpsert(state, "Task 1", null, null, null).stage_id;
            var t2 = store.StageUpsert(state, "Task 2", null, null, null).stage_id;

            Assert.Equal(t1, store.FindFirstIncompleteLeaf(state));
            Assert.Equal(t2, store.FindNextIncompleteLeaf(state, afterStageId: t1));

            store.StageSetStatus(state, t1, "done");
            Assert.Equal(t2, store.FindFirstIncompleteLeaf(state));
            Assert.Equal(t2, store.FindNextIncompleteLeaf(state, afterStageId: t1));
            Assert.Null(store.FindNextIncompleteLeaf(state, afterStageId: t2));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Nested_container_resolves_to_child_leaves()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-leaf-n-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "Leaf Feature Nest", null);
            var container = store.StageUpsert(state, "Container", null, null, null).stage_id;
            var c1 = store.StageUpsert(state, "Child 1", null, container, null).stage_id;
            var c2 = store.StageUpsert(state, "Child 2", null, container, null).stage_id;
            var sibling = store.StageUpsert(state, "Sibling leaf", null, null, null).stage_id;

            Assert.Equal(c1, store.FindFirstIncompleteLeaf(state));
            Assert.Equal(c1, store.ResolveIncompleteLeaf(state, container));
            Assert.Equal(c2, store.FindNextIncompleteLeaf(state, afterStageId: c1));
            Assert.Equal(sibling, store.FindNextIncompleteLeaf(state, afterStageId: c2));

            store.StageSetStatus(state, c1, "done");
            store.StageSetStatus(state, c2, "done");
            // Container still pending + no incomplete children → becomes the next leaf before sibling.
            Assert.Equal(container, store.FindFirstIncompleteLeaf(state));
            Assert.Equal(container, store.ResolveIncompleteLeaf(state, container));
            Assert.Equal(sibling, store.FindNextIncompleteLeaf(state, afterStageId: container));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Handoff_and_parked_skipped()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-leaf-h-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "Leaf Feature Skip", null);
            var parked = store.StageUpsert(state, "Parked", null, null, null).stage_id;
            store.StageSetStatus(state, parked, "parked");
            var handoff = store.StageUpsert(state, "Handoff", null, null, null, phaseAffinity: "handoff").stage_id;
            var ok = store.StageUpsert(state, "Real leaf", null, null, null).stage_id;

            Assert.Equal(ok, store.FindFirstIncompleteLeaf(state));
            Assert.Null(store.ResolveIncompleteLeaf(state, parked));
            Assert.Null(store.ResolveIncompleteLeaf(state, handoff));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
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
        store.EnsureStageEventsTable();
        return store;
    }
}
