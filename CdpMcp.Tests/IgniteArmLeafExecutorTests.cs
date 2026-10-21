using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>
/// Адресат доставки по умолчанию — исполнитель активного TM-листа (~Executor), если это
/// известная реестру линия. Раньше адресатом была «единственная живая opencode-линия»,
/// то есть будильник уходил не тому, кому принадлежит лист (инцидент tm-wake).
/// </summary>
[Collection("CdpProfileIsolation")]
public sealed class IgniteArmLeafExecutorTests : IDisposable
{
    readonly string _root;

    public IgniteArmLeafExecutorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-arm-leaf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        IdeToolCallWatch.SuppressArmForTests = false;
        IdeIgniteArmHost.SetAutonomous(false, "test_setup");
        IdeIgniteArmHost.SetHild(false, "test_setup");
    }

    public void Dispose()
    {
        IdeToolCallWatch.SuppressArmForTests = false;
        IdeIgniteArmHost.SetAutonomous(false, "test_dispose");
        IdeIgniteArmHost.SetHild(false, "test_dispose");
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    IntentWorkspaceStore BootStore()
    {
        var path = Path.Combine(_root, "intent.witdb");
        var opts = new DbContextOptionsBuilder<IntentWorkspaceDbContext>()
            .UseWitDb($"Data Source={path}")
            .Options;
        using (var boot = new IntentWorkspaceDbContext(opts))
            boot.Database.EnsureCreated();
        var store = new IntentWorkspaceStore(opts, path);
        store.EnsureStageClockColumns();
        store.EnsureStageEventsTable();
        store.EnsureStageExecutorColumn();
        return store;
    }

    [Fact]
    public void Arm_without_explicit_seat_takes_executor_of_active_leaf()
    {
        var store = BootStore();
        var state = new IntentWorkspaceState();
        _ = store.IntentUpsert(state, "фича", null);
        var stage = store.StageUpsert(state, "лист", null, null, null);
        _ = store.StageSetExecutor(state, stage.stage_id, "Тестовик");
        IdeStageCycle.Bind(store, () => state);

        var id = "leaf-owner-" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("arm"),
                ["when"] = JsonSerializer.SerializeToElement("shell_finished"),
                ["id"] = JsonSerializer.SerializeToElement(id),
                ["task"] = JsonSerializer.SerializeToElement("leaf owner probe"),
                ["settle_seconds"] = JsonSerializer.SerializeToElement(0),
                ["ok_only"] = JsonSerializer.SerializeToElement(false),
                ["once"] = JsonSerializer.SerializeToElement(false),
            });

            var armed = IdeIgniteArmHost.Snapshot().FirstOrDefault(a => a.Id == id);
            Assert.NotNull(armed);
            Assert.Equal("Тестовик", armed!.OwnerNick);
            Assert.Equal("ses_test0000000000000000000", armed.OpencodeSession);
        }
        finally
        {
            _ = IdeIgniteArmHost.Disarm(new Dictionary<string, JsonElement>
            {
                ["id"] = JsonSerializer.SerializeToElement(id),
                ["force"] = JsonSerializer.SerializeToElement(true)
            });
        }
    }
}
