#nullable enable
using Microsoft.EntityFrameworkCore;

namespace Cdp.CdpState;

/// <summary>Wake-очередь (ADR-0213) — колонки вместо wake-dispatch.json.</summary>
public sealed class CdpWakeEnvelopeEntity
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Nick { get; set; } = "";
    public string Body { get; set; } = "";
    public string State { get; set; } = "pending";
    public string? SkippedReason { get; set; }
    public string? Detail { get; set; }
    public string? From { get; set; }
    public string? Harness { get; set; }
    public string? TaskKey { get; set; }
    public string? Seat { get; set; }
    public string? Session { get; set; }
    public DateTimeOffset StampedUtc { get; set; }
    public DateTimeOffset? DeliveredUtc { get; set; }
}

/// <summary>Ignite-arms (per-seat) — payload JSON 1:1 с моделью IdeIgniteArmHost.</summary>
public sealed class CdpIgniteArmEntity
{
    public string Seat { get; set; } = "";
    public string Id { get; set; } = "";
    public string Status { get; set; } = "";
    public string Json { get; set; } = "";
    public DateTimeOffset StampedUtc { get; set; }
}

/// <summary>ADR-0219 P0 — реестр сторов state root.</summary>
public sealed class CdpStoreRegistryEntity
{
    public string Name { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Format { get; set; } = "";
    public string? Note { get; set; }
    public DateTimeOffset StampedUtc { get; set; }
}

/// <summary>Флаги очереди (stopped/cooldown) — одна строка на очередь.</summary>
public sealed class CdpQueueStateEntity
{
    public string Id { get; set; } = "";
    public bool Stopped { get; set; }
    public int CooldownSeconds { get; set; }
    public bool HarnessCdt { get; set; }
    public DateTimeOffset StampedUtc { get; set; }
}

/// <summary>NotificationCenter-подписка ника на событие (ADR-0213 NC → коллекция ADR-0219).</summary>
public sealed class CdpWakeSubscriptionEntity
{
    public string Id { get; set; } = "";
    public string Nick { get; set; } = "";
    public string EventKind { get; set; } = "";
    public string? TaskFilter { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}

/// <summary>Latch-документы (ADR-0219 P2): одиночный JSON-документ на ключ (identity/presence
/// и будущие latch-коллекции). witdb-строка = SSOT; legacy LATEST-файлы мигрируют при первом
/// чтении и остаются interop-экспортом.</summary>
public sealed class CdpLatchDocEntity
{
    public string Id { get; set; } = "";
    public string Json { get; set; } = "";
    public DateTimeOffset StampedUtc { get; set; }
}

public sealed class CdpStateDbContext : DbContext
{
    public CdpStateDbContext(DbContextOptions<CdpStateDbContext> options)
        : base(options)
    {
    }

    public DbSet<CdpWakeEnvelopeEntity> Wake => Set<CdpWakeEnvelopeEntity>();
    public DbSet<CdpIgniteArmEntity> Arms => Set<CdpIgniteArmEntity>();
    public DbSet<CdpStoreRegistryEntity> Registry => Set<CdpStoreRegistryEntity>();
    public DbSet<CdpQueueStateEntity> QueueState => Set<CdpQueueStateEntity>();
    public DbSet<CdpWakeSubscriptionEntity> Subscriptions => Set<CdpWakeSubscriptionEntity>();
    public DbSet<CdpLatchDocEntity> LatchDocs => Set<CdpLatchDocEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var wake = modelBuilder.Entity<CdpWakeEnvelopeEntity>();
        wake.ToTable("wake_envelopes");
        wake.HasKey(x => x.Id);
        wake.Property(x => x.Id).HasMaxLength(64);
        wake.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        wake.Property(x => x.Nick).HasMaxLength(64);
        wake.Property(x => x.Body).IsRequired();
        wake.Property(x => x.State).HasMaxLength(16).IsRequired();
        wake.Property(x => x.SkippedReason).HasMaxLength(64);
        wake.Property(x => x.Detail).HasMaxLength(512);
        wake.Property(x => x.From).HasMaxLength(64);
        wake.Property(x => x.Harness).HasMaxLength(32);
        wake.Property(x => x.TaskKey).HasMaxLength(256);
        wake.Property(x => x.Seat).HasMaxLength(32);
        wake.Property(x => x.Session).HasMaxLength(64);
        wake.HasIndex(x => x.State);

        var arm = modelBuilder.Entity<CdpIgniteArmEntity>();
        arm.ToTable("ignite_arms");
        arm.HasKey(x => new { x.Seat, x.Id });
        arm.Property(x => x.Seat).HasMaxLength(32).IsRequired();
        arm.Property(x => x.Id).HasMaxLength(64).IsRequired();
        arm.Property(x => x.Status).HasMaxLength(16).IsRequired();
        arm.Property(x => x.Json).IsRequired();

        var reg = modelBuilder.Entity<CdpStoreRegistryEntity>();
        reg.ToTable("store_registry");
        reg.HasKey(x => x.Name);
        reg.Property(x => x.Name).HasMaxLength(64);
        reg.Property(x => x.Owner).HasMaxLength(64).IsRequired();
        reg.Property(x => x.Format).HasMaxLength(32).IsRequired();
        reg.Property(x => x.Note).HasMaxLength(256);

        var qs = modelBuilder.Entity<CdpQueueStateEntity>();
        qs.ToTable("queue_state");
        qs.HasKey(x => x.Id);
        qs.Property(x => x.Id).HasMaxLength(64);
    
        var sub = modelBuilder.Entity<CdpWakeSubscriptionEntity>();
        sub.ToTable("wake_subscriptions");
        sub.HasKey(x => x.Id);

        var latch = modelBuilder.Entity<CdpLatchDocEntity>();
        latch.ToTable("latch_docs");
        latch.HasKey(x => x.Id);
        latch.Property(x => x.Id).HasMaxLength(64).IsRequired();
        latch.Property(x => x.Json).IsRequired();
        sub.Property(x => x.Id).HasMaxLength(64);
        sub.Property(x => x.Nick).HasMaxLength(64).IsRequired();
        sub.Property(x => x.EventKind).HasMaxLength(32).IsRequired();
        sub.Property(x => x.TaskFilter).HasMaxLength(256);
        sub.HasIndex(x => x.Nick);
}
}
