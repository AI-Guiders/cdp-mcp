#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=find_desk</c> / Meta <c>cdp_search</c> — agent-native search (ADR-0009).
/// Axes: what / where / shape. Text engine = FindInFiles + buffer find.
/// </summary>
internal static partial class IdeFindChannel
{
    public const string SchemaVersion = "find_organ/v1";
    public const string ToolName = "cdp_search";
    public const string LastKey = "find.last";
    public const int SlimHitCap = 1;
    public const int ListHitCap = 40;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

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
        var op = (Opt(args, "op") ?? "run").Trim().ToLowerInvariant();

        return op switch
        {
            "last" => LastCard(),
            "clear" => ClearCard(),
            "refine" => Run(store, session, MergeRefine(args), shapeOverride: null),
            "run" or "search" or "find" => Run(store, session, args, shapeOverride: null),
            _ => new
            {
                ok = false,
                schema = SchemaVersion,
                role = "find",
                go = "find_desk",
                error = "unknown_op",
                hint = "op=run|refine|last|clear"
            }
        };
    }

    static object Run(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        string? shapeOverride)
    {
        var what = (Opt(args, "what") ?? "text").Trim().ToLowerInvariant();
        var where = (Opt(args, "where") ?? Opt(args, "scope") ?? "project").Trim().ToLowerInvariant();
        var shape = (shapeOverride ?? Opt(args, "shape") ?? "slim").Trim().ToLowerInvariant();
        if (shape is not ("slim" or "list" or "raw"))
            shape = "slim";

        if (what is "index" or "fts" or "semantic")
            return StubIndex(where, shape);
        if (what is "symbol" or "symbols" or "ide")
            return StubSymbol(where, shape);

        if (what is not ("text" or "rg" or "grep" or "literal"))
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                role = "find",
                go = "find_desk",
                what,
                where,
                shape,
                error = "unknown_what",
                hint = "what=text (default) | index | symbol"
            };
        }

        var query = Opt(args, "query") ?? Opt(args, "text") ?? Opt(args, "pattern") ?? Opt(args, "q");
        if (string.IsNullOrEmpty(query))
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                role = "find",
                go = "find_desk",
                what,
                where,
                shape,
                error = "query_required",
                hint = "query= + where=project|external|dirty|buffers|buffer. shape=slim|list|raw."
            };
        }

        if (!TryBuildFindArgs(store, session, args, where, query!, shape, out var findArgs, out var whereWire, out var pathNote, out var err, out var hint))
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                role = "find",
                go = "find_desk",
                what,
                where = whereWire,
                shape,
                error = err,
                hint
            };
        }

        SaveLast(what, whereWire, shape, query!, findArgs, pathNote);

        var all = shape is "list" or "raw";
        var rawJson = FindInFiles.IsFilesScope(whereWire) || whereWire is "dirty" or "buffers" or "roots"
            ? FindInFiles.Dispatch(store, session, findArgs, all)
            : EditorComfort.Dispatch(store, session, all ? "find_all" : "find", findArgs);

        return ShapeResult(rawJson, what, whereWire, shape, query!, pathNote);
    }
}
