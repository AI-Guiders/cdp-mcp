#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=arch_desk</c> / Meta <c>cdp_arch</c> — architecture staging board (ADR 0196).
/// Roles (CCU/Channel/CDS/…) + candidates with anchors — on the board, not in code until promote.
/// Not a Miro whiteboard: ontological kneeboard for wire-ready CIDE parity.
/// </summary>
internal static partial class IdeArchBoardChannel
{
    public const string SchemaVersion = "arch_board/v0";
    public const string ToolName = "cdp_arch";
    public const string GoName = "arch_desk";

    public static readonly string[] RoleLexicon =
    [
        "ccu", "channel", "cds", "ids", "compositor", "surface",
        "instrument", "dal", "transport"
    ];

    public static readonly string[] EdgeKinds =
    [
        "feeds", "mounts", "projects", "wires"
    ];

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
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
        args = FlattenGoArgs(args);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "add_role" or "role" or "add" => AddRole(session, args),
            "add_candidates" or "candidates" or "candidate" => AddCandidates(session, args),
            "elect" or "bind" or "choose" => Elect(session, args),
            "reject" or "drop_candidate" => Reject(session, args),
            "edge" or "wire" or "link" => AddEdge(session, args),
            "promote" => Promote(session, args),
            "clear" => Clear(session),
            "as_built" or "asbuilt" or "built" or "scan" => AsBuilt(session, args),
            "roles" or "lexicon" => new
            {
                ok = true,
                schema = SchemaVersion,
                go = GoName,
                tool = ToolName,
                roles = RoleLexicon,
                edge_kinds = EdgeKinds,
                hint = "add_role role=ccu · as_built — scan open project · add_candidates anchors=[F:;M:]"
            },
            _ => Scene(session, args)
        };
    }
}
