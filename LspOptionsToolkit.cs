using System.Text.Json;
using Cdp.Lsp;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Tools → Options → Languages: probe / install / ensure LSP without leaving the agent IDE.
/// Flow: missing → (browser search) → shell install → probe → hot-reload pool.
/// </summary>
internal sealed partial class LspOptionsToolkit
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
}
