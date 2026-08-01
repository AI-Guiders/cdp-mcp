using System.Text.Json;
using Cdp.Core;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>KeySpec catalog, Effective* readers, ApplyHot for IdeSettingsHabitat.</summary>
internal sealed partial class IdeSettingsHabitat
{
    bool ApplyHot(string key, string value)
    {
        if (key.Equals("session.default_phase", StringComparison.OrdinalIgnoreCase)
            && CdpEnumParse.TryParsePhase(value, out var phase))
        {
            _session.Phase = phase;
            return true;
        }

        if (key.Equals("session.default_object", StringComparison.OrdinalIgnoreCase)
            && CdpEnumParse.TryParseObject(value, out var obj))
        {
            _session.Object = obj;
            return true;
        }

        return key.StartsWith("browser.", StringComparison.OrdinalIgnoreCase)
               || key.StartsWith("desk.", StringComparison.OrdinalIgnoreCase)
               || key.StartsWith("shell.", StringComparison.OrdinalIgnoreCase)
               || key.StartsWith("mcp.", StringComparison.OrdinalIgnoreCase);
    }

    static string ResolveEffective(KeySpec spec, string? user)
    {
        if (!string.IsNullOrWhiteSpace(user)) return user!;
        if (!string.IsNullOrWhiteSpace(spec.ProcessValue)) return spec.ProcessValue!;
        return spec.Default ?? "";
    }

    static (string? Value, string? Error) NormalizeValue(KeySpec spec, string raw)
    {
        raw = raw.Trim();
        if (spec.Choices is { Length: > 0 })
        {
            var hit = spec.Choices.FirstOrDefault(c => c.Equals(raw, StringComparison.OrdinalIgnoreCase));
            if (hit is null)
                return (null, $"value one of: {string.Join("|", spec.Choices)}");
            // Normalize known aliases for search engine
            if (spec.Key.Equals("browser.search_engine", StringComparison.OrdinalIgnoreCase))
            {
                return raw.ToLowerInvariant() switch
                {
                    "duck" or "duckduckgo" => ("ddg", null),
                    "g" => ("google", null),
                    _ => (hit.ToLowerInvariant(), null)
                };
            }

            return (hit, null);
        }

        if (spec.Control is "int" or "number")
        {
            if (!int.TryParse(raw, out var n))
                return (null, "integer required");
            n = spec.Key switch
            {
                "browser.width" => Math.Clamp(n, 40, 200),
                "browser.timeout_seconds" => Math.Clamp(n, 5, 120),
                "browser.dump_chars" => Math.Clamp(n, 1000, 100_000),
                "shell.timeout_seconds" => Math.Clamp(n, 1, 600),
                "shell.codepage" => Math.Clamp(n, 1, 65_535),
                _ => n
            };
            return (n.ToString(), null);
        }

        if (spec.Key.StartsWith("session.default_", StringComparison.OrdinalIgnoreCase))
            return (raw.Trim().ToLowerInvariant(), null);

        return (raw, null);
    }

    public static string EffectiveUserAgent()
    {
        foreach (var key in new[] { "CDP_BROWSER_UA", "CDP_LYNX_UA", "LYNX_USER_AGENT" })
        {
            var env = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim();
        }

        return IdeSettingsStore.GetOrNull("browser.user_agent")
               ?? InternetBrowserHabitat.DefaultUserAgent;
    }

    public static string EffectiveSearchEngine() =>
        IdeSettingsStore.GetOrNull("browser.search_engine")
        ?? InternetBrowserHabitat.DefaultSearchEngine;

    public static int EffectiveWidth() =>
        IdeSettingsStore.GetInt("browser.width", InternetBrowserHabitat.DefaultWidth)
        ?? InternetBrowserHabitat.DefaultWidth;

    public static int EffectiveTimeout() =>
        IdeSettingsStore.GetInt("browser.timeout_seconds", InternetBrowserHabitat.DefaultTimeoutSeconds)
        ?? InternetBrowserHabitat.DefaultTimeoutSeconds;

    public static int EffectiveDumpChars() =>
        IdeSettingsStore.GetInt("browser.dump_chars", InternetBrowserHabitat.DumpBodyChars)
        ?? InternetBrowserHabitat.DumpBodyChars;

    public static string? EffectiveDeskLayout() =>
        IdeSettingsStore.GetOrNull("desk.default_layout");

    public static bool EffectiveDeskLayoutHold()
    {
        var v = IdeSettingsStore.GetOrNull("desk.layout.hold");
        return v is not null && bool.TryParse(v, out var b) && b;
    }

    public static string EffectiveDeskMfd() =>
        IdeSettingsStore.GetOrNull("desk.default_mfd") ?? "nav";

    public static string EffectiveDeskMode() =>
        IdeSettingsStore.GetOrNull("desk.mode") ?? "seats";

    /// <summary>Cold default organ for a seat id (p|forward|m); empty string clears.</summary>
    public static string? EffectiveSeatDefault(string seatId)
    {
        var key = seatId.ToLowerInvariant() switch
        {
            "p" => "desk.seat.p",
            "forward" => "desk.seat.forward",
            "m" => "desk.seat.m",
            _ => null
        };
        if (key is null) return null;
        var v = IdeSettingsStore.GetOrNull(key);
        if (v is null)
        {
            return seatId.ToLowerInvariant() switch
            {
                "p" => "project_scene",
                "forward" => "editor_scene",
                "m" => "browser",
                _ => null
            };
        }

        return string.IsNullOrWhiteSpace(v) || v.Equals("empty", StringComparison.OrdinalIgnoreCase)
            || v.Equals("-", StringComparison.OrdinalIgnoreCase)
            ? null
            : v.Trim();
    }

    public static int EffectiveShellTimeout() =>
        IdeSettingsStore.GetInt("shell.timeout_seconds", ShellHabitat.DefaultTimeoutSeconds)
        ?? ShellHabitat.DefaultTimeoutSeconds;

    public static int EffectiveShellCodepage() =>
        IdeSettingsStore.GetInt("shell.codepage", 65001) ?? 65001;

    public static string? EffectiveMcpDefaultPreset() =>
        IdeSettingsStore.GetOrNull("mcp.default_preset");

    static string NormalizeKey(string key) =>
        key.Trim().Replace('/', '.').Replace('\\', '.').ToLowerInvariant();

    static string BoolStr(bool v) => v ? "true" : "false";

    static string Trunc(string s, int n) =>
        s.Length <= n ? s : s[..(n - 1)] + "…";

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number => el.ToString(),
            _ => null
        };
    }

    static bool Bool(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && (
            el.ValueKind == JsonValueKind.True
            || (el.ValueKind == JsonValueKind.String
                && bool.TryParse(el.GetString(), out var b) && b)
            || (el.ValueKind == JsonValueKind.String
                && el.GetString() is "1" or "yes" or "on"));

    static string Fail(string reason, string hint) =>
        JsonSerializer.Serialize(new { schema = Schema, ok = false, reason, hint }, Pretty);

    sealed record KeySpec(
        string Key,
        string Page,
        string Section,
        string Title,
        string Control,
        string[]? Choices,
        string Layer,
        bool Writable,
        bool Hot,
        bool RestartRequired,
        string Description,
        string? Default,
        string? ProcessValue);

    public readonly record struct SettingsPulse(
        bool Ok,
        string Line,
        int UserCount,
        string UserPath,
        string ProcessPath);
}