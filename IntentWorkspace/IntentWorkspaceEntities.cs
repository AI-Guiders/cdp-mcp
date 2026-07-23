namespace CdpMcp.IntentWorkspace;

internal sealed class IntentEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public List<StageEntity> Stages { get; set; } = [];
    public List<SceneEntity> Scenes { get; set; } = [];
}

internal sealed class StageEntity
{
    public Guid Id { get; set; }
    public Guid IntentId { get; set; }
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = "pending";
    public Guid? SceneId { get; set; }
    public int Ordinal { get; set; }
    public string? Loot { get; set; }
    public string? JobJson { get; set; }
    public string? JobError { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public IntentEntity? Intent { get; set; }
}

internal sealed class SceneEntity
{
    public Guid Id { get; set; }
    public Guid IntentId { get; set; }
    public string Name { get; set; } = "";
    public string SnapshotJson { get; set; } = "{}";
    public string? FocusPath { get; set; }
    public int? FocusLine { get; set; }
    public string? Loot { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public IntentEntity? Intent { get; set; }
}

internal sealed class OpenRecentEntity
{
    public Guid Id { get; set; }
    public string Path { get; set; } = "";
    public string? Root { get; set; }
    public string? Kind { get; set; }
    public string? Language { get; set; }
    public DateTimeOffset OpenedUtc { get; set; }
}
