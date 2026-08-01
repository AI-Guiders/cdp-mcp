using System.Text.Json;
using Cdp.Lsp;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>InstallCore, preset merge, Recipe catalog for LspOptionsToolkit.</summary>
internal sealed partial class LspOptionsToolkit
{
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
