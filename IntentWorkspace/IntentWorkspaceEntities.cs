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
    /// <summary>Optional product/category tag for grouping work units (e.g. Cursor|CDP|CIDE). Soft — not status.</summary>
    public string? Product { get; set; }
    /// <summary>Explicit Start gesture — wall clock begin of a measurable ship cycle. Null until cmd=start.</summary>
    public DateTimeOffset? StartedUtc { get; set; }
    /// <summary>Explicit Completed (shipped) gesture — wall clock end. Elapsed = Completed−Start (calendar, not agent-active).</summary>
    public DateTimeOffset? CompletedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    public IntentEntity? Intent { get; set; }
}

/// <summary>
/// First-class work-unit criterion (DoR / AC / DoD) — not Loot/notes.
/// Mode: manual | auto | hybrid (auto/hybrid later fed by producers like Change Planner).
/// </summary>
internal sealed class StageCriterionEntity
{
    public Guid Id { get; set; }
    public Guid StageId { get; set; }
    /// <summary>Wire: dor | ac | dod.</summary>
    public string Kind { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>Wire: manual | auto | hybrid.</summary>
    public string Mode { get; set; } = "manual";
    /// <summary>Wire: pending | met | unmet | waived.</summary>
    public string Status { get; set; } = "pending";
    /// <summary>Optional evidence pointer (planner/check/ref) for auto/hybrid.</summary>
    public string? EvidenceRef { get; set; }
    public int Ordinal { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>Append-only pointers while a stage wall clock is open — SA diagnostic, not a score.</summary>
internal sealed class StageEventEntity
{
    public Guid Id { get; set; }
    public Guid StageId { get; set; }
    public DateTimeOffset Utc { get; set; }
    public string Kind { get; set; } = "";
    public string Source { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? Ref { get; set; }
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
