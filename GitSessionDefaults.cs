using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// After <c>cdp_open</c>, CDP may omit <c>workspace_path</c> on <c>git_*</c> —
/// inject session <see cref="SessionContext.ScmRoot"/> (ADR 0178 comfort).
/// </summary>
internal static class GitSessionDefaults
{
    public static string? TryResolveScmRoot(string? entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath))
            return null;
        try
        {
            return GitRootResolver.ResolveGitRoot(entryPath);
        }
        catch
        {
            return null;
        }
    }

    public static bool HasSlices(IReadOnlyDictionary<string, JsonElement> args) =>
        args.TryGetValue("slices", out var el)
        && el.ValueKind == JsonValueKind.Array
        && el.GetArrayLength() > 0;

    public static IReadOnlyDictionary<string, JsonElement> WithWorkspace(
        IReadOnlyDictionary<string, JsonElement> args,
        SessionContext session)
    {
        if (HasSlices(args) || HasWorkspace(args))
            return args;

        var scm = session.ScmRoot;
        if (string.IsNullOrWhiteSpace(scm) && !string.IsNullOrWhiteSpace(session.ProjectRoot))
            scm = TryResolveScmRoot(session.ProjectRoot);

        if (string.IsNullOrWhiteSpace(scm))
            return args;

        var copy = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal)
        {
            ["workspace_path"] = JsonSerializer.SerializeToElement(scm)
        };
        return copy;
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
                            if (item.ValueKind == JsonValueKind.String
                                && item.ValueEquals("workspace_path"))
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
                                        var prev = inner.Value.GetString() ?? "Repo root.";
                                        writer.WriteString(
                                            "description",
                                            prev.TrimEnd('.') + ". Optional after cdp_open — defaults to session scm_root.");
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
                                        "Repo root. Optional after cdp_open — defaults to session scm_root.");
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

    private static bool HasWorkspace(IReadOnlyDictionary<string, JsonElement> args) =>
        args.TryGetValue("workspace_path", out var ws)
        && ws.ValueKind == JsonValueKind.String
        && ws.GetString() is { Length: > 0 };
}
