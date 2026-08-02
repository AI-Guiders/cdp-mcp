#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=project_switch</c> / Meta <c>cdp_scope</c> — AN Project Switch (PRIMARY/SCOPE) latch on desk.
/// Sticky under workspace state. Learn stash inherits. Note: <c>go=scope</c> remains EditSniper — use <c>go=ps</c>.
/// Ops: scene|set|recall|clear.
/// </summary>
internal static partial class IdeScopeChannel
{
    public const string SchemaVersion = "scope_channel/v0";
    public const string ToolName = "cdp_scope";
    public const string GoName = "project_switch";

    static readonly Regex PrimaryMarker = new(
        @"\[\[?PRIMARY\s*:\s*([^\]\s]+)\]?\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex ScopeMarker = new(
        @"\[\[?SCOPE\s*:\s*([^\]\s]+)\]?\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static readonly object Gate = new();

    public static string FilePath => Path.Combine(CdpProfile.StateRoot, "scope-latch.json");

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args), new JsonSerializerOptions { WriteIndented = true });

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "scene" or "help" or "status" => Scene(session),
            "set" or "latch" or "switch" or "arm" => Set(session, args),
            "recall" or "get" or "peek" or "load" => Recall(),
            "clear" or "reset" or "disarm" => Clear(),
            _ => Fail("unknown_op", "op=scene|set|recall|clear — go=project_switch|ps (not go=scope=sniper)")
        };
    }

    public static void PublishGlass()
    {
        var latch = CurrentOrNull();
        var active = latch is not null;
        CideScopeLatch.Publish(
            active,
            PulseLine(),
            latch?.Primary,
            latch?.Scope);
    }

    public static string PulseLine(SessionContext? session = null)
    {
        _ = session;
        var doc = Load();
        if (doc is null || (string.IsNullOrWhiteSpace(doc.Primary) && string.IsNullOrWhiteSpace(doc.Scope)))
            return "ps · idle · go=project_switch";
        var p = string.IsNullOrWhiteSpace(doc.Primary) ? "—" : doc.Primary;
        var s = string.IsNullOrWhiteSpace(doc.Scope) ? "—" : doc.Scope;
        return $"ps · PRIMARY={p} · SCOPE={s}";
    }

    /// <summary>Current latch for learn / other organs (null if empty).</summary>
    public static ScopeLatch? CurrentOrNull()
    {
        var doc = Load();
        if (doc is null)
            return null;
        if (string.IsNullOrWhiteSpace(doc.Primary) && string.IsNullOrWhiteSpace(doc.Scope))
            return null;
        return new ScopeLatch(doc.Primary, doc.Scope, doc.SetUtc);
    }

    static object Scene(SessionContext session)
    {
        var doc = Load();
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "scene",
            go = GoName,
            tool = ToolName,
            pulse = PulseLine(session),
            primary = doc?.Primary,
            scope = doc?.Scope,
            set_utc = doc?.SetUtc,
            latch_path = FilePath,
            ops = new[] { "scene", "set", "recall", "clear" },
            markers = new[] { "[PRIMARY:project-id]", "[SCOPE:slice]" },
            conflict_note = "go=scope → EditSniper. This organ: go=project_switch|ps|cdp_scope.",
            next = new object[]
            {
                new { go = "project_switch", label = "Set", why = "op=set primary= scope=  or text=[PRIMARY:…][SCOPE:…]" },
                new { go = "project_switch", label = "Recall", why = "op=recall" },
                new { go = "learn", label = "Learn", why = "stash inherits PRIMARY/SCOPE" }
            },
            hint =
                "AN Project Switch on desk: one PRIMARY + one SCOPE latch. " +
                "Explicit markers beat path heuristics. Learn stash picks them up."
        };
    }


    public sealed record ScopeLatch(string? Primary, string? Scope, string? SetUtc);
}