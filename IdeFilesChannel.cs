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
        return Ops.TryGetValue(op, out var handler)
            ? handler(store, session, args)
            : Scene(session, args);
    }

    /// <summary>Operation registry — LRC-style dispatch: new verbs register here, no branch editing (ADR-0062 parity).</summary>
    static readonly IReadOnlyDictionary<string, Func<DocumentBufferStore, SessionContext, IReadOnlyDictionary<string, JsonElement>, object>> Ops =
        new Dictionary<string, Func<DocumentBufferStore, SessionContext, IReadOnlyDictionary<string, JsonElement>, object>>(StringComparer.OrdinalIgnoreCase)
        {
            ["list"] = (s, c, a) => List(c, a),
            ["ls"] = (s, c, a) => List(c, a),
            ["dir"] = (s, c, a) => List(c, a),
            ["cd"] = (s, c, a) => Cd(c, a),
            ["chdir"] = (s, c, a) => Cd(c, a),
            ["up"] = (s, c, a) => Up(c),
            [".."] = (s, c, a) => Up(c),
            ["cdup"] = (s, c, a) => Up(c),
            ["stat"] = (s, c, a) => Stat(c, a),
            ["info"] = (s, c, a) => Stat(c, a),
            ["tree"] = (s, c, a) => Tree(c, a),
            ["open"] = OpenFile,
            ["text"] = (s, c, a) => TextProject(c, a),
            ["dump"] = (s, c, a) => TextProject(c, a),
            ["read"] = (s, c, a) => TextProject(c, a),
            ["search"] = (s, c, a) => SearchFacet(c, a),
            ["find"] = (s, c, a) => SearchFacet(c, a),
            ["roots"] = (s, c, a) => Roots(c),
            ["new"] = (s, c, a) => New(c, a),
            ["mkdir"] = (s, c, a) => Mkdir(c, a),
            ["delete"] = (s, c, a) => Delete(c, a),
            ["rm"] = (s, c, a) => Delete(c, a),
            ["rename"] = (s, c, a) => Rename(c, a),
            ["move"] = (s, c, a) => Move(c, a),
            ["copy"] = (s, c, a) => Copy(c, a),
            ["clip_copy"] = (s, c, a) => ClipSet(c, a, cut: false),
            ["clip_cut"] = (s, c, a) => ClipSet(c, a, cut: true),
            ["clip_clear"] = (s, c, a) => ClipClear(),
            ["clip_show"] = (s, c, a) => ClipShow(),
            ["paste"] = (s, c, a) => ClipPaste(c, a),
            ["clear"] = (s, c, a) => ClearCwd(),
        };

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
