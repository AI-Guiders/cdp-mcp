using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed class IntentWorkspaceDbContext(DbContextOptions<IntentWorkspaceDbContext> options)
    : DbContext(options)
{
    public DbSet<IntentEntity> Intents => Set<IntentEntity>();
    public DbSet<StageEntity> Stages => Set<StageEntity>();
    public DbSet<StageEventEntity> StageEvents => Set<StageEventEntity>();
    public DbSet<SceneEntity> Scenes => Set<SceneEntity>();
    public DbSet<OpenRecentEntity> OpenRecent => Set<OpenRecentEntity>();
    public DbSet<DeskSeatEntity> DeskSeats => Set<DeskSeatEntity>();
    public DbSet<WorkFocusEntity> WorkFocus => Set<WorkFocusEntity>();
    public DbSet<ScriptLastRunEntity> ScriptLastRuns => Set<ScriptLastRunEntity>();

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

        modelBuilder.Entity<StageEventEntity>(e =>
        {
            e.ToTable("stage_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).IsRequired();
            e.Property(x => x.Source).IsRequired();
            e.Property(x => x.Summary).IsRequired();
            e.HasIndex(x => new { x.StageId, x.Utc });
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

        modelBuilder.Entity<DeskSeatEntity>(e =>
        {
            e.ToTable("desk_seats");
            e.HasKey(x => x.Seat);
            e.Property(x => x.Seat).IsRequired();
        });

        modelBuilder.Entity<WorkFocusEntity>(e =>
        {
            e.ToTable("work_focus");
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<ScriptLastRunEntity>(e =>
        {
            e.ToTable("script_last_run");
            e.HasKey(x => x.RootKey);
            e.Property(x => x.RootKey).IsRequired();
            e.Property(x => x.Path).IsRequired();
            e.Property(x => x.Mode).IsRequired();
            e.Property(x => x.Pulse).IsRequired();
            e.Property(x => x.BoardJson).IsRequired();
        });
    }
}
