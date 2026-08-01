using System.Text.Json;
using Cdp.Core;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Agent IDE Tools → Options (ADR 0190). Not Cursor settings.json.
/// Browse pages like VS Options; set hot prefs without leaving CDP.
/// </summary>
internal sealed partial class IdeSettingsHabitat
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

}
