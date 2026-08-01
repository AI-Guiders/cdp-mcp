#nullable enable
using System.Text.Json;
using Cdp.Core;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>Catalog/Get/Set/Unset/ResetAll/Which/ControlCard for IdeSettingsHabitat.</summary>
internal sealed partial class IdeSettingsHabitat
{
    string Catalog(IReadOnlyDictionary<string, JsonElement> args)
    {
        var section = Opt(args, "section") ?? Opt(args, "group") ?? Opt(args, "page");
        var writableOnly = Bool(args, "writable_only") || Bool(args, "hot_only");
        var specs = Specs(_process, _configPath)
            .Where(s => section is null
                        || s.Page.Equals(section, StringComparison.OrdinalIgnoreCase)
                        || s.Section.Equals(section, StringComparison.OrdinalIgnoreCase))
            .Where(s => !writableOnly || s.Writable)
            .ToList();
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "catalog",
            count = specs.Count,
            keys = specs.Select(ControlCard).ToList(),
            hint = "Prefer op=options → op=page for Tools>Options UX."
        }, Pretty);
    }

    string Get(IReadOnlyDictionary<string, JsonElement> args)
    {
        var key = Opt(args, "key") ?? Opt(args, "name");
        if (string.IsNullOrWhiteSpace(key))
            return Fail("key_required", "key=browser.search_engine");

        key = NormalizeKey(key!);
        var spec = Specs(_process, _configPath).FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (spec is null)
            return Fail("unknown_key", $"Unknown key '{key}'. op=options → page=");

        var userHit = IdeSettingsStore.TryGet(key, out var userVal);
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "get",
            control = ControlCard(spec),
            user = userHit ? userVal : null,
            source = userHit ? "user" : (spec.ProcessValue is not null ? "process" : "default")
        }, Pretty);
    }

    string Set(IReadOnlyDictionary<string, JsonElement> args)
    {
        var key = Opt(args, "key") ?? Opt(args, "name");
        var value = Opt(args, "value") ?? Opt(args, "val") ?? Opt(args, "to");
        if (string.IsNullOrWhiteSpace(key))
            return Fail("key_required", "key=… value=…");
        if (value is null)
            return Fail("value_required", "value=… (string/number/bool as text)");

        key = NormalizeKey(key!);
        var spec = Specs(_process, _configPath).FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (spec is null)
            return Fail("unknown_key", $"Unknown key '{key}'. op=page= first");
        if (!spec.Writable)
        {
            return Fail(
                "read_only",
                $"Key '{key}' is process-layer. Edit {_configPath} then remount MCP.");
        }

        var normalized = NormalizeValue(spec, value);
        if (normalized.Error is { } err)
            return Fail("bad_value", err);

        IdeSettingsStore.Set(key, normalized.Value!);
        var applied = ApplyHot(key, normalized.Value!);

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "set",
            key,
            value = normalized.Value,
            page = spec.Page,
            hot_applied = applied,
            path = IdeSettingsStore.FilePath,
            hint = applied
                ? "Applied now (+ persisted to Options user store)."
                : "Persisted; takes effect on next use."
        }, Pretty);
    }

    string Unset(IReadOnlyDictionary<string, JsonElement> args)
    {
        var key = Opt(args, "key") ?? Opt(args, "name");
        if (string.IsNullOrWhiteSpace(key))
            return Fail("key_required", "key=… or op=reset_all");

        key = NormalizeKey(key!);
        var removed = IdeSettingsStore.Unset(key);
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "unset",
            key,
            removed,
            hint = removed ? "User override dropped — factory/process effective." : "No user override."
        }, Pretty);
    }

    string ResetAll()
    {
        var n = IdeSettingsStore.ClearAll();
        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "reset_all",
            cleared = n,
            path = IdeSettingsStore.FilePath,
            hint = "Options user layer empty. Process toml unchanged."
        }, Pretty);
    }

    string Which() => JsonSerializer.Serialize(new
    {
        schema = Schema,
        ok = true,
        op = "which",
        process_config = _configPath,
        process_exists = File.Exists(_configPath),
        user_prefs = IdeSettingsStore.FilePath,
        user_exists = File.Exists(IdeSettingsStore.FilePath),
        user_count = IdeSettingsStore.SnapshotUser().Count,
        pages = Pages.Select(p => p.Id).ToArray()
    }, Pretty);

    object ControlCard(KeySpec s)
    {
        var user = IdeSettingsStore.GetOrNull(s.Key);
        var effective = ResolveEffective(s, user);
        return new
        {
            key = s.Key,
            page = s.Page,
            section = s.Section,
            title = s.Title,
            description = s.Description,
            control = s.Control,
            choices = s.Choices,
            layer = s.Layer,
            writable = s.Writable,
            hot = s.Hot,
            restart_required = s.RestartRequired,
            @default = s.Default,
            process = s.ProcessValue,
            user,
            effective,
            dirty = user is not null
        };
    }

    object SnapshotEffective() => new
    {
        browser_search_engine = EffectiveSearchEngine(),
        browser_user_agent = Trunc(EffectiveUserAgent(), 56),
        desk_default_layout = EffectiveDeskLayout(),
        desk_default_mfd = EffectiveDeskMfd(),
        shell_timeout_seconds = EffectiveShellTimeout(),
        shell_codepage = EffectiveShellCodepage(),
        mcp_default_preset = EffectiveMcpDefaultPreset(),
        session_phase = CdpEnumParse.ToWire(_session.Phase),
        session_object = CdpEnumParse.ToWire(_session.Object)
    };
}
