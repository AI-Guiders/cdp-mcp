#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdePluginsChannel
{
    static object DoPreview(
        DocumentBufferStore store,
        SessionContext session,
        Dictionary<string, JsonElement> merged,
        CancellationToken cancellationToken)
    {
        var path = Opt(merged, "path") ?? Opt(merged, "file");
        string body;
        string? sourcePath;

        if (path is { Length: > 0 })
        {
            path = Path.GetFullPath(path);
            if (!File.Exists(path))
                return new { ok = false, op = "preview", error = "path_not_found", path };
            body = File.ReadAllText(path);
            sourcePath = path;
        }
        else
        {
            var buf = PickPlantBuffer(store, merged);
            if (buf is null)
            {
                return new
                {
                    ok = false,
                    op = "preview",
                    error = "no_plantuml_buffer",
                    hint = "Open/create .puml buffer, or path= to diagram."
                };
            }

            body = buf.Text;
            sourcePath = buf.Path;
        }

        if (!PlantUmlRender.LooksLikePlantUml(body, sourcePath, fence: null))
            return new { ok = false, op = "preview", error = "not_plantuml", path = sourcePath };

        var rendered = PlantUmlRender.RenderPng(body, cancellationToken);
        if (!rendered.Ok || rendered.Png is not { Length: > 0 } png)
        {
            return new
            {
                ok = false,
                op = "preview",
                error = rendered.Error ?? "render_failed",
                jar = rendered.Jar,
                path = sourcePath
            };
        }

        var previewPath = TryWritePreviewPng(sourcePath, session.ProjectRoot, png);
        return new
        {
            ok = true,
            op = "preview",
            kind = "plantuml_png",
            bytes = png.Length,
            mime = "image/png",
            preview_path = previewPath,
            jar = rendered.Jar,
            source = sourcePath,
            note = previewPath is null
                ? "PNG rendered but write failed"
                : "PNG on disk — Read preview_path or take with vision=true"
        };
    }

    static DocBuffer? PickPlantBuffer(DocumentBufferStore store, Dictionary<string, JsonElement> merged)
    {
        var docId = Opt(merged, "doc_id");
        if (docId is { Length: > 0 })
        {
            var hit = store.All.FirstOrDefault(b =>
                string.Equals(b.DocId, docId, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return hit;
        }

        return store.All
            .OrderByDescending(b => b.Version)
            .FirstOrDefault(b => PlantUmlRender.LooksLikePlantUml(b.Text, b.Path, fence: null));
    }

    static string? TryWritePreviewPng(string? sourcePath, string? projectRoot, byte[] png)
    {
        try
        {
            string dir;
            string name;
            if (sourcePath is { Length: > 0 })
            {
                dir = Path.GetDirectoryName(sourcePath) ?? projectRoot ?? Path.GetTempPath();
                name = Path.GetFileNameWithoutExtension(sourcePath) + ".png";
            }
            else
            {
                dir = projectRoot is { Length: > 0 }
                    ? Path.Combine(projectRoot, ".cdp", "evidence", "plugins")
                    : Path.Combine(Path.GetTempPath(), "cdp-plugins");
                name = "preview.png";
            }

            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name);
            File.WriteAllBytes(path, png);
            return path;
        }
        catch
        {
            return null;
        }
    }

    static bool ActionOk(object? action)
    {
        if (action is null)
            return true;
        try
        {
            var json = JsonSerializer.Serialize(action);
            using var doc = JsonDocument.Parse(json);
            return !doc.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.False;
        }
        catch
        {
            return true;
        }
    }

    static Dictionary<string, JsonElement> FlattenArgs(IReadOnlyDictionary<string, JsonElement>? args)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (args is null)
            return merged;

        foreach (var kv in args)
        {
            if (kv.Key is "go_args" && kv.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in kv.Value.EnumerateObject())
                    merged[p.Name] = p.Value.Clone();
                continue;
            }

            merged[kv.Key] = kv.Value.Clone();
        }

        return merged;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.ToString(),
            _ => null
        };
    }

    static int IntOr(IReadOnlyDictionary<string, JsonElement> args, string key, int fallback)
    {
        if (!args.TryGetValue(key, out var el))
            return fallback;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(el.GetString(), out var n) => n,
            _ => fallback
        };
    }

    static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
