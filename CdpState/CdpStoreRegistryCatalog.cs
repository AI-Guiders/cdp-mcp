#nullable enable

namespace Cdp.CdpState;

/// <summary>
/// ADR-0219 P0 — канонический инвентарь store-registry: все коллекции cdp-state.witdb
/// и interop-каналы state root (GS-HS1 partial). SSOT для seed/upsert и тестов полноты.
/// </summary>
public static class CdpStoreRegistryCatalog
{
    public sealed record Entry(string Name, string Owner, string Format, string? Note = null);

    /// <summary>Все записи реестра: witdb-коллекции cdp-state + GS-HS1 interop-каналы.</summary>
    public static IReadOnlyList<Entry> CanonicalEntries { get; } =
    [
        new("wake", "CideWakeDispatch (ADR-0213)", "witdb"),
        new("arms", "IdeIgniteArmHost (ADR-0219 W1)", "witdb"),
        new("store-registry", "CdpStateStore (ADR-0219 P0)", "witdb"),
        new("queue-state", "CdpStateStore queue seats", "witdb"),
        new("wake-subscriptions", "NotificationCenter (ADR-0213)", "witdb"),
        new("latch_docs", "latch latches + pressure-stash:{seat} (P2-P3)", "witdb"),
        new("pressure-memos", "IdePressureChannel (ADR-0219 P3)", "witdb"),
        new("cide-latches", "GS-HS1 interop channels", "file",
            "IDE interop latch zoo (~25 Cide*Latch LATEST files; identity/presence mirrored to witdb)"),
        new("teeth-tape", "GS-HS1 interop channels", "file",
            "CIDE teeth tape jsonl — wake/delivery audit, interop"),
        new("remount-wake", "GS-HS1 interop channels", "file",
            "remount-*.pending.json — service restart wake notes, interop"),
        new("cdb-channel", "GS-HS1 interop channels", "file",
            "IDE status/telemetry channel to CdpService host, interop"),
    ];

    /// <summary>Имена всех канонических записей (для assert полноты GS-HS1 partial).</summary>
    public static IReadOnlySet<string> CanonicalNames { get; } =
        CanonicalEntries.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>True когда actual содержит все канонические имена с ожидаемым owner/format.</summary>
    public static bool IsComplete(IReadOnlyList<CdpStoreRegistryEntity> actual)
    {
        if (actual.Count < CanonicalEntries.Count)
            return false;
        var byName = actual.ToDictionary(x => x.Name, StringComparer.Ordinal);
        foreach (var expected in CanonicalEntries)
        {
            if (!byName.TryGetValue(expected.Name, out var row))
                return false;
            if (!string.Equals(row.Owner, expected.Owner, StringComparison.Ordinal))
                return false;
            if (!string.Equals(row.Format, expected.Format, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    internal static IEnumerable<CdpStoreRegistryEntity> ToEntities(DateTimeOffset stampedUtc)
        => CanonicalEntries.Select(e => new CdpStoreRegistryEntity
        {
            Name = e.Name,
            Owner = e.Owner,
            Format = e.Format,
            Note = e.Note,
            StampedUtc = stampedUtc
        });
}
