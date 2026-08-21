using System.Text.Json;
using Cdp.Lsp;
using TerminalMcp.Core;

namespace CdpMcp;
internal sealed partial class LspOptionsToolkit
{
    public string Ensure(IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = Opt(args, "id") ?? Opt(args, "language") ?? Opt(args, "lang");
        if (string.IsNullOrWhiteSpace(id))
            return Fail("id_required", "id=python|go|rust|yaml|json|markdown|typescript");
        id = id!.Trim().ToLowerInvariant();

        if (id == "powershell")
            return EnsurePowerShell(args);

        var before = ProbeOne(id);
        if (before.Ok)
        {
            return JsonSerializer.Serialize(new { schema = IdeSettingsHabitat.Schema, ok = true, op = "lsp_ensure", id, status = "already_ok", server = RowCard(before), hint = "Already on PATH — open a .py/.go/… file and use IDE verbs." }, Pretty);
        }

        if (!Recipes.TryGetValue(id, out var recipe))
        {
            return JsonSerializer.Serialize(new { schema = IdeSettingsHabitat.Schema, ok = false, op = "lsp_ensure", id, reason = "no_recipe", search_q = $"{id} language server install windows", next = new object[] { new { go = "internet_browser_search", label = "Search", why = $"q={id} language server install" }, new { go = "shell_scene", label = "Shell", why = "manual install then lsp_probe" } }, hint = "No built-in recipe — search in agent browser, install via shell, then lsp_probe / lsp_add." }, Pretty);
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

        return JsonSerializer.Serialize(new { schema = IdeSettingsHabitat.Schema, ok = after.Ok, op = "lsp_ensure", id, status = after.Ok ? "installed_ok" : "still_missing", before = RowCard(before), install = JsonSerializer.Deserialize<object>(installJson), fallback_install = fallbackInstall, via, after = RowCard(after), next = after.Ok ? new object[] { new { go = "lsp_probe", label = "Probe", why = $"id={id}" }, new { go = "project_scene", label = "Open project", why = "cdp_open then IDE verbs" } } : new object[] { new { go = "internet_browser_search", label = "Search", why = $"q={recipe.SearchQuery}" }, new { go = "shell_last", label = "Shell last", why = "see install output" }, new { go = "lsp_install", label = "Retry install", why = $"id={id} via={via}" } }, hint = after.Ok ? "LSP ready — hot pool reloaded. No host remount needed if bin is on PATH." : installOk ? "Install exited ok but PATH probe still fails — new shell/MCP remount may be needed for PATH refresh." : "Install failed — check shell_last or try via=npm|pip|scoop|go." }, Pretty);
    }

    string EnsurePowerShell(IReadOnlyDictionary<string, JsonElement> args)
    {
        var beforeOk = Ps1EditorServices.TryProbe(out var beforeDoc);
        var beforeCard = beforeOk && beforeDoc is not null
            ? PsesProbeCard(beforeDoc.RootElement)
            : new { ok = false, id = "powershell", error = "pes_missing", ensure = "op=lsp_ensure id=powershell" };

        if (beforeOk)
        {
            beforeDoc?.Dispose();
            return JsonSerializer.Serialize(new
            {
                schema = IdeSettingsHabitat.Schema,
                ok = true,
                op = "lsp_ensure",
                id = "powershell",
                status = "already_ok",
                pes = beforeCard,
                hint = "PSES ready in CDP quarantine (Open VSX) — completions/navigation/DAP."
            }, Pretty);
        }

        beforeDoc?.Dispose();
        var ensure = Ps1EditorServices.EnsureOpenVsx(CancellationToken.None);
        ApplyMergedPresets();
        var afterOk = Ps1EditorServices.TryProbe(out var afterDoc);
        var afterCard = afterOk && afterDoc is not null
            ? PsesProbeCard(afterDoc.RootElement)
            : new { ok = false, id = "powershell", error = ensure.Error ?? "pes_missing" };
        afterDoc?.Dispose();

        return JsonSerializer.Serialize(new
        {
            schema = IdeSettingsHabitat.Schema,
            ok = afterOk,
            op = "lsp_ensure",
            id = "powershell",
            status = afterOk ? "installed_ok" : "still_missing",
            via = "openvsx",
            before = beforeCard,
            install = new
            {
                ok = ensure.Ok,
                source = "open-vsx",
                id = Ps1EditorServices.OpenVsxPluginId,
                version = ensure.Version,
                error = ensure.Error,
                hint = ensure.Hint,
                quarantine = CdpPluginQuarantine.Root,
                plugin = ensure.Plugin is null
                    ? null
                    : new
                    {
                        id = ensure.Plugin.Id,
                        display_name = ensure.Plugin.DisplayName,
                        version = ensure.Plugin.Version,
                        root = ensure.Plugin.RootDir
                    }
            },
            after = afterCard,
            next = afterOk
                ? new object[]
                {
                    new { go = "lsp_probe", label = "Probe", why = "id=powershell" },
                    new { go = "plugins", label = "Plugins", why = "op=list" }
                }
                : new object[]
                {
                    new { go = "plugins", label = "Install", why = "op=install id=ms-vscode.powershell" },
                    new { go = "internet_browser_search", label = "Search", why = "q=ms-vscode powershell open vsx" }
                },
            hint = afterOk
                ? "PSES installed to CDP quarantine — LSP pool reloaded."
                : "Open VSX install failed or PSES still missing — go=plugins op=install id=ms-vscode.powershell"
        }, Pretty);
    }

    static object PsesProbeCard(JsonElement probe) => new
    {
        ok = true,
        id = "powershell",
        source = probe.TryGetProperty("source", out var s) ? s.GetString() : null,
        module = probe.TryGetProperty("module", out var m) ? m.GetString() : null,
        bundled = probe.TryGetProperty("bundled", out var b) ? b.GetString() : null
    };

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
        dict["hint"] = after.Ok ? "Installed and resolves." : "Ran installer — if still missing, PATH may need MCP remount.";
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
        return JsonSerializer.Serialize(new { schema = IdeSettingsHabitat.Schema, ok = true, op = "lsp_add", id, command, args = argsList, probe = RowCard(probe), path = IdeSettingsStore.FilePath, hint = probe.Ok ? "User LSP preset registered + resolves." : "Registered — install the binary (lsp_ensure / shell) then probe." }, Pretty);
    }

    public void ApplyMergedPresets()
    {
        var merged = MergePresets(_process.LspPresets, LoadUserPresets(), Recipes.Values);
        IdeLanguageTools.ReconfigureLsp(merged);
    }
}