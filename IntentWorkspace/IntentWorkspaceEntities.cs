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
    /// <summary>Optional epistemic phase affinity (wire: explore|plan|act|…). Soft — not Stage status.</summary>
    public string? PhaseAffinity { get; set; }
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

/// <summary>Scan Pattern desk seats — one row per seat (p|forward|m).</summary>
internal sealed class DeskSeatEntity
{
    public string Seat { get; set; } = "";
    public string? Organ { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>Singleton sticky Task Manager focus — survives MCP remount.</summary>
internal sealed class WorkFocusEntity
{
    public int Id { get; set; } = 1;
    public Guid? ActiveIntentId { get; set; }
    public Guid? ActiveStageId { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>Thin ScriptScene last-run pulse/board — survives MCP remount (ADR 0193 comfort).</summary>
internal sealed class ScriptLastRunEntity
{
    public string RootKey { get; set; } = "";
    public string Path { get; set; } = "";
    public string Mode { get; set; } = "";
    public bool Ok { get; set; }
    public DateTimeOffset AtUtc { get; set; }
    public string Pulse { get; set; } = "";
    public string? BodyJson { get; set; }
    public string BoardJson { get; set; } = "[]";
}
