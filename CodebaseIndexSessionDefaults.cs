using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// After <c>cdp_open</c>, CDP may omit <c>workspace_path</c> / <c>solution_path</c> on
/// <c>codebase_index_*</c> — inject session project + solution (git parity; HCI scopes DB by both).
/// </summary>
internal static class CodebaseIndexSessionDefaults
{
    public static IReadOnlyDictionary<string, JsonElement> WithSession(
        IReadOnlyDictionary<string, JsonElement> args,
        SessionContext session)
    {
        var copy = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
        var changed = false;

        if (!HasNonEmpty(copy, "workspace_path"))
        {
            var ws = FirstNonEmpty(session.ProjectRoot, session.ScmRoot);
            if (ws is not null)
            {
                copy["workspace_path"] = JsonSerializer.SerializeToElement(ws);
                changed = true;
            }
        }

        if (!HasNonEmpty(copy, "solution_path")
            && session.SolutionOrProjectPath is { Length: > 0 } sol)
        {
            copy["solution_path"] = JsonSerializer.SerializeToElement(sol);
            changed = true;
        }

        return changed ? copy : args;
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
                            if (item.ValueKind == JsonValueKind.String
                                && (item.ValueEquals("workspace_path") || item.ValueEquals("solution_path")))
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
                            if (p.NameEquals("workspace_path") && p.Value.ValueKind == JsonValueKind.Object)
                            {
                                WritePatchedProp(
                                    writer,
                                    "workspace_path",
                                    p.Value,
                                    "Optional after cdp_open — defaults to session project_root.");
                            }
                            else if (p.NameEquals("solution_path") && p.Value.ValueKind == JsonValueKind.Object)
                            {
                                WritePatchedProp(
                                    writer,
                                    "solution_path",
                                    p.Value,
                                    "Optional after cdp_open — defaults to session solution_or_project_path (HCI DB scope).");
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

    static void WritePatchedProp(
        Utf8JsonWriter writer,
        string name,
        JsonElement value,
        string comfortSuffix)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        var wroteDesc = false;
        foreach (var inner in value.EnumerateObject())
        {
            if (inner.NameEquals("description"))
            {
                var prev = inner.Value.GetString() ?? name;
                writer.WriteString("description", prev.TrimEnd('.') + ". " + comfortSuffix);
                wroteDesc = true;
            }
            else
            {
                inner.WriteTo(writer);
            }
        }

        if (!wroteDesc)
            writer.WriteString("description", comfortSuffix);

        writer.WriteEndObject();
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
