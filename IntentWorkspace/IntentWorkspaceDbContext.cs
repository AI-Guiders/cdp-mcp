using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed class IntentWorkspaceDbContext(DbContextOptions<IntentWorkspaceDbContext> options)
    : DbContext(options)
{
    public DbSet<IntentEntity> Intents => Set<IntentEntity>();
    public DbSet<StageEntity> Stages => Set<StageEntity>();
    public DbSet<SceneEntity> Scenes => Set<SceneEntity>();
    public DbSet<OpenRecentEntity> OpenRecent => Set<OpenRecentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IntentEntity>(e =>
        {
            e.ToTable("intents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).IsRequired();
        });

        modelBuilder.Entity<StageEntity>(e =>
        {
            e.ToTable("stages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).IsRequired();
            e.Property(x => x.Status).IsRequired();
            e.HasIndex(x => x.IntentId);
            e.HasOne(x => x.Intent).WithMany(x => x.Stages).HasForeignKey(x => x.IntentId);
        });

        modelBuilder.Entity<SceneEntity>(e =>
        {
            e.ToTable("scenes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.SnapshotJson).IsRequired();
            e.HasIndex(x => new { x.IntentId, x.Name }).IsUnique();
            e.HasOne(x => x.Intent).WithMany(x => x.Scenes).HasForeignKey(x => x.IntentId);
        });

        modelBuilder.Entity<OpenRecentEntity>(e =>
        {
            e.ToTable("open_recent");
            e.HasKey(x => x.Id);
            e.Property(x => x.Path).IsRequired();
            e.HasIndex(x => x.Path);
            e.HasIndex(x => x.OpenedUtc);
        });
    }
}
