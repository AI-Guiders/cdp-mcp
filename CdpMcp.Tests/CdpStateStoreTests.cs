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

        Assert.Null(CdpStateStore.LoadQueueState(root, "wake"));
        Assert.True(CdpStateStore.SetQueueState(root, new CdpQueueStateEntity { Id = "wake", Stopped = true, CooldownSeconds = 42 }));

        var row = CdpStateStore.LoadQueueState(root, "wake");
        Assert.NotNull(row);
        Assert.True(row!.Stopped);
        Assert.Equal(42, row.CooldownSeconds);

        Assert.True(CdpStateStore.SetQueueState(root, new CdpQueueStateEntity { Id = "wake", Stopped = false, CooldownSeconds = 7 }));
        var updated = CdpStateStore.LoadQueueState(root, "wake");
        Assert.False(updated!.Stopped);
        Assert.Equal(7, updated.CooldownSeconds);
    }

    [Fact]
    public void Subscriptions_upsert_load_delete_roundtrip()
    {
        var root = TempRoot();

        Assert.True(CdpStateStore.UpsertSubscription(root, new CdpWakeSubscriptionEntity { Id = "s1", Nick = "Ток", EventKind = "build_finished" }));
        Assert.True(CdpStateStore.UpsertSubscription(root, new CdpWakeSubscriptionEntity { Id = "s2", Nick = "Тень", EventKind = "shell_finished", TaskFilter = "di" }));

        var subs = CdpStateStore.LoadSubscriptions(root);
        Assert.Equal(2, subs.Count);

        Assert.True(CdpStateStore.UpsertSubscription(root, new CdpWakeSubscriptionEntity { Id = "s1", Nick = "Ток", EventKind = "peer_ship" }));
        Assert.Equal(2, CdpStateStore.LoadSubscriptions(root).Count);
        Assert.Contains(CdpStateStore.LoadSubscriptions(root), s => s.Id == "s1" && s.EventKind == "peer_ship");

        Assert.Equal(1, CdpStateStore.DeleteSubscriptions(root, subId: null, nick: "Тень", eventKind: "shell_finished"));
        Assert.Single(CdpStateStore.LoadSubscriptions(root));
        Assert.Equal(0, CdpStateStore.DeleteSubscriptions(root, subId: null, nick: "нет-такого", eventKind: null));
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

        var pending = CdpStateStore.LoadWake(root, "pending");
        var all = CdpStateStore.LoadWake(root);
        var arms = CdpStateStore.LoadArms(root, "other");

        Assert.Equal(1, pending.Count);
        Assert.Equal("w1", pending[0].Id);
        Assert.Equal(2, all.Count);
        Assert.Single(arms);
        Assert.Equal("arm-1", arms[0].Id);
        Assert.True(CdpStateStore.IsWakeStopped(root), "legacy stopped=true должен мигрировать");

        Assert.False(File.Exists(Path.Combine(root, "wake-dispatch.json")), "legacy → .bak (interop-only)");
        Assert.False(File.Exists(Path.Combine(root, "ignite-arms-other.json")));
        Assert.NotEmpty(Directory.GetFiles(root, "*.migrated-*.bak"));

        var registry = CdpStateStore.ListStores(root);
        Assert.Contains(registry, r => r.Name == "store-registry");
        Assert.Contains(registry, r => r.Name == "wake");
        Assert.Contains(registry, r => r.Name == "arms");
    }

    [Fact]
    public void Recovers_from_abandoned_gate_mutex_killrunning()
    {
        var root = TempRoot();
        var dbPath = CdpStateStore.DbPath(root);
        var abandoned = new Mutex(initiallyOwned: true, name: GateMutexName(dbPath));
        // Конструктор уже дал владение этому потоку — WaitOne здесь поднял бы счётчик до 2,
        // и один ReleaseMutex отпустил бы только до 1 (воркер ждал бы вечно).

        var worker = Task.Run(() => CdpStateStore.EnqueueWake(root, new CdpWakeEnvelopeEntity
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
        var loaded = CdpStateStore.LoadWake(root, "pending");
        Assert.Contains(loaded, x => x.Id == "z1");
    }

    [Fact]
    public void ReplaceArms_is_transactional_full_replacement()
    {
        var root = TempRoot();
        var row = new CdpIgniteArmEntity
        {
            Seat = "other", Id = "a1", Status = "armed",
            Json = """{"id":"a1"}""", StampedUtc = DateTimeOffset.UtcNow
        };
        Assert.True(CdpStateStore.ReplaceArms(root, "other", new[] { row }));
        Assert.Single(CdpStateStore.LoadArms(root, "other"));

        var replacement = new CdpIgniteArmEntity
        {
            Seat = "other", Id = "a2", Status = "armed",
            Json = """{"id":"a2"}""", StampedUtc = DateTimeOffset.UtcNow
        };
        Assert.True(CdpStateStore.ReplaceArms(root, "other", new[] { replacement }));
        var after = CdpStateStore.LoadArms(root, "other");
        Assert.Single(after);
        Assert.Equal("a2", after[0].Id);
    }

    [Fact]
    public void WakeStopped_flag_roundtrip()
    {
        var root = TempRoot();
        Assert.False(CdpStateStore.IsWakeStopped(root));
        Assert.True(CdpStateStore.SetWakeStopped(root, true));
        Assert.True(CdpStateStore.IsWakeStopped(root));
        Assert.True(CdpStateStore.SetWakeStopped(root, false));
        Assert.False(CdpStateStore.IsWakeStopped(root));
    }
}
