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

    static object Set(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var text = Opt(args, "text") ?? Opt(args, "message") ?? Opt(args, "body") ?? "";
        TryParseMarkers(text, out var markerPrimary, out var markerScope);

        var primary = FirstNonEmpty(
            Opt(args, "primary"),
            Opt(args, "project"),
            Opt(args, "project_id"),
            markerPrimary);
        var scope = FirstNonEmpty(
            Opt(args, "scope"),
            Opt(args, "active_scope"),
            Opt(args, "slice"),
            markerScope);

        primary = NormalizeId(primary);
        scope = NormalizeId(scope);

        if (string.IsNullOrWhiteSpace(primary) && string.IsNullOrWhiteSpace(scope))
        {
            return Fail(
                "need_primary_or_scope",
                "op=set primary= and/or scope= — or text with [PRIMARY:…] / [SCOPE:…]");
        }

        var prev = Load();
        var doc = new ScopeDoc
        {
            Schema = SchemaVersion,
            Primary = string.IsNullOrWhiteSpace(primary) ? prev?.Primary : primary,
            Scope = string.IsNullOrWhiteSpace(scope) ? prev?.Scope : scope,
            SetUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ProjectRoot = session.ProjectRoot,
            Source = !string.IsNullOrWhiteSpace(text) && (markerPrimary is not null || markerScope is not null)
                ? "markers"
                : "args"
        };

        // Allow clearing one axis with empty string only when explicitly passed as "" via primary=/scope= —
        // already handled: blank keeps previous. Explicit clear uses op=clear.

        Save(doc);
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "set",
            go = GoName,
            primary = doc.Primary,
            scope = doc.Scope,
            set_utc = doc.SetUtc,
            source = doc.Source,
            pulse = PulseLine(session),
            latch_path = FilePath,
            next = new object[]
            {
                new { go = "learn", label = "Stash learning", why = "inherits PRIMARY/SCOPE" },
                new { go = "project_switch", label = "Recall", why = "op=recall" }
            },
            hint = "Latched. Subsequent learn stash inherits unless overridden."
        };
    }

    static object Recall() => new
    {
        schema = SchemaVersion,
        ok = true,
        op = "recall",
        go = GoName,
        latch = CurrentOrNull(),
        pulse = PulseLine(),
        latch_path = FilePath,
        hint = CurrentOrNull() is null
            ? "Empty — op=set primary= scope="
            : "Pass active_scope= to AN tools when slice must differ from path default."
    };

    static object Clear()
    {
        lock (Gate)
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }

        PublishGlass();

        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "clear",
            go = GoName,
            pulse = PulseLine(),
            hint = "Latch cleared."
        };
    }

    static object Fail(string code, string message) => new
    {
        schema = SchemaVersion,
        ok = false,
        go = GoName,
        tool = ToolName,
        error = code,
        message,
        hint = message
    };

    internal static void TryParseMarkers(string? text, out string? primary, out string? scope)
    {
        primary = null;
        scope = null;
        if (string.IsNullOrWhiteSpace(text))
            return;
        var pm = PrimaryMarker.Match(text);
        if (pm.Success)
            primary = NormalizeId(pm.Groups[1].Value);
        var sm = ScopeMarker.Match(text);
        if (sm.Success)
            scope = NormalizeId(sm.Groups[1].Value);
    }

    static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }
        return null;
    }

    static string? NormalizeId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var s = raw.Trim().Trim('[', ']');
        if (s.Length == 0)
            return null;
        return s;
    }

    

    

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => el.ToString()
        };
    }

    public sealed record ScopeLatch(string? Primary, string? Scope, string? SetUtc);
}