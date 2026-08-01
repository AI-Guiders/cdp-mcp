using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Typed pack dogfood embed for <c>cdp_session</c> (wire: snake_case via attributes).</summary>
internal sealed record PackPlaneResult
{
    [JsonPropertyName("available")]
    public required bool Available { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    [JsonPropertyName("facet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Facet { get; init; }

    [JsonPropertyName("pack_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PackId { get; init; }

    [JsonPropertyName("process_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProcessId { get; init; }

    [JsonPropertyName("procedure_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProcedureId { get; init; }

    [JsonPropertyName("list")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonEmbed? List { get; init; }

    [JsonPropertyName("process")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonEmbed? Process { get; init; }

    [JsonPropertyName("procedure")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonEmbed? Procedure { get; init; }

    [JsonPropertyName("definition_debug_radius")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonEmbed? DefinitionDebugRadius { get; init; }

    [JsonPropertyName("suggested_next")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SuggestedNextDto? SuggestedNext { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    public static PackPlaneResult Unavailable(string reason) => new()
    {
        Available = false,
        Reason = reason
    };

    /// <summary>A-path stub: pack not embedded; escalate with include_pack=true.</summary>
    public static PackPlaneResult Omitted(string packId, string processId, string? procedureId) => new()
    {
        Available = true,
        PackId = packId,
        ProcessId = processId,
        ProcedureId = procedureId,
        Reason = "omitted_A",
        SuggestedNext = new SuggestedNextDto
        {
            Policy = "ask",
            Note = "Pack dump omitted (A). Need dogfood → include_pack=true (C/W).",
            Candidates =
            [
                SuggestedCandidateDto.Tool("cdp_session", hint: "include_pack=true"),
                SuggestedCandidateDto.Cue("Or memory_world_list_pack / get_process — one card, not full embed.")
            ]
        }
    };

    public static PackPlaneResult Failed(string facet, string packId, string error) => new()
    {
        Available = true,
        Facet = facet,
        PackId = packId,
        Error = error
    };
}

internal sealed record SuggestedNextDto
{
    [JsonPropertyName("policy")]
    public required string Policy { get; init; }

    [JsonPropertyName("note")]
    public required string Note { get; init; }

    [JsonPropertyName("candidates")]
    public required IReadOnlyList<SuggestedCandidateDto> Candidates { get; init; }
}

internal sealed record SuggestedCandidateDto
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonPropertyName("definition_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefinitionId { get; init; }

    [JsonPropertyName("process_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProcessId { get; init; }

    [JsonPropertyName("procedure_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProcedureId { get; init; }

    [JsonPropertyName("hint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hint { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    public static SuggestedCandidateDto Tool(
        string name,
        string? definitionId = null,
        string? processId = null,
        string? procedureId = null,
        string? hint = null) =>
        new()
        {
            Kind = "tool",
            Name = name,
            DefinitionId = definitionId,
            ProcessId = processId,
            ProcedureId = procedureId,
            Hint = hint
        };

    public static SuggestedCandidateDto Cue(string text) =>
        new() { Kind = "cue", Text = text };
}

internal sealed record SessionPlaneResult
{
    [JsonPropertyName("plane")]
    public string Plane { get; init; } = "cdp_session";

    [JsonPropertyName("context")]
    public required SessionContextWire Context { get; init; }

    [JsonPropertyName("workspace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkspacePlaneDto? Workspace { get; init; }

    [JsonPropertyName("shortlist")]
    public required IReadOnlyList<ShortlistItemDto> Shortlist { get; init; }

    [JsonPropertyName("health")]
    public required SessionHealthDto Health { get; init; }

    [JsonPropertyName("debug")]
    public required DebugStopDto Debug { get; init; }

    [JsonPropertyName("pack")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PackPlaneResult? Pack { get; init; }

    [JsonPropertyName("continuity")]
    public required ContinuityDto Continuity { get; init; }

    [JsonPropertyName("explain_tool")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExplainToolResult? ExplainTool { get; init; }
}

internal sealed record SessionContextWire
{
    [JsonPropertyName("phase")]
    public required string Phase { get; init; }

    [JsonPropertyName("object")]
    public required string Object { get; init; }

    [JsonPropertyName("intent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Intent { get; init; }

    [JsonPropertyName("language")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Language { get; init; }

    [JsonPropertyName("project_root")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjectRoot { get; init; }

    [JsonPropertyName("project_kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjectKind { get; init; }

    [JsonPropertyName("solution_or_project_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SolutionOrProjectPath { get; init; }

    [JsonPropertyName("tsconfig_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TsConfigPath { get; init; }

    [JsonPropertyName("scm_root")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ScmRoot { get; init; }

    public static SessionContextWire From(SessionContext session) => new()
    {
        Phase = CdpEnumParse.ToWire(session.Phase),
        Object = CdpEnumParse.ToWire(session.Object),
        Intent = session.Intent is null ? null : CdpEnumParse.ToWire(session.Intent.Value),
        Language = session.Language,
        ProjectRoot = session.ProjectRoot,
        ProjectKind = session.ProjectKind,
        SolutionOrProjectPath = session.SolutionOrProjectPath,
        TsConfigPath = session.TsConfigPath,
        ScmRoot = session.ScmRoot
    };
}

internal sealed record WorkspacePlaneDto
{
    [JsonPropertyName("active_intent_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveIntentId { get; init; }

    [JsonPropertyName("active_scene_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveSceneId { get; init; }

    [JsonPropertyName("active_scene_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveSceneName { get; init; }

    [JsonPropertyName("database_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DatabasePath { get; init; }
}

internal sealed record ShortlistItemDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("score")]
    public required int Score { get; init; }

    [JsonPropertyName("hint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hint { get; init; }
}

internal sealed record SessionHealthDto
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; } = true;

    [JsonPropertyName("backends")]
    public required IReadOnlyList<BackendHealthDto> Backends { get; init; }
}

internal sealed record BackendHealthDto
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("enabled")]
    public required bool Enabled { get; init; }

    [JsonPropertyName("health")]
    public required string Health { get; init; }
}
