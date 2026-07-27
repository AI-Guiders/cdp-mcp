#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=crm</c> / Meta <c>cdp_crm</c> — CRM callout panel (ADR-0014).
/// Closed codes: approved|stabilized|go_around|hold|unable|negative|say_again|continue|roger|wilco.
/// Partials: Ops (scene/call/respond), View (pulse/next), Persist (inbox), Models (snap/norm).
/// </summary>
internal static partial class IdeCrmChannel
{
    public const string SchemaVersion = "crm/v1";
    public const string ToolName = "cdp_crm";
    public const string Awaiting = "awaiting";

    public static readonly string[] Lexicon =
    [
        "approved", "stabilized", "go_around", "hold", "unable",
        "negative", "say_again", "continue", "roger", "wilco"
    ];

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };


    public static string HandleJson(
        SessionContext session,
        IntentWorkspaceStore? store,
        IntentWorkspaceState? state,
        IReadOnlyDictionary<string, JsonElement>? args) =>
        JsonSerializer.Serialize(Handle(session, store, state, args), Pretty);

    public static object Handle(
        SessionContext session,
        IntentWorkspaceStore? store = null,
        IntentWorkspaceState? state = null,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        args = FlattenGoArgs(args);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "call" or "ask" or "open" => Call(session, args),
            "respond" or "reply" or "say" => Respond(session, store, state, args),
            "last" => Last(session),
            "clear" => Clear(session),
            "lexicon" => new { ok = true, schema = SchemaVersion, go = "crm", lexicon = Lexicon },
            _ => Scene(session)
        };
    }
}
