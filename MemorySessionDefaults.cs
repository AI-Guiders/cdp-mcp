using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// After <c>cdp_open</c>, <c>memory_*</c> may omit <c>workspace_path</c> — inject session
/// project root (scope map / hot notes). Same comfort class as git / HCI.
/// </summary>
internal static class MemorySessionDefaults
{
    public static bool IsMemoryDomain(string domain) =>
        CdpDomains.LayerOf(domain) == CdpLayer.Memory;

    public static IReadOnlyDictionary<string, JsonElement> WithWorkspace(
        IReadOnlyDictionary<string, JsonElement> args,
        SessionContext session)
    {
        if (HasNonEmpty(args, "workspace_path"))
            return args;

        var ws = FirstNonEmpty(session.ProjectRoot, session.ScmRoot);
        if (ws is null)
            return args;

        return new Dictionary<string, JsonElement>(args, StringComparer.Ordinal)
        {
            ["workspace_path"] = JsonSerializer.SerializeToElement(ws)
        };
    }

    public static JsonElement OptionalWorkspaceSchema(JsonElement schema)
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
                            if (item.ValueKind == JsonValueKind.String && item.ValueEquals("workspace_path"))
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
                                writer.WritePropertyName("workspace_path");
                                writer.WriteStartObject();
                                var wroteDesc = false;
                                foreach (var inner in p.Value.EnumerateObject())
                                {
                                    if (inner.NameEquals("description"))
                                    {
                                        var prev = inner.Value.GetString() ?? "Workspace root.";
                                        writer.WriteString(
                                            "description",
                                            prev.TrimEnd('.') + ". Optional after cdp_open — defaults to session project_root.");
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
                                        "Workspace root. Optional after cdp_open — defaults to session project_root.");
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
