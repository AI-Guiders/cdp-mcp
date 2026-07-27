#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeRefactorPlanChannel
{
    static object BuildPartials(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var path = Opt(args, "path") ?? Opt(args, "file_path");
        var topic = Opt(args, "topic") ?? Opt(args, "seam") ?? Opt(args, "name");
        if (path is null or { Length: 0 })
        {
            return new
            {
                ok = false,
                error = "path_required",
                pulse = "refactor_plan · partials · need path",
                hint = "path=IdeFoo.cs → list IdeFoo.*.cs seams"
            };
        }

        var full = ResolvePath(session, path);
        var dir = Path.GetDirectoryName(full) ?? "";
        var file = Path.GetFileName(full);
        var stem = file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            ? file[..^3]
            : file;

        // IdeFoo.Bar.cs → stem for siblings is IdeFoo (first segment before topic)
        // IdeFoo.cs → stem IdeFoo
        var typeStem = stem.Contains('.') ? stem.Split('.')[0] : stem;
        var siblings = new List<object>();
        try
        {
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.EnumerateFiles(dir, typeStem + ".*.cs")
                             .Concat(Directory.EnumerateFiles(dir, typeStem + ".cs"))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileName(f);
                    var topicPart = name.Equals(typeStem + ".cs", StringComparison.OrdinalIgnoreCase)
                        ? "(root)"
                        : name[(typeStem.Length + 1)..^3];
                    siblings.Add(new
                    {
                        path = f,
                        rel = Rel(session.ProjectRoot, f),
                        topic = topicPart,
                        lines = QuietLineCount(f),
                        is_locus = f.Equals(full, StringComparison.OrdinalIgnoreCase)
                    });
                }
            }
        }
        catch { /* ignore */ }

        string? suggested = null;
        if (topic is { Length: > 0 })
        {
            var safe = new string(topic.Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-').ToArray());
            if (safe.Length > 0)
                suggested = Path.Combine(dir, $"{typeStem}.{safe}.cs");
        }

        var pulse = siblings.Count == 0
            ? $"refactor_plan · partials · {typeStem} · no siblings yet"
            : $"refactor_plan · partials · {typeStem} · seams={siblings.Count}";

        return new
        {
            ok = true,
            pulse,
            type_stem = typeStem,
            locus = Rel(session.ProjectRoot, full),
            siblings,
            suggested_path = suggested,
            suggested_rel = suggested is null ? null : Rel(session.ProjectRoot, suggested),
            convention = "TypeName.Topic.cs — keep partial class name = type_stem",
            hint = suggested is null
                ? "topic=Ops|View|Persist to suggest next seam path"
                : "create via cdp_buffer op=create path=suggested — move members with sniper"
        };
    }
}
