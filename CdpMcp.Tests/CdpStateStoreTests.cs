#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cdp.CdpState;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>ADR-0219 P0+P1 — cdp-state.witdb: миграция legacy JSON + KillRunning (AbandonedMutex) + транзакционность arms.</summary>
public class CdpStateStoreTests
{
    [Fact]
    public void Queue_state_upsert_and_load_roundtrip()
    {
        var root = TempRoot();
        var store = new CdpStateStore(root);

        Assert.Null(store.LoadQueueState("wake"));
        Assert.True(store.SetQueueState(new CdpQueueStateEntity { Id = "wake", Stopped = true, CooldownSeconds = 42 }));

        var row = store.LoadQueueState("wake");
        Assert.NotNull(row);
        Assert.True(row!.Stopped);
        Assert.Equal(42, row.CooldownSeconds);

        Assert.True(store.SetQueueState(new CdpQueueStateEntity { Id = "wake", Stopped = false, CooldownSeconds = 7 }));
        var updated = store.LoadQueueState("wake");
        Assert.False(updated!.Stopped);
        Assert.Equal(7, updated.CooldownSeconds);
    }

    [Fact]
    public void Subscriptions_upsert_load_delete_roundtrip()
    {
        var root = TempRoot();
        var store = new CdpStateStore(root);

        Assert.True(store.UpsertSubscription(new CdpWakeSubscriptionEntity { Id = "s1", Nick = "Ток", EventKind = "build_finished" }));
        Assert.True(store.UpsertSubscription(new CdpWakeSubscriptionEntity { Id = "s2", Nick = "Тень", EventKind = "shell_finished", TaskFilter = "di" }));

        var subs = store.LoadSubscriptions();
        Assert.Equal(2, subs.Count);

        Assert.True(store.UpsertSubscription(new CdpWakeSubscriptionEntity { Id = "s1", Nick = "Ток", EventKind = "peer_ship" }));
        Assert.Equal(2, store.LoadSubscriptions().Count);
        Assert.Contains(store.LoadSubscriptions(), s => s.Id == "s1" && s.EventKind == "peer_ship");

        Assert.Equal(1, store.DeleteSubscriptions(subId: null, nick: "Тень", eventKind: "shell_finished"));
        Assert.Single(store.LoadSubscriptions());
        Assert.Equal(0, store.DeleteSubscriptions(subId: null, nick: "нет-такого", eventKind: null));
    }

    static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-state-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    static string GateMutexName(string dbPath)
    {
        var key = Path.GetFullPath(dbPath).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return $@"Local\CdpMcp.WitDb.{hash}";
    }

    [Fact]
    public void Migrates_legacy_wake_and_arms_and_renames_aside()
    {
        var root = TempRoot();
        var store = new CdpStateStore(root);
        var legacyWake = new
        {
            schema = "wake_dispatch/v1",
            stopped = true,
            delivery_cooldown_seconds = 15,
            queue = new object[]
            {
                new { id = "w1", kind = "letter", nick = "Тень", body = "knock", state = "pending" },
                new { id = "w2", kind = "letter", nick = "Ток", body = "old", state = "delivered" }
            }
        };
        File.WriteAllText(Path.Combine(root, "wake-dispatch.json"), JsonSerializer.Serialize(legacyWake));
        var arm = new
        {
            id = "arm-1",
            @event = "timer",
            task = "t",
            status = "armed",
            harness = "opencode",
            created_utc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(Path.Combine(root, "ignite-arms-other.json"), JsonSerializer.Serialize(new[] { arm }));

        var pending = store.LoadWake("pending");
        var all = store.LoadWake();
        var arms = store.LoadArms("other");

        Assert.Equal(1, pending.Count);
        Assert.Equal("w1", pending[0].Id);
        Assert.Equal(2, all.Count);
        Assert.Single(arms);
        Assert.Equal("arm-1", arms[0].Id);
        Assert.True(store.IsWakeStopped(), "legacy stopped=true должен мигрировать");

        Assert.False(File.Exists(Path.Combine(root, "wake-dispatch.json")), "legacy → .bak (interop-only)");
        Assert.False(File.Exists(Path.Combine(root, "ignite-arms-other.json")));
        Assert.NotEmpty(Directory.GetFiles(root, "*.migrated-*.bak"));

        var registry = store.ListStores();
        Assert.Contains(registry, r => r.Name == "store-registry");
        Assert.Contains(registry, r => r.Name == "wake");
        Assert.Contains(registry, r => r.Name == "arms");
    }

    [Fact]
    public void Recovers_from_abandoned_gate_mutex_killrunning()
    {
        var root = TempRoot();
        var store = new CdpStateStore(root);
        var dbPath = CdpStateStoreDb.DbPath(root);
        var abandoned = new Mutex(initiallyOwned: true, name: GateMutexName(dbPath));
        // Конструктор уже дал владение этому потоку — WaitOne здесь поднял бы счётчик до 2,
        // и один ReleaseMutex отпустил бы только до 1 (воркер ждал бы вечно).

        var worker = Task.Run(() => store.EnqueueWake(new CdpWakeEnvelopeEntity
        {
            Id = "z1",
            Kind = "letter",
            Body = "killrunning",
            StampedUtc = DateTimeOffset.UtcNow
        }));

        Thread.Sleep(1500); // воркер должен ждать гейт (12s wait), не падать
        Assert.False(worker.IsCompleted, "воркер ждёт гейт — не отказывается сразу");

        abandoned.ReleaseMutex(); // «процесс умер» → AbandonedMutex у воркера
        abandoned.Dispose();

        Assert.True(worker.Wait(TimeSpan.FromSeconds(15)), "после релиза воркер завершается");
        Assert.True(worker.Result, "конверт записан после восстановления гейта");
        var loaded = store.LoadWake("pending");
        Assert.Contains(loaded, x => x.Id == "z1");
    }

    [Fact]
    public void ReplaceArms_is_transactional_full_replacement()
    {
        var root = TempRoot();
        var store = new CdpStateStore(root);
        var row = new CdpIgniteArmEntity
        {
            Seat = "other", Id = "a1", Status = "armed",
            Json = """{"id":"a1"}""", StampedUtc = DateTimeOffset.UtcNow
        };
        Assert.True(store.ReplaceArms("other", new[] { row }));
        Assert.Single(store.LoadArms("other"));

        var replacement = new CdpIgniteArmEntity
        {
            Seat = "other", Id = "a2", Status = "armed",
            Json = """{"id":"a2"}""", StampedUtc = DateTimeOffset.UtcNow
        };
        Assert.True(store.ReplaceArms("other", new[] { replacement }));
        var after = store.LoadArms("other");
        Assert.Single(after);
        Assert.Equal("a2", after[0].Id);
    }

    [Fact]
    public void WakeStopped_flag_roundtrip()
    {
        var root = TempRoot();
        var store = new CdpStateStore(root);
        Assert.False(store.IsWakeStopped());
        Assert.True(store.SetWakeStopped(true));
        Assert.True(store.IsWakeStopped());
        Assert.True(store.SetWakeStopped(false));
        Assert.False(store.IsWakeStopped());
    }
}
