using System.Text.Json;
using Cdp.Core;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Agent IDE Tools → Options (ADR 0190). Not Cursor settings.json.
/// Browse pages like VS Options; set hot prefs without leaving CDP.
/// </summary>
internal sealed class IdeSettingsHabitat
{
    public const string Schema = "ide_options/v1";

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static readonly (string Id, string Title, string Why)[] Pages =
    [
        ("environment", "Environment", "Session phase / object cold defaults"),
        ("internet", "Internet", "Browser search engine, UA, lynx width/timeout"),
        ("languages", "Languages / LSP", "Probe + install language servers inside the IDE"),
        ("desk", "Desk", "Cockpit tile layout + MFD default"),
        ("shell", "Shell", "Terminal timeout / codepage defaults"),
        ("mcp", "MCP Outlet", "Default guest preset for mount"),
        ("process", "Process (read-only)", "cdp-mcp.toml backends — remount to change")
    ];

    readonly string _configPath;
    readonly CdpSettings _process;
    readonly SessionContext _session;
    readonly LspOptionsToolkit _lsp;

    public IdeSettingsHabitat(
        string configPath,
        CdpSettings process,
        SessionContext session,
        ShellHabitat shell,
        Func<ShellCwdDefaults> shellDefaults)
    {
        _configPath = configPath;
        _process = process;
        _session = session;
        _lsp = new LspOptionsToolkit(shell, shellDefaults, process);
        IdeSettingsStore.EnsureLoaded();
        _lsp.ApplyMergedPresets();
    }

    public string Dispatch(IReadOnlyDictionary<string, JsonElement> args)
    {
        var op = Opt(args, "op") ?? "options";
        return op.Trim().ToLowerInvariant() switch
        {
            "options" or "scene" or "status" or "tools" => Options(args),
            "page" or "category" or "open" => Page(args),
            "catalog" or "keys" or "list" => Catalog(args),
            "get" => Get(args),
            "set" => Set(args),
            "unset" or "reset" or "clear" => Unset(args),
            "reset_all" or "clear_all" => ResetAll(),
            "which" or "path" => Which(),
            "lsp_probe" or "lsp_status" => _lsp.Probe(args),
            "lsp_install" => _lsp.Install(args),
            "lsp_ensure" => _lsp.Ensure(args),
            "lsp_add" => _lsp.Add(args),
            _ => Fail("unknown_op",
                "op=options|page|catalog|get|set|unset|lsp_probe|lsp_install|lsp_ensure|lsp_add|which")
        };
    }

    public SettingsPulse Pulse()
    {
        IdeSettingsStore.EnsureLoaded();
        var user = IdeSettingsStore.SnapshotUser();
        var layout = IdeSettingsStore.GetOrNull("desk.default_layout");
        var mode = EffectiveDeskMode();
        var search = EffectiveSearchEngine();
        var line = user.Count == 0
            ? $"Options: factory defaults mode={mode}"
            : $"Options user×{user.Count} mode={mode}" +
              (layout is { Length: > 0 } ? $" layout={layout}" : "") +
              $" search={search}";
        return new SettingsPulse(true, line, user.Count, IdeSettingsStore.FilePath, _configPath);
    }

    /// <summary>Tools → Options root — page tree, not a flat dump.</summary>
    string Options(IReadOnlyDictionary<string, JsonElement> args)
    {
        // Comfort: section= or page= on options → open that page directly (VS jump).
        var jump = Opt(args, "page") ?? Opt(args, "section") ?? Opt(args, "group");
        if (jump is { Length: > 0 })
            return Page(args);

        var user = IdeSettingsStore.SnapshotUser();
        var tree = Pages.Select(p => new
        {
            id = p.Id,
            title = p.Title,
            why = p.Why,
            go = "options_page",
            page = p.Id,
            writable_keys = Specs(_process, _configPath).Count(s =>
                s.Page.Equals(p.Id, StringComparison.OrdinalIgnoreCase) && s.Writable),
            overridden = Specs(_process, _configPath).Count(s =>
                s.Page.Equals(p.Id, StringComparison.OrdinalIgnoreCase)
                && IdeSettingsStore.GetOrNull(s.Key) is not null)
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "options",
            role = "tools_options",
            title = "Tools → Options",
            hint =
                "Agent IDE Options (VS Tools>Options twin). page=languages → lsp_ensure id=python. " +
                "Not Cursor settings.json.",
            layers = new
            {
                process = new { path = _configPath, exists = File.Exists(_configPath), writable = false },
                user = new
                {
                    path = IdeSettingsStore.FilePath,
                    exists = File.Exists(IdeSettingsStore.FilePath),
                    writable = true,
                    count = user.Count
                }
            },
            effective = SnapshotEffective(),
            tree,
            next = new object[]
            {
                new { go = "options_page", label = "Languages / LSP", why = "page=languages — probe/install" },
                new { go = "options_page", label = "Internet", why = "page=internet — search engine / UA" },
                new { go = "options_page", label = "Desk", why = "page=desk — tile layout presets" },
                new { go = "lsp_ensure", label = "Ensure Python LSP", why = "id=python" },
                new { go = "settings_set", label = "Set", why = "key=browser.search_engine value=ddg" }
            }
        }, Pretty);
    }

    string Page(IReadOnlyDictionary<string, JsonElement> args)
    {
        var page = Opt(args, "page") ?? Opt(args, "section") ?? Opt(args, "group") ?? Opt(args, "category");
        if (string.IsNullOrWhiteSpace(page))
            return Fail("page_required", "page=languages|internet|desk|shell|mcp|environment|process");

        page = page!.Trim().ToLowerInvariant();
        // Aliases
        page = page switch
        {
            "browser" or "net" or "web" => "internet",
            "session" or "env" => "environment",
            "cockpit" or "tiles" or "layout" => "desk",
            "terminal" => "shell",
            "toml" or "backends" => "process",
            "outlet" => "mcp",
            "lsp" or "lang" or "language" or "language_servers" => "languages",
            _ => page
        };

        var meta = Pages.FirstOrDefault(p => p.Id.Equals(page, StringComparison.OrdinalIgnoreCase));
        if (meta.Id is null)
            return Fail("unknown_page", "page=languages|internet|desk|shell|mcp|environment|process");

        if (meta.Id == "languages")
        {
            var body = _lsp.LanguagesPage();
            return JsonSerializer.Serialize(new
            {
                schema = Schema,
                ok = true,
                op = "page",
                role = "tools_options_page",
                page = meta.Id,
                title = meta.Title,
                why = meta.Why,
                body,
                hint = "lsp_ensure id=python — browser search + shell install + probe, all inside CDP."
            }, Pretty);
        }

        var controls = Specs(_process, _configPath)
            .Where(s => s.Page.Equals(page, StringComparison.OrdinalIgnoreCase))
            .Select(ControlCard)
            .ToList();

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            ok = true,
            op = "page",
            role = "tools_options_page",
            page = meta.Id,
            title = meta.Title,
            why = meta.Why,
            controls,
            next = new object[]
            {
                new { go = "settings_set", label = "Apply", why = "key=… value=… (from controls[].key)" },
                new { go = "settings_unset", label = "Reset one", why = "key=… drop user override" },
                new { go = "options", label = "Back to Options", why = "page tree" }
            },
            hint = meta.Id == "process"
                ? "Read-only process layer — edit cdp-mcp.toml + remount MCP."
                : "Pick a control, set key= value=. Choices listed when enum."
        }, Pretty);
    }

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

    static IEnumerable<KeySpec> Specs(CdpSettings p, string configPath)
    {
        var layouts = IdeCockpit.LayoutPresetIds;
        var mcpPresets = McpOutletHabitat.KnownPresetIds;
        return
        [
            // Environment
            new("session.default_phase", "environment", "session", "Default phase", "enum",
                ["recall", "explore", "clarify", "plan", "act", "verify", "handoff"],
                "user", true, true, false,
                "Cold/hot session phase (ListTools catalog axis)", p.DefaultPhase, p.DefaultPhase),
            new("session.default_object", "environment", "session", "Default object", "enum",
                ["kb", "code", "repo", "task", "finding", "process", "issue", "session"],
                "user", true, true, false,
                "Cold/hot session object", p.DefaultObject, p.DefaultObject),

            // Internet
            new("browser.search_engine", "internet", "browser", "Default search engine", "enum",
                ["ddg", "google", "bing"],
                "user", true, true, false,
                "op=search without engine= (sovereign default = ddg)", "ddg", null),
            new("browser.user_agent", "internet", "browser", "User-Agent", "string", null,
                "user", true, true, false,
                "Lynx -useragent= (env CDP_BROWSER_UA wins if set)",
                InternetBrowserHabitat.DefaultUserAgent, null),
            new("browser.width", "internet", "browser", "Dump width", "int", null,
                "user", true, true, false,
                "Lynx -width", InternetBrowserHabitat.DefaultWidth.ToString(), null),
            new("browser.timeout_seconds", "internet", "browser", "Fetch timeout", "int", null,
                "user", true, true, false,
                "Seconds", InternetBrowserHabitat.DefaultTimeoutSeconds.ToString(), null),
            new("browser.dump_chars", "internet", "browser", "Dump char cap", "int", null,
                "user", true, true, false,
                "Max body chars returned", InternetBrowserHabitat.DumpBodyChars.ToString(), null),

            // Desk
            new("desk.mode", "desk", "desk", "Desk model", "enum",
                ["seats", "tiles"],
                "user", true, true, false,
                "seats = Scan Pattern P|Forward|M (default); tiles = legacy append pins", "seats", null),
            new("desk.default_layout", "desk", "desk", "Default seat/tile preset", "enum", layouts,
                "user", true, true, false,
                "Cold fill when seats empty (cockpit = P+F+M)", null, null),
            new("desk.layout.hold", "desk", "desk", "Hold phase→desk auto-layout", "bool", null,
                "user", true, true, false,
                "When true, cdp_context phase= does not retune seats (escape). Explicit layout= still works.", "false", null),
            new("desk.default_mfd", "desk", "desk", "Default MFD (deprecated)", "enum",
                ["nav", "sys", "chk", "ecl", "gates"],
                "user", true, true, false,
                "Deprecated in seats: use go=sys|chk|gates or desk_detail=nav. Kept for tiles/legacy.", "nav", null),
            new("desk.seat.p", "desk", "desk", "Default P seat organ", "string", null,
                "user", true, true, false,
                "Cold P when no layout (project_scene|empty)", "project_scene", null),
            new("desk.seat.forward", "desk", "desk", "Default Forward seat organ", "string", null,
                "user", true, true, false,
                "Cold Forward (editor_scene)", "editor_scene", null),
            new("desk.seat.m", "desk", "desk", "Default M seat organ", "string", null,
                "user", true, true, false,
                "Cold M (browser|empty)", "browser", null),
            new("desk.seat.organ.browser", "desk", "desk", "Seat for browser", "enum",
                ["p", "forward", "m"],
                "user", true, true, false,
                "Override organ→seat policy", "m", null),
            new("desk.seat.organ.git", "desk", "desk", "Seat for git", "enum",
                ["p", "forward", "m"],
                "user", true, true, false,
                "Override organ→seat policy", "m", null),
            new("desk.seat.organ.shell", "desk", "desk", "Seat for shell", "enum",
                ["p", "forward", "m"],
                "user", true, true, false,
                "Override organ→seat policy", "m", null),
            new("desk.seat.organ.correspondence", "desk", "desk", "Seat for correspondence", "enum",
                ["p", "forward", "m"],
                "user", true, true, false,
                "Override organ→seat policy", "m", null),
            new("desk.seat.organ.editor_scene", "desk", "desk", "Seat for editor", "enum",
                ["p", "forward", "m"],
                "user", true, true, false,
                "Override organ→seat policy", "forward", null),

            // Shell
            new("shell.timeout_seconds", "shell", "shell", "Command timeout", "int", null,
                "user", true, true, false,
                "cdp_shell_run default timeout", ShellHabitat.DefaultTimeoutSeconds.ToString(), null),
            new("shell.codepage", "shell", "shell", "Console codepage", "int", null,
                "user", true, true, false,
                "Default tab codepage (65001 = UTF-8)", "65001", null),

            // MCP
            new("mcp.default_preset", "mcp", "mcp", "Default mount preset", "enum", mcpPresets,
                "user", true, true, false,
                "cdp_mcp op=mount with no preset=/command= uses this", null, null),

            // Process (read-only)
            new("process.default_phase", "process", "process", "toml default_phase", "string", null,
                "process", false, false, true, "Startup phase", p.DefaultPhase, p.DefaultPhase),
            new("process.default_object", "process", "process", "toml default_object", "string", null,
                "process", false, false, true, "Startup object", p.DefaultObject, p.DefaultObject),
            new("process.memory.world.enabled", "process", "process", "memory.world", "bool", null,
                "process", false, false, true, "Backend toggle", BoolStr(p.Memory.World.Enabled), BoolStr(p.Memory.World.Enabled)),
            new("process.memory.project.enabled", "process", "process", "memory.project", "bool", null,
                "process", false, false, true, "Backend toggle", BoolStr(p.Memory.Project.Enabled), BoolStr(p.Memory.Project.Enabled)),
            new("process.memory.session.enabled", "process", "process", "memory.session", "bool", null,
                "process", false, false, true, "Backend toggle", BoolStr(p.Memory.Session.Enabled), BoolStr(p.Memory.Session.Enabled)),
            new("process.dev.git.enabled", "process", "process", "git", "bool", null,
                "process", false, false, true, "Backend toggle", BoolStr(p.Dev.Git.Enabled), BoolStr(p.Dev.Git.Enabled)),
            new("process.dev.debug.enabled", "process", "process", "debug", "bool", null,
                "process", false, false, true, "Backend toggle", BoolStr(p.Dev.Debug.Enabled), BoolStr(p.Dev.Debug.Enabled)),
            new("process.languages", "process", "process", "Languages", "string", null,
                "process", false, false, true, "Language ids", string.Join(",", p.Languages.Ids), string.Join(",", p.Languages.Ids)),
            new("process.config_path", "process", "process", "Config path", "string", null,
                "process", false, false, true, "Resolved cdp-mcp.toml", configPath, configPath),
        ];
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
