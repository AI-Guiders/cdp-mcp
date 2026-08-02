#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>
/// Regression 0.5.552: nested go_args.tm_op must mutate (cockpit go=plan + go_args).
/// Without OptGoArg(tm_op) / SoftBoard flatten, agents saw mutation=null.
/// </summary>
public sealed class IdeTaskManagerGoArgsTmOpTests
{
    [Fact]
    public void Nested_go_args_tm_op_note_mutates_active()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-tm-goargs-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "goargs-feature", null);
            var activeId = store.StageUpsert(state, "goargs-leaf", null, null, null).stage_id;
            store.FocusStage(state, activeId);
            IdeTaskManager.Handle(store, state, Args(new { tm_op = "start" }));

            var goArgs = JsonSerializer.SerializeToElement(new { tm_op = "note", text = "flatten dogfood" });
            var result = IdeTaskManager.Handle(store, state, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["go_args"] = goArgs
            });

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            var mutation = doc.RootElement.GetProperty("mutation");
            Assert.Equal("note", mutation.GetProperty("op").GetString());
            Assert.Equal(activeId.ToString(), mutation.GetProperty("task_id").GetGuid().ToString());
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    static IntentWorkspaceStore BootStore(string path)
    {
        var opts = new DbContextOptionsBuilder<IntentWorkspaceDbContext>().UseWitDb($"Data Source={path}").Options;
        using (var boot = new IntentWorkspaceDbContext(opts))
            boot.Database.EnsureCreated();
        var store = new IntentWorkspaceStore(opts, path);
        store.EnsureStageClockColumns();
        store.EnsureStageEventsTable();
        return store;
    }

    static IReadOnlyDictionary<string, JsonElement> Args(object anon)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(anon));
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }
}
