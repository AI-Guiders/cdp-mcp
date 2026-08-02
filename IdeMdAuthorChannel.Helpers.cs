#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;
internal static partial class IdeMdAuthorChannel
{
    static object Expand(SessionContext session, IReadOnlyDictionary<string, JsonElement> args, bool write)
    {
        if (!TryLoad(session, args, out var path, out var raw, out var fail))
            return fail!;
        var opts = ParseOptions(args);
        var result = MarkdownIncludeExpansion.TryExpand(raw, path, opts);
        if (!result.Ok)
        {
            return new
            {
                schema = SchemaVersion,
                ok = false,
                op = write ? "export" : "expand",
                path,
                scope = WireScope(opts.Scope),
                include_hits = result.IncludeHits,
                errors = result.Errors,
                hint = "Expansion failed — fix INCLUDE paths/cycles."
            };
        }

        string? outPath = null;
        if (write)
        {
            outPath = Opt(args, "out") ?? Opt(args, "output") ?? Opt(args, "to");
            outPath = string.IsNullOrWhiteSpace(outPath) ? MarkdownIncludeExpansion.DefaultExportPath(path) : ResolvePath(session, outPath!);
            try
            {
                var dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, result.Expanded);
            }
            catch (Exception ex)
            {
                return Fail("write_failed", ex.Message);
            }
        }

        var maxChars = OptInt(args, "max_chars") ?? 12_000;
        var body = result.Expanded;
        var truncated = body.Length > maxChars;
        if (truncated)
            body = body[..maxChars] + "\n…[truncated]";
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = write ? "export" : "expand",
            path,
            out_path = outPath,
            scope = WireScope(opts.Scope),
            include_hits = result.IncludeHits,
            char_count = result.Expanded.Length,
            truncated,
            markdown = body,
            hint = write ? $"Wrote portable Markdown → {outPath}" : "Expanded in-memory. op=export to write .expanded.md."
        };
    }

    static bool TryLoad(SessionContext session, IReadOnlyDictionary<string, JsonElement> args, out string path, out string raw, out object? fail)
    {
        path = "";
        raw = "";
        fail = null;
        var pathArg = Opt(args, "path") ?? Opt(args, "file") ?? Opt(args, "src");
        if (string.IsNullOrWhiteSpace(pathArg))
        {
            fail = Fail("path_required", "path= to .md (absolute or relative to project_root)");
            return false;
        }

        path = ResolvePath(session, pathArg!);
        if (!File.Exists(path))
        {
            fail = Fail("not_found", path);
            return false;
        }

        try
        {
            raw = File.ReadAllText(path);
            return true;
        }
        catch (Exception ex)
        {
            fail = Fail("read_failed", ex.Message);
            return false;
        }
    }

    static MarkdownIncludeExpansion.Options ParseOptions(IReadOnlyDictionary<string, JsonElement> args)
    {
        var scopeRaw = (Opt(args, "scope") ?? "all").Trim().ToLowerInvariant();
        var scope = scopeRaw is "fence" or "fenced" or "cide" ? MarkdownIncludeExpansion.IncludeScope.Fence : MarkdownIncludeExpansion.IncludeScope.All;
        var maxDepth = OptInt(args, "max_depth") ?? 5;
        if (maxDepth < 1)
            maxDepth = 1;
        if (maxDepth > 32)
            maxDepth = 32;
        return new MarkdownIncludeExpansion.Options(maxDepth, scope);
    }

    static string WireScope(MarkdownIncludeExpansion.IncludeScope scope) => scope == MarkdownIncludeExpansion.IncludeScope.Fence ? "fence" : "all";
    static string ResolvePath(SessionContext session, string pathArg)
    {
        if (Path.IsPathRooted(pathArg))
            return Path.GetFullPath(pathArg);
        var root = session.ProjectRoot is { Length: > 0 } pr ? pr : Environment.CurrentDirectory;
        return Path.GetFullPath(Path.Combine(root, pathArg));
    }

    static object Fail(string reason, string detail) => new
    {
        schema = SchemaVersion,
        ok = false,
        go = GoName,
        tool = ToolName,
        reason,
        detail,
        hint = "op=scene|check|expand|export · path= · scope=all|fence"
    };
    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) => args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    static int? OptInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            return n;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out n))
            return n;
        return null;
    }
}