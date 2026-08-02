#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=md_author</c> / Meta <c>cdp_md_author</c> — CIDE-style Markdown INCLUDE authoring.
/// Ops: scene|check|expand|export. Syntax: <c>{{ INCLUDE: relative/path }}</c>.
/// </summary>
internal static partial class IdeMdAuthorChannel
{
    public const string SchemaVersion = "md_author/v0";
    public const string ToolName = "cdp_md_author";
    public const string GoName = "md_author";

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args), Pretty);

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "scene" or "help" or "status" => Scene(),
            "check" or "validate" => Check(session, args),
            "expand" => Expand(session, args, write: false),
            "export" => Expand(session, args, write: true),
            _ => Fail("unknown_op", "op=scene|check|expand|export")
        };
    }

    public static string PulseLine(SessionContext? session = null)
    {
        _ = session;
        return "md_author · INCLUDE expand/export · go=md_author";
    }

    static object Scene() => new
    {
        schema = SchemaVersion,
        ok = true,
        op = "scene",
        go = GoName,
        tool = ToolName,
        syntax = "{{ INCLUDE: relative/path }}",
        scope_default = "all",
        ops = new[] { "scene", "check", "expand", "export" },
        next = new object[]
        {
            new { go = "md_author", label = "Check", why = "op=check path=…" },
            new { go = "md_author", label = "Expand", why = "op=expand path=…" },
            new { go = "md_author", label = "Export", why = "op=export path=… [out=]" },
            new { go = "files_desk", label = "Files", why = "pick md path" }
        },
        hint =
            "CIDE ADR 0023 INCLUDE. Default scope=all (any INCLUDE line); scope=fence = CIDE preview parity. " +
            "export writes {name}.expanded.md beside source (or out=)."
    };

    static object Check(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!TryLoad(session, args, out var path, out var raw, out var fail))
            return fail!;

        var opts = ParseOptions(args);
        var result = MarkdownIncludeExpansion.TryExpand(raw, path, opts);
        return new
        {
            schema = SchemaVersion,
            ok = result.Ok,
            op = "check",
            path,
            scope = WireScope(opts.Scope),
            include_hits = result.IncludeHits,
            error_count = result.Errors.Count,
            errors = result.Errors,
            hint = result.Ok
                ? (result.IncludeHits == 0
                    ? "No INCLUDE directives — already portable."
                    : "Includes resolve. op=expand to see body; op=export to write .expanded.md.")
                : "Fix INCLUDE errors before export."
        };
    }

}
