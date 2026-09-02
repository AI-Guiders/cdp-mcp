#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// Meta <c>cdp_peek</c> — read-only file eyes (ADR-0201). Disk ingress without buffer lifecycle.
/// Prefer over host Read in CDP habitat; anchors chain to sniper/edit.
/// </summary>
internal static partial class CdpPeekChannel
{
    public const string SchemaVersion = "cdp_peek/v1";
    public const string ToolName = "cdp_peek";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string HandleJson(
        SessionContext session,
        LanguageRegistry langs,
        DocumentBufferStore? store,
        IReadOnlyDictionary<string, JsonElement> args) =>
        JsonSerializer.Serialize(Handle(session, langs, store, args), Pretty);

    public static object Handle(
        SessionContext session,
        LanguageRegistry langs,
        DocumentBufferStore? store,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        var query = Opt(args, "query") ?? Opt(args, "pattern") ?? Opt(args, "q");
        if (query is { Length: > 0 })
            return FindAndPeek(session, langs, store, args, query);

        if (TryReadPathList(args, out var paths) && paths.Count > 0)
            return PeekBatch(session, langs, args, paths);

        var path = Opt(args, "path") ?? Opt(args, "file");
        var wire = Opt(args, "anchor") ?? Opt(args, "at");
        if (path is null && wire is { Length: > 0 })
        {
            if (!TryResolveAnchorPath(session, wire, out path, out var anchorErr))
            {
                return Fail("anchor_unresolved", anchorErr ?? "bad anchor wire",
                    "Pass path= or anchor with F: file label resolvable under ProjectRoot.");
            }
        }

        if (path is null)
        {
            return Fail("path_required",
                "Provide path=, paths[], anchor=, or query= (rg + peek windows).",
                "Explore: cdp_peek path=src/Foo.cs · batch paths[]= · land anchor= · find query= glob=*.cs");
        }

        var bind = BoolOr(args, "bind", defaultValue: true);
        var abs = ResolvePath(session, langs, path, Opt(args, "scope"), bind, out var bindNote, out var resolveErr);
        if (abs is null)
            return Fail(resolveErr ?? "path_invalid", $"Could not resolve path={path}", bindNote ?? "cdp_open or scope=external path= absolute.");

        if (string.Equals(Opt(args, "mode"), "outline", StringComparison.OrdinalIgnoreCase))
            return PeekOutline(session, abs, args, bindNote);

        return PeekFile(session, abs, args, bindNote);
    }

    static object Fail(string error, string detail, string hint) => new
    {
        schema = SchemaVersion,
        ok = false,
        tool = ToolName,
        error,
        detail,
        hint
    };
}
