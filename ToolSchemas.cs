#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CdpMcp;

/// <summary>
/// JSON Schema из существующих доменных рекордов через стандартный
/// JsonSchemaExporter (.NET 9+): метаданные сериализатора — единственный
/// источник истины (политика имён, nullability), поэтому сгенерированная
/// схема не может разойтись с проводом. Рекорды именуем так, как они
/// выглядят на проводе.
/// </summary>
internal static class ToolSchemas
{
    public static readonly JsonSerializerOptions Options = new()
    {
        // Имена — snake, как на всём CDP-проводе: схема обязана совпадать с проводом.
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public static JsonNode For<T>() => For(typeof(T));

    public static JsonNode For(Type type) =>
        JsonSchemaExporter.GetJsonSchemaAsNode(Options, type);

    /// <summary>Для полей, которые приходят null'ом (hold=null после clear и т.п.):
    /// схема типа становится ["object","null"], иначе строгий SDK отвергнет ответ.</summary>
    public static JsonNode NullableFor<T>()
    {
        var node = For<T>();
        node["type"] = new JsonArray("object", "null");
        return node;
    }
}
