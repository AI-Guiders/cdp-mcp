using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft-organ <c>build_*</c> parsers require <c>solution_path</c>; after <c>cdp_open</c> inject
/// session solution/project (parity with meta <c>cdp_build</c>/<c>cdp_test</c>).
/// </summary>
internal static class BuildSessionDefaults
{
    public static IReadOnlyDictionary<string, JsonElement> WithSession(
        IReadOnlyDictionary<string, JsonElement> args,
        SessionContext session)
    {
        if (HasNonEmpty(args, "solution_path"))
            return args;

        var sol = FirstNonEmpty(session.SolutionOrProjectPath, session.ProjectRoot);
        if (sol is null)
            return args;

        return new Dictionary<string, JsonElement>(args, StringComparer.Ordinal)
        {
            ["solution_path"] = JsonSerializer.SerializeToElement(sol)
        };
    }

    public static JsonElement OptionalSessionSchema(JsonElement schema)
    {
        try
        {
            using var doc = JsonDocument.Parse(schema.GetRawText());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return schema;

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.NameEquals("required") && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        writer.WritePropertyName("required");
                        writer.WriteStartArray();
                        foreach (var item in prop.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String && item.ValueEquals("solution_path"))
                                continue;
                            item.WriteTo(writer);
                        }

                        writer.WriteEndArray();
                        continue;
                    }

                    if (prop.NameEquals("properties") && prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        writer.WritePropertyName("properties");
                        writer.WriteStartObject();
                        foreach (var p in prop.Value.EnumerateObject())
                        {
                            if (p.NameEquals("solution_path") && p.Value.ValueKind == JsonValueKind.Object)
                            {
                                writer.WritePropertyName("solution_path");
                                writer.WriteStartObject();
                                var wroteDesc = false;
                                foreach (var inner in p.Value.EnumerateObject())
                                {
                                    if (inner.NameEquals("description"))
                                    {
                                        var prev = inner.Value.GetString() ?? "Solution or project path.";
                                        writer.WriteString(
                                            "description",
                                            prev.TrimEnd('.') + ". Optional after cdp_open — defaults to session solution_or_project_path.");
                                        wroteDesc = true;
                                    }
                                    else
                                    {
                                        inner.WriteTo(writer);
                                    }
                                }

                                if (!wroteDesc)
                                {
                                    writer.WriteString(
                                        "description",
                                        "Solution or project path. Optional after cdp_open — defaults to session solution_or_project_path.");
                                }

                                writer.WriteEndObject();
                            }
                            else
                            {
                                p.WriteTo(writer);
                            }
                        }

                        writer.WriteEndObject();
                        continue;
                    }

                    prop.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            return JsonSerializer.Deserialize<JsonElement>(stream.ToArray());
        }
        catch
        {
            return schema;
        }
    }

    static bool HasNonEmpty(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el)
        && el.ValueKind == JsonValueKind.String
        && el.GetString() is { Length: > 0 };

    static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }

        return null;
    }
}
