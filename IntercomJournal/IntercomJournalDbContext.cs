#nullable enable
using Microsoft.EntityFrameworkCore;

namespace Cdp.IntercomJournal;

public sealed class IntercomJournalEntity
{
    public string Id { get; set; } = "";
    public string FromSeat { get; set; } = "";
    public string ToSeat { get; set; } = "";
    public string Body { get; set; } = "";
    public string Origin { get; set; } = "";
    public string? Name { get; set; }
    public string? Kind { get; set; }
    public string? Channel { get; set; }
    public DateTimeOffset StampedUtc { get; set; }
    public bool Acked { get; set; }
}

public sealed class IntercomJournalDbContext : DbContext
{
    public IntercomJournalDbContext(DbContextOptions<IntercomJournalDbContext> options)
        : base(options)
    {
    }

    public DbSet<IntercomJournalEntity> Entries => Set<IntercomJournalEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<IntercomJournalEntity>();
        e.ToTable("intercom_journal");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.FromSeat).HasMaxLength(32).IsRequired();
        e.Property(x => x.ToSeat).HasMaxLength(32).IsRequired();
        e.Property(x => x.Body).IsRequired();
        e.Property(x => x.Origin).HasMaxLength(32).IsRequired();
        e.Property(x => x.Name).HasMaxLength(128);
        e.Property(x => x.Kind).HasMaxLength(32);
        e.Property(x => x.Channel).HasMaxLength(32);
        e.HasIndex(x => x.StampedUtc);
    }
}
