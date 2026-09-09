#nullable enable
using static CdpMcp.IdeIgniteArmHost;
using Cdp.CdpState;
using System.Text.Json;

namespace CdpMcp;

internal sealed partial class CdpIgniteArmHost
{
    void EnsureLoaded()
    {
        lock (Gate)
        {
            if (Loaded) return;
            Loaded = true;
            TryMigrateLegacyUnlocked();
            LoadFromStoreUnlocked();
        }
    }

    /// <summary>
    /// One-shot: legacy shared ignite-arms.json → seat file for live cdp only.
    /// Debug starts empty so sibling cannot ghost-fire live arms. The seat file itself is
    /// imported into cdp-state.witdb by the store migration-on-first-run (MigrateArms)
    /// and renamed .bak — the file is interop-only from then on.
    /// </summary>
    void TryMigrateLegacyUnlocked()
    {
        if (File.Exists(StorePath)) return;
        if (!string.Equals(Seat, "cdp", StringComparison.OrdinalIgnoreCase)) return;
        if (!File.Exists(LegacyStorePath)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.Copy(LegacyStorePath, StorePath);
            try { File.Move(LegacyStorePath, LegacyStorePath + ".migrated", overwrite: true); }
            catch { /* best-effort */ }
        }
        catch { /* first load without migration */ }
    }

    /// <summary>Store is SSOT across processes (ADR-0219 P1): arms of this seat live in
    /// ignite_arms rows; payload JSON is 1:1 with the host model (snake_case). Legacy rows
    /// imported by the store keep the legacy subset — missing fields default.</summary>
    void LoadFromStoreUnlocked()
    {
        var arms = new List<IgniteArm>();
        foreach (var row in Store.LoadArms(Seat))
        {
            var arm = FromEntity(row);
            if (arm is not null)
                arms.Add(arm);
        }
        Arms = arms;
        LoadedIds = arms.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    IgniteArm? FromEntity(CdpIgniteArmEntity row)
    {
        try
        {
            var arm = JsonSerializer.Deserialize<IgniteArm>(row.Json, JsonOpts);
            if (arm is null || string.IsNullOrWhiteSpace(arm.Id))
                return null;
            if (string.IsNullOrWhiteSpace(arm.Status))
                arm.Status = string.IsNullOrWhiteSpace(row.Status) ? "armed" : row.Status;
            return arm;
        }
        catch
        {
            return null;
        }
    }

    CdpIgniteArmEntity ToEntity(IgniteArm a) => new()
    {
        Seat = Seat,
        Id = a.Id,
        Status = a.Status,
        Json = JsonSerializer.Serialize(a, JsonOpts),
        StampedUtc = a.CreatedUtc
    };

    /// <summary>Транзакционная синхронизация (ADR-0219 P1): upsert текущих армов хоста и drop
    /// тех, что хост удалил со своей последней загрузки/reload. Строки сиблинг-процессов,
    /// которых хост не знал, стор сохраняет — двухписательский merge (A2-фикс a88771b)
    /// переживает переход на witdb, а disarm больше не реанимирует удалённые армы.</summary>
    void PersistUnlocked()
    {
        var rows = new List<CdpIgniteArmEntity>(Arms.Count);
        foreach (var arm in Arms)
            rows.Add(ToEntity(arm));

        var current = rows.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var drop = LoadedIds.Where(id => !current.Contains(id)).ToList();
        LoadedIds = current;

        Store.SyncArms(Seat, rows, drop);
    }

    /// <summary>Store is SSOT across processes: adopt arms written by siblings, drop in-memory
    /// arms the store no longer has (sibling fired/disarmed them) — in-flight fires are kept.</summary>
    internal void ReloadFromStoreUnlocked()
    {
        var fromStore = new List<IgniteArm>();
        foreach (var row in Store.LoadArms(Seat))
        {
            var arm = FromEntity(row);
            if (arm is not null)
                fromStore.Add(arm);
        }

        foreach (var arm in fromStore)
            if (!Arms.Any(a => a.Id.Equals(arm.Id, StringComparison.OrdinalIgnoreCase)))
                Arms.Add(Clone(arm));

        var firingIds = Firing.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Arms.RemoveAll(a =>
            !firingIds.Contains(a.Id)
            && !fromStore.Any(d => d.Id.Equals(a.Id, StringComparison.OrdinalIgnoreCase)));

        LoadedIds = Arms.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
