#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent editor_scene|cdp_editor_scene|editor — editor map without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteEditorScene(string raw)
    {
        var work = NormalizeEditorSceneCompound(raw);
        var path = ExtractKeyedValue(work, "path")
            ?? ExtractKeyedValue(work, "file_path")
            ?? ExtractKeyedValue(work, "file");
        var detail = ExtractKeyedValue(work, "detail");
        var docId = ExtractKeyedValue(work, "doc_id")
            ?? ExtractKeyedValue(work, "doc");
        var locus = ExtractKeyedValue(work, "locus")
            ?? ExtractKeyedValue(work, "focus");
        var contextLines = ExtractKeyedValue(work, "context_lines");

        return new Route(
            Verb.EditorScene,
            raw,
            Ok: true,
            Op: string.IsNullOrWhiteSpace(detail) ? null : detail.Trim().ToLowerInvariant(),
            Path: path,
            Tool: docId,
            NewString: locus,
            Detail: contextLines,
            Go: "editor_scene");
    }

    static string NormalizeEditorSceneCompound(string raw)
    {
        foreach (var prefix in EditorScenePrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "editor_scene";
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;
            return "editor_scene " + raw[prefix.Length..].TrimStart();
        }

        return raw;
    }

    static readonly string[] EditorScenePrefixes =
    [
        "editor_scene_desk",
        "editor_scene",
        "cdp_editor_scene",
        "editor_desk",
        "editor"
    ];
}
