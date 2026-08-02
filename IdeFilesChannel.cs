#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=files_desk</c> / Meta <c>cdp_files</c> — agent-native File Manager (ADR-0016).
/// Utility (not project-bound): <c>where=project|external|cwd</c> parity with search.
/// </summary>
internal static partial class IdeFilesChannel
{
    public const string SchemaVersion = "files/v1";
    public const string ToolName = "cdp_files";
    public const string CwdKey = "files.cwd";
    public const int SlimCap = 24;
    public const int ListCap = 120;
    public const int TreeDefaultDepth = 2;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string HandleJson(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args) =>
        JsonSerializer.Serialize(Handle(store, session, args), Pretty);

    public static object Handle(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        args = FlattenGoArgs(args);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "list" or "ls" or "dir" => List(session, args),
            "cd" or "chdir" => Cd(session, args),
            "up" or ".." or "cdup" => Up(session),
            "stat" or "info" => Stat(session, args),
            "tree" => Tree(session, args),
            "open" => OpenFile(store, session, args),
            "text" or "dump" or "read" => TextProject(session, args),
            "search" or "find" => SearchFacet(session, args),
            "roots" => Roots(session),
            "clear" => ClearCwd(),
            _ => Scene(session, args)
        };
    }

    static object Scene(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var cwd = ResolveCwd(session, args, out var where);
        var entries = Enumerate(cwd, args, SlimCap, out var total, out var truncated);
        return Board(
            op: "scene",
            where: where,
            cwd: cwd,
            shape: "slim",
            entries: entries,
            total: total,
            truncated: truncated,
            hint: "Utility FM — where=project|external|cwd. Prefer over shell ls. op=list|cd|up|stat|tree|open|text|search.");
    }


}
