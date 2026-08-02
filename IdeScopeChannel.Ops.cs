#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cdp.Core;

namespace CdpMcp;
internal static partial class IdeScopeChannel
{
    static object Set(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var text = Opt(args, "text") ?? Opt(args, "message") ?? Opt(args, "body") ?? "";
        TryParseMarkers(text, out var markerPrimary, out var markerScope);
        var primary = FirstNonEmpty(Opt(args, "primary"), Opt(args, "project"), Opt(args, "project_id"), markerPrimary);
        var scope = FirstNonEmpty(Opt(args, "scope"), Opt(args, "active_scope"), Opt(args, "slice"), markerScope);
        primary = NormalizeId(primary);
        scope = NormalizeId(scope);
        if (string.IsNullOrWhiteSpace(primary) && string.IsNullOrWhiteSpace(scope))
        {
            return Fail("need_primary_or_scope", "op=set primary= and/or scope= — or text with [PRIMARY:…] / [SCOPE:…]");
        }

        var prev = Load();
        var doc = new ScopeDoc
        {
            Schema = SchemaVersion,
            Primary = string.IsNullOrWhiteSpace(primary) ? prev?.Primary : primary,
            Scope = string.IsNullOrWhiteSpace(scope) ? prev?.Scope : scope,
            SetUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ProjectRoot = session.ProjectRoot,
            Source = !string.IsNullOrWhiteSpace(text) && (markerPrimary is not null || markerScope is not null) ? "markers" : "args"
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
                new
                {
                    go = "learn",
                    label = "Stash learning",
                    why = "inherits PRIMARY/SCOPE"
                },
                new
                {
                    go = "project_switch",
                    label = "Recall",
                    why = "op=recall"
                }
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
        hint = CurrentOrNull()is null ? "Empty — op=set primary= scope=" : "Pass active_scope= to AN tools when slice must differ from path default."
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

    static string? FirstNonEmpty(params string? [] values)
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
            _ => el.ToString()};
    }
}