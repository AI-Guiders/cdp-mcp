#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>Wave33: plan-repl TM intents via @intent cmd= host-execute.</summary>
public sealed class CitizenPlanReplHostTests
{
    [Fact]
    public void Router_cmd_feature_is_plan_repl()
    {
        var r = CitizenIntentRouter.RouteOne("cmd=feature wave33-leaf @act #CDP");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Cmd, r.Verb);
        Assert.Equal("feature wave33-leaf @act #CDP", r.Cmd);
        Assert.Equal("plan", r.Go);
    }

    [Fact]
    public void Router_cmd_shell_is_refused()
    {
        var r = CitizenIntentRouter.RouteOne("cmd=shell echo hi");
        Assert.False(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Refuse, r.Verb);
        Assert.Contains("refuse_non_plan_repl", r.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_cmd_feature_mutates_tm_and_places_plan()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-citizen-cmd-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            IdeStageCycle.Bind(store, () => state, () => "explore");

            IdeDeskSeats.EnsureDefaultsFromSettings();
            IdeDeskSeats.Clear();
            IdeDeskSeats.TryPlaceExplicit("p", "alert");
            IdeDeskSeats.TryPlaceExplicit("forward", "editor_scene");
            IdeDeskSeats.TryPlaceExplicit("m", "browser");

            var routes = new[]
            {
                CitizenIntentRouter.RouteOne("cmd=feature citizen-cmd-host-test @act #CDP")
            };
            var applied = CitizenRouteHost.Execute(routes);

            Assert.Single(applied);
            Assert.True(applied[0].Ok, applied[0].Reason);
            Assert.Equal("repl", applied[0].Action);
            Assert.Equal("plan", applied[0].Go);
            Assert.NotNull(applied[0].Pulse);
            Assert.Contains("citizen-cmd-host-test", applied[0].Pulse!, StringComparison.OrdinalIgnoreCase);

            var map = IdeDeskSeats.Snapshot();
            Assert.Contains(map, kv => string.Equals(kv.Value, "plan", StringComparison.OrdinalIgnoreCase));

            var snap = store.TaskManagerSnapshot(state);
            Assert.Contains(snap.Features, f => f.Title.Contains("citizen-cmd-host-test", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            IdeStageCycle.Unbind();
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Channel_dry_run_execute_cmd_note_hosts()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-citizen-cmd-ch-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "cmd-channel-feature", null);
            var stageId = store.StageUpsert(state, "cmd-channel-task", null, null, null).stage_id;
            store.FocusStage(state, stageId);
            IdeTaskManager.Handle(store, state, Args(new { tm_op = "start" }));
            IdeStageCycle.Bind(store, () => state, () => "act");

            var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["op"] = JsonSerializer.SerializeToElement("turn"),
                ["dry_run"] = JsonSerializer.SerializeToElement(true),
                ["execute"] = JsonSerializer.SerializeToElement(true),
                ["message"] = JsonSerializer.SerializeToElement(
                    "@intent cmd=note wave33 dogfood host repl")
            };

            var json = IdeCitizenChannel.HandleJson(args);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            var executed = doc.RootElement.GetProperty("executed");
            Assert.Equal(JsonValueKind.Array, executed.ValueKind);
            Assert.True(executed.GetArrayLength() >= 1);
            Assert.Equal("repl", executed[0].GetProperty("action").GetString());
            Assert.True(executed[0].GetProperty("ok").GetBoolean(), executed[0].GetProperty("reason").GetString());
        }
        finally
        {
            IdeStageCycle.Unbind();
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Execute_cmd_note_closed_clock_surfaces_open_clock_reason()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-citizen-note-closed-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "note-closed-feature", null);
            var stageId = store.StageUpsert(state, "note-closed-task", null, null, null).stage_id;
            store.FocusStage(state, stageId);
            // no start → wall closed
            IdeStageCycle.Bind(store, () => state, () => "act");

            var applied = CitizenRouteHost.Execute(
            [
                CitizenIntentRouter.RouteOne("cmd=note wave34 closed wall reason")
            ]);

            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Equal("repl", applied[0].Action);
            Assert.Contains("open clock", applied[0].Reason!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tm_failed", applied[0].Reason!, StringComparison.Ordinal);
        }
        finally
        {
            IdeStageCycle.Unbind();
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryWorkspace_lazy_ensure_binds_before_cmd()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-citizen-lazy-" + Guid.NewGuid().ToString("N") + ".witdb");
        var ensured = false;
        IdeStageCycle.Unbind();
        IdeStageCycle.SetEnsure(() =>
        {
            ensured = true;
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            IdeStageCycle.Bind(store, () => state, () => "explore");
        });

        try
        {
            IdeDeskSeats.EnsureDefaultsFromSettings();
            IdeDeskSeats.Clear();
            IdeDeskSeats.TryPlaceExplicit("p", "alert");
            IdeDeskSeats.TryPlaceExplicit("forward", "editor_scene");
            IdeDeskSeats.TryPlaceExplicit("m", "browser");

            var applied = CitizenRouteHost.Execute(
            [
                CitizenIntentRouter.RouteOne("cmd=feature citizen-lazy-ensure @act #CDP")
            ]);

            Assert.True(ensured);
            Assert.Single(applied);
            Assert.True(applied[0].Ok, applied[0].Reason);
            Assert.DoesNotContain("no_workspace", applied[0].Reason ?? "", StringComparison.Ordinal);
        }
        finally
        {
            IdeStageCycle.SetEnsure(null);
            IdeStageCycle.Unbind();
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    static Dictionary<string, JsonElement> Args(object anon)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(anon));
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
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
}
