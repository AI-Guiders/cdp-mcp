using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

internal sealed record ContinuityDto
{
    [JsonPropertyName("default_move")]
    public required string DefaultMove { get; init; }

    [JsonPropertyName("agent_ide_canon")]
    public required string AgentIdeCanon { get; init; }

    [JsonPropertyName("agent_env")]
    public required string AgentEnv { get; init; }

    /// <summary>EICAS-like W/C/A context-cost hierarchy (same attention model as CIDE ADR 0021).</summary>
    [JsonPropertyName("context_budget")]
    public required ContextBudgetDto ContextBudget { get; init; }
}

/// <summary>
/// Context-cost tiers for agent tools — EICAS W/C/A (not TCAS).
/// W = do not by default; C = opt-in awareness; A = prefer / Dark Cockpit quiet path.
/// </summary>
internal sealed record ContextBudgetDto
{
    [JsonPropertyName("canon")]
    public string Canon { get; init; } =
        "EICAS W/C/A · cascade-ide ADR 0021 (not TCAS TA/RA)";

    /// <summary>Warning — burns session hard; avoid unless intentional.</summary>
    [JsonPropertyName("W")]
    public required string Warning { get; init; }

    /// <summary>Caution — opt-in dump; know the cost.</summary>
    [JsonPropertyName("C")]
    public required string Caution { get; init; }

    /// <summary>Advisory — quiet/default path (Dark Cockpit).</summary>
    [JsonPropertyName("A")]
    public required string Advisory { get; init; }

    [JsonPropertyName("habit")]
    public required string Habit { get; init; }
}

internal sealed record DebugStopDto
{
    [JsonPropertyName("available")]
    public required bool Available { get; init; }

    [JsonPropertyName("stop_context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StopContext { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}

/// <summary>explain_tool payload — one shape with optional fields (wire-compatible with prior anonymous objects).</summary>
internal sealed record ExplainToolResult
{
    [JsonPropertyName("ok")]
    public required bool Ok { get; init; }

    [JsonPropertyName("tool")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tool { get; init; }

    [JsonPropertyName("domain")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Domain { get; init; }

    [JsonPropertyName("underlying")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Underlying { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    [JsonPropertyName("visibility")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Visibility { get; init; }

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }

    [JsonPropertyName("context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionContextWire? Context { get; init; }

    [JsonPropertyName("affordance_phases")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AffordancePhases { get; init; }

    [JsonPropertyName("affordance_objects")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AffordanceObjects { get; init; }

    [JsonPropertyName("affordance_languages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AffordanceLanguages { get; init; }
}

/// <summary>
/// Backend JSON blob: full <see cref="JsonElement"/> or truncated preview.
/// Serializes as the element itself, or <c>{truncated,preview}</c>.
/// </summary>
[JsonConverter(typeof(JsonEmbedJsonConverter))]
internal readonly struct JsonEmbed
{
    public bool IsTruncated { get; }
    public JsonElement? Element { get; }
    public string? Preview { get; }

    private JsonEmbed(bool truncated, JsonElement? element, string? preview)
    {
        IsTruncated = truncated;
        Element = element;
        Preview = preview;
    }

    public static JsonEmbed From(JsonElement el, int maxChars)
    {
        var raw = el.GetRawText();
        if (raw.Length <= maxChars)
            return new(false, JsonSerializer.Deserialize<JsonElement>(raw), null);
        return new(true, null, raw[..maxChars] + "…");
    }
}

internal sealed class JsonEmbedJsonConverter : JsonConverter<JsonEmbed>
{
    public override JsonEmbed Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("JsonEmbed is write-only for session plane.");

    public override void Write(Utf8JsonWriter writer, JsonEmbed value, JsonSerializerOptions options)
    {
        if (value.IsTruncated)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("truncated", true);
            writer.WriteString("preview", value.Preview);
            writer.WriteEndObject();
            return;
        }

        if (value.Element is { } el)
        {
            el.WriteTo(writer);
            return;
        }

        writer.WriteNullValue();
    }
}
