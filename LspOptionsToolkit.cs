using System.Text.Json;
using Cdp.Lsp;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Tools → Options → Languages: probe / install / ensure LSP without leaving the agent IDE.
/// Flow: missing → (browser search) → shell install → probe → hot-reload pool.
/// </summary>
internal sealed class LspOptionsToolkit
{
    public const string UserPresetsKey = "lsp.user_presets";

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    readonly ShellHabitat _shell;
    readonly Func<ShellCwdDefaults> _shellDefaults;
    readonly CdpSettings _process;

    public LspOptionsToolkit(ShellHabitat shell, Func<ShellCwdDefaults> shellDefaults, CdpSettings process)
    {
        _shell = shell;
        _shellDefaults = shellDefaults;
        _process = process;
    }

    public object LanguagesPage()
    {
        var rows = StatusRows();
        var missing = rows.Where(r => !r.Ok).ToList();
        return new
        {
            page = "languages",
            title = "Languages / LSP",
            why = "Probe + install language servers inside the IDE (Tools→Options)",
            servers = rows.Select(RowCard),
            missing_count = missing.Count,
            recipes = Recipes.Values.Select(r => new
            {
                id = r.Id,
                title = r.Title,
                package = r.Package,
                vias = r.Vias.Select(v => v.Via).ToArray(),
                search_q = r.SearchQuery
            }),
            next = missing.Count == 0
                ? new object[]
                {
                    new { go = "lsp_probe", label = "Probe all", why = "op=lsp_probe" },
                    new { go = "options", label = "Back", why = "Options tree" }
                }
                : missing.Select(m => (object)new
                {
                    go = "lsp_ensure",
                    label = $"Ensure {m.Id}",
                    why = $"id={m.Id} — install if missing"
                }).Concat(
                [
                    new { go = "internet_browser_search", label = "Search how", why = $"q={missing[0].SearchQuery}" },
                    new { go = "options", label = "Back", why = "Options tree" }
                ]).ToArray(),
            hint =
                "Today's language missing LSP? op=lsp_ensure id=python (or gopls/yaml/…). " +
                "Install runs in IDE shell; pool hot-reloads — no Cursor remount for PATH-visible bins."
        };
    }

    public string Probe(IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = Opt(args, "id") ?? Opt(args, "language") ?? Opt(args, "lang");
        var rows = StatusRows();
        if (id is { Length: > 0 })
            rows = rows.Where(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();

        return JsonSerializer.Serialize(new
        {
            schema = IdeSettingsHabitat.Schema,
            ok = true,
            op = "lsp_probe",
            count = rows.Count,
            servers = rows.Select(RowCard),
            hint = rows.Any(r => !r.Ok)
                ? "Missing — op=lsp_ensure id=… or search + shell install."
                : "All probed LSPs resolve on PATH."
        }, Pretty);
    }

    public string Ensure(IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = Opt(args, "id") ?? Opt(args, "language") ?? Opt(args, "lang");
        if (string.IsNullOrWhiteSpace(id))
            return Fail("id_required", "id=python|go|rust|yaml|json|markdown|typescript");

        id = id!.Trim().ToLowerInvariant();
        var before = ProbeOne(id);
        if (before.Ok)
        {
            return JsonSerializer.Serialize(new
            {
                schema = IdeSettingsHabitat.Schema,
                ok = true,
                op = "lsp_ensure",
                id,
                status = "already_ok",
                server = RowCard(before),
                hint = "Already on PATH — open a .py/.go/… file and use IDE verbs."
            }, Pretty);
        }

        if (!Recipes.TryGetValue(id, out var recipe))
        {
            return JsonSerializer.Serialize(new
            {
                schema = IdeSettingsHabitat.Schema,
                ok = false,
                op = "lsp_ensure",
                id,
                reason = "no_recipe",
                search_q = $"{id} language server install windows",
                next = new object[]
                {
                    new
                    {
                        go = "internet_browser_search",
                        label = "Search",
                        why = $"q={id} language server install"
                    },
                    new { go = "shell_scene", label = "Shell", why = "manual install then lsp_probe" }
                },
                hint = "No built-in recipe — search in agent browser, install via shell, then lsp_probe / lsp_add."
            }, Pretty);
        }

        var viaForced = Opt(args, "via") ?? Opt(args, "manager");
        var via = viaForced ?? recipe.Vias[0].Via;
        var installJson = InstallCore(recipe, via!);
        using var installDoc = JsonDocument.Parse(installJson);
        var installOk = installDoc.RootElement.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();

        // Re-merge presets (recipe may register) + probe again.
        ApplyMergedPresets();
        var after = ProbeOne(id);

        // Multilang comfort: if first via failed PATH probe and user did not pin via=, try remaining vias.
        object? fallbackInstall = null;
        if (!after.Ok && string.IsNullOrWhiteSpace(viaForced) && recipe.Vias.Length > 1)
        {
            foreach (var alt in recipe.Vias.Skip(1))
            {
                if (alt.Via.Equals(via, StringComparison.OrdinalIgnoreCase))
                    continue;
                var altJson = InstallCore(recipe, alt.Via);
                fallbackInstall = JsonSerializer.Deserialize<object>(altJson);
                ApplyMergedPresets();
                after = ProbeOne(id);
                if (after.Ok)
                {
                    via = alt.Via;
                    installJson = altJson;
                    installOk = true;
                    break;
                }
            }
        }

        return JsonSerializer.Serialize(new
        {
            schema = IdeSettingsHabitat.Schema,
            ok = after.Ok,
            op = "lsp_ensure",
            id,
            status = after.Ok ? "installed_ok" : "still_missing",
            before = RowCard(before),
            install = JsonSerializer.Deserialize<object>(installJson),
            fallback_install = fallbackInstall,
            via,
            after = RowCard(after),
            next = after.Ok
                ? new object[]
                {
                    new { go = "lsp_probe", label = "Probe", why = $"id={id}" },
                    new { go = "project_scene", label = "Open project", why = "cdp_open then IDE verbs" }
                }
                : new object[]
                {
                    new { go = "internet_browser_search", label = "Search", why = $"q={recipe.SearchQuery}" },
                    new { go = "shell_last", label = "Shell last", why = "see install output" },
                    new { go = "lsp_install", label = "Retry install", why = $"id={id} via={via}" }
                },
            hint = after.Ok
                ? "LSP ready — hot pool reloaded. No host remount needed if bin is on PATH."
                : installOk
                    ? "Install exited ok but PATH probe still fails — new shell/MCP remount may be needed for PATH refresh."
                    : "Install failed — check shell_last or try via=npm|pip|scoop|go."
        }, Pretty);
    }

    public string Install(IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = Opt(args, "id") ?? Opt(args, "language") ?? Opt(args, "lang");
        if (string.IsNullOrWhiteSpace(id))
            return Fail("id_required", "id=python|go|rust|yaml|json|markdown|typescript");
        id = id!.Trim().ToLowerInvariant();
        if (!Recipes.TryGetValue(id, out var recipe))
            return Fail("no_recipe", "op=page page=languages for recipe list");

        var via = Opt(args, "via") ?? Opt(args, "manager") ?? recipe.Vias[0].Via;
        var json = InstallCore(recipe, via!);
        ApplyMergedPresets();
        var after = ProbeOne(id);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var dict = new Dictionary<string, object?>();
        foreach (var p in root.EnumerateObject())
            dict[p.Name] = JsonSerializer.Deserialize<object>(p.Value.GetRawText());
        dict["after"] = RowCard(after);
        dict["hint"] = after.Ok
            ? "Installed and resolves."
            : "Ran installer — if still missing, PATH may need MCP remount.";
        return JsonSerializer.Serialize(dict, Pretty);
    }

    public string Add(IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = Opt(args, "id") ?? Opt(args, "language");
        var command = Opt(args, "command") ?? Opt(args, "exe");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(command))
            return Fail("id_and_command", "id=foo command=marksman args=--stdio");

        id = id!.Trim().ToLowerInvariant();
        var argsList = ReadStringArray(args, "args") ?? ["--stdio"];
        var candidates = ReadStringArray(args, "candidates") ?? [command!];
        var langs = ReadStringArray(args, "language_ids") ?? [id];

        var preset = new LspLaunchPreset
        {
            Id = id,
            Command = command!.Trim(),
            CommandCandidates = candidates,
            Args = argsList,
            LanguageIds = langs,
            RootMarkers = ReadStringArray(args, "root_markers") ?? [".git"]
        };

        var user = LoadUserPresets().Where(p => !p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
        user.Add(preset);
        SaveUserPresets(user);
        ApplyMergedPresets();
        var probe = ProbeOne(id);

        return JsonSerializer.Serialize(new
        {
            schema = IdeSettingsHabitat.Schema,
            ok = true,
            op = "lsp_add",
            id,
            command,
            args = argsList,
            probe = RowCard(probe),
            path = IdeSettingsStore.FilePath,
            hint = probe.Ok
                ? "User LSP preset registered + resolves."
                : "Registered — install the binary (lsp_ensure / shell) then probe."
        }, Pretty);
    }

    public void ApplyMergedPresets()
    {
        var merged = MergePresets(_process.LspPresets, LoadUserPresets(), Recipes.Values);
        IdeLanguageTools.ReconfigureLsp(merged);
    }

    string InstallCore(Recipe recipe, string via)
    {
        var hit = recipe.Vias.FirstOrDefault(v => v.Via.Equals(via, StringComparison.OrdinalIgnoreCase));
        if (hit is null)
            return Fail("unknown_via", $"via={string.Join("|", recipe.Vias.Select(v => v.Via))}");

        // Ensure preset exists in pool before/after install.
        EnsureRecipePreset(recipe);

        string shellJson;
        try
        {
            shellJson = _shell.Run(
                _shellDefaults(),
                command: null,
                tabId: "lsp-install",
                cwd: null,
                shellPrefer: null,
                timeoutSeconds: Math.Max(IdeSettingsHabitat.EffectiveShellTimeout(), 300),
                background: false,
                codepage: IdeSettingsHabitat.EffectiveShellCodepage(),
                argv: hit.Argv);
        }
        catch (Exception ex)
        {
            return Fail("install_failed", ex.Message);
        }

        // Shell returns its own JSON — wrap.
        return JsonSerializer.Serialize(new
        {
            schema = IdeSettingsHabitat.Schema,
            ok = true,
            op = "lsp_install",
            id = recipe.Id,
            via = hit.Via,
            argv = hit.Argv,
            package = recipe.Package,
            tab = "lsp-install",
            shell = TryParseJson(shellJson),
            hint = "Install finished — lsp_probe id=" + recipe.Id
        }, Pretty);
    }

    void EnsureRecipePreset(Recipe recipe)
    {
        var user = LoadUserPresets();
        if (user.Any(p => p.Id.Equals(recipe.Id, StringComparison.OrdinalIgnoreCase)))
            return;
        if (_process.LspPresets.Any(p => p.Id.Equals(recipe.Id, StringComparison.OrdinalIgnoreCase)))
            return;
        if (IdeLanguageTools.CurrentLspPresets.Any(p => p.Id.Equals(recipe.Id, StringComparison.OrdinalIgnoreCase)))
            return;

        user.Add(recipe.Preset);
        SaveUserPresets(user);
        ApplyMergedPresets();
    }

    List<ServerRow> StatusRows()
    {
        var ids = IdeLanguageTools.CurrentLspPresets
            .Select(p => p.Id)
            .Concat(Recipes.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

        return ids.Select(ProbeOne).ToList();
    }

    ServerRow ProbeOne(string id)
    {
        var preset = IdeLanguageTools.CurrentLspPresets.FirstOrDefault(p =>
            p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (preset is null && Recipes.TryGetValue(id, out var recipe))
            return ProbePreset(recipe.Preset, recipe.SearchQuery);
        if (preset is null)
            return new ServerRow(id, false, null, null, null, $"{id} language server install", "no_preset");

        var search = Recipes.TryGetValue(id, out var r) ? r.SearchQuery : $"{id} language server install";
        return ProbePreset(preset, search);
    }

    static ServerRow ProbePreset(LspLaunchPreset preset, string searchQuery)
    {
        try
        {
            var resolved = LspCommandResolver.Resolve(preset);
            return new ServerRow(
                preset.Id, true, preset.Command, resolved.Display, resolved.FileName, searchQuery, null);
        }
        catch (Exception ex)
        {
            return new ServerRow(
                preset.Id, false, preset.Command, null, null, searchQuery, ex.Message);
        }
    }

    static object RowCard(ServerRow r) => new
    {
        id = r.Id,
        ok = r.Ok,
        command = r.Command,
        resolved = r.Resolved,
        path = r.Path,
        error = r.Error,
        search_q = r.SearchQuery,
        ensure = r.Ok ? null : $"op=lsp_ensure id={r.Id}"
    };

    static List<LspLaunchPreset> MergePresets(
        IReadOnlyList<LspLaunchPreset> process,
        IReadOnlyList<LspLaunchPreset> user,
        IEnumerable<Recipe> recipes)
    {
        var map = new Dictionary<string, LspLaunchPreset>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in process)
            map[p.Id] = p;
        // Recipe presets fill gaps (e.g. gopls) without forcing install.
        foreach (var recipe in recipes)
        {
            if (!map.ContainsKey(recipe.Id))
                map[recipe.Id] = recipe.Preset;
        }

        foreach (var p in user)
            map[p.Id] = p;
        return map.Values.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static List<LspLaunchPreset> LoadUserPresets()
    {
        var raw = IdeSettingsStore.GetOrNull(UserPresetsKey);
        if (string.IsNullOrWhiteSpace(raw))
            return [];
        try
        {
            var docs = JsonSerializer.Deserialize<List<UserPresetDoc>>(raw, Compact) ?? [];
            return docs
                .Where(d => !string.IsNullOrWhiteSpace(d.Id) && !string.IsNullOrWhiteSpace(d.Command))
                .Select(d => new LspLaunchPreset
                {
                    Id = d.Id!.Trim().ToLowerInvariant(),
                    Command = d.Command!.Trim(),
                    CommandCandidates = d.Candidates is { Length: > 0 } c ? c : [d.Command.Trim()],
                    Args = d.Args ?? ["--stdio"],
                    LanguageIds = d.LanguageIds is { Length: > 0 } l ? l : [d.Id.Trim().ToLowerInvariant()],
                    RootMarkers = d.RootMarkers ?? [".git"]
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    static void SaveUserPresets(IReadOnlyList<LspLaunchPreset> presets)
    {
        var docs = presets.Select(p => new UserPresetDoc
        {
            Id = p.Id,
            Command = p.Command,
            Candidates = p.CommandCandidates.ToArray(),
            Args = p.Args.ToArray(),
            LanguageIds = p.LanguageIds.ToArray(),
            RootMarkers = p.RootMarkers.ToArray()
        }).ToList();
        IdeSettingsStore.Set(UserPresetsKey, JsonSerializer.Serialize(docs, Compact));
    }

    static object? TryParseJson(string json)
    {
        try { return JsonSerializer.Deserialize<object>(json); }
        catch { return json; }
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    static string[]? ReadStringArray(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.String)
        {
            return el.GetString()!
                .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (el.ValueKind != JsonValueKind.Array)
            return null;
        return el.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => s.Length > 0)
            .ToArray();
    }

    static string Fail(string reason, string hint) =>
        JsonSerializer.Serialize(new { schema = IdeSettingsHabitat.Schema, ok = false, reason, hint }, Pretty);

    sealed record ServerRow(
        string Id,
        bool Ok,
        string? Command,
        string? Resolved,
        string? Path,
        string SearchQuery,
        string? Error);

    sealed record ViaSpec(string Via, string[] Argv);

    sealed record Recipe(
        string Id,
        string Title,
        string Package,
        string SearchQuery,
        LspLaunchPreset Preset,
        ViaSpec[] Vias);

    sealed class UserPresetDoc
    {
        public string? Id { get; set; }
        public string? Command { get; set; }
        public string[]? Candidates { get; set; }
        public string[]? Args { get; set; }
        public string[]? LanguageIds { get; set; }
        public string[]? RootMarkers { get; set; }
    }

    static readonly Dictionary<string, Recipe> Recipes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["python"] = new(
            "python",
            "Python (basedpyright)",
            "basedpyright",
            "basedpyright-langserver npm install windows",
            LspLaunchPreset.DefaultPython,
            [
                new("npm", ["npm", "install", "-g", "basedpyright"]),
                new("pip", ["pip", "install", "basedpyright"]),
                new("pipx", ["pipx", "install", "basedpyright"])
            ]),
        ["go"] = new(
            "go",
            "Go (gopls)",
            "gopls",
            "gopls install golang.org/x/tools/gopls",
            new LspLaunchPreset
            {
                Id = "go",
                Command = "gopls",
                CommandCandidates = ["gopls"],
                Args = ["serve"],
                LanguageIds = ["go"],
                RootMarkers = ["go.mod", ".git"]
            },
            [new("go", ["go", "install", "golang.org/x/tools/gopls@latest"])]),
        ["rust"] = new(
            "rust",
            "Rust (rust-analyzer)",
            "rust-analyzer",
            "rust-analyzer install rustup component",
            new LspLaunchPreset
            {
                Id = "rust",
                Command = "rust-analyzer",
                CommandCandidates = ["rust-analyzer"],
                Args = [],
                LanguageIds = ["rust"],
                RootMarkers = ["Cargo.toml", ".git"]
            },
            [
                new("rustup", ["rustup", "component", "add", "rust-analyzer"]),
                new("scoop", ["scoop", "install", "rust-analyzer"])
            ]),
        ["yaml"] = new(
            "yaml",
            "YAML language server",
            "yaml-language-server",
            "yaml-language-server npm install",
            new LspLaunchPreset
            {
                Id = "yaml",
                Command = "yaml-language-server",
                CommandCandidates = ["yaml-language-server"],
                Args = ["--stdio"],
                LanguageIds = ["yaml"],
                RootMarkers = [".git"]
            },
            [new("npm", ["npm", "install", "-g", "yaml-language-server"])]),
        ["json"] = new(
            "json",
            "JSON language server",
            "vscode-langservers-extracted",
            "vscode-json-language-server npm install",
            new LspLaunchPreset
            {
                Id = "json",
                Command = "vscode-json-language-server",
                CommandCandidates = ["vscode-json-language-server", "vscode-json-languageserver"],
                Args = ["--stdio"],
                LanguageIds = ["json"],
                RootMarkers = [".git"]
            },
            [new("npm", ["npm", "install", "-g", "vscode-langservers-extracted"])]),
        ["markdown"] = new(
            "markdown",
            "Markdown (marksman)",
            "marksman",
            "marksman language server scoop install",
            new LspLaunchPreset
            {
                Id = "markdown",
                Command = "marksman",
                CommandCandidates = ["marksman"],
                Args = ["server"],
                LanguageIds = ["markdown"],
                RootMarkers = [".git"]
            },
            [
                new("scoop", ["scoop", "install", "marksman"]),
                new("winget", ["winget", "install", "-e", "--id", "artempyanykh.marksman"])
            ]),
        ["typescript"] = new(
            "typescript",
            "TypeScript (typescript-language-server)",
            "typescript-language-server",
            "typescript-language-server npm install",
            new LspLaunchPreset
            {
                Id = "typescript",
                Command = "typescript-language-server",
                CommandCandidates = ["typescript-language-server", "typescript-language-server.cmd"],
                Args = ["--stdio"],
                LanguageIds = ["typescript", "javascript"],
                RootMarkers = ["tsconfig.json", "package.json", ".git"]
            },
            [new("npm", ["npm", "install", "-g", "typescript-language-server", "typescript"])]),
    };
}
