namespace CdpMcp.IntentWorkspace;

public sealed record IntentUpsertResult(Guid intent_id, string title, bool active);

public sealed record StageSetStatusResult(Guid stage_id, string status);

public sealed record StageUpsertResult(Guid stage_id, string title, string status, Guid? parent_id, Guid? scene_id, int ordinal);

