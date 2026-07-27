#nullable enable
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=toolchain</c> / Meta <c>cdp_toolchain</c> (ADR 0198).
/// Runtime/compiler/SDK ensure — orthogonal to <c>lsp_ensure</c>. Hangs on DAL (ADR 0197).
/// </summary>
internal static class IdeToolchainChannel
{
    public const string SchemaVersion = "toolchain/v0";
    public const string ToolName = "cdp_toolchain";
    public const string GoName = "toolchain";
    const string UserRecipesKey = "toolchain.user_recipes_json";

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static ShellHabitat? _shell;
    static Func<ShellCwdDefaults>? _shellDefaults;

    public static void Configure(ShellHabitat shell, Func<ShellCwdDefaults> shellDefaults)
    {
        _shell = shell;
        _shellDefaults = shellDefaults;
    }

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(session, args), Pretty);

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        _ = session;
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "scene" or "status" or "catalog" => Scene(),
            "probe" => Probe(args),
            "ensure" => Ensure(args),
            "install" => Install(args),
            "add" => Add(args),
            "which" => Which(args),
            _ => Fail("unknown_op", "op=scene|probe|ensure|install|add|which")
        };
    }

    public static string PulseLine(SessionContext? session = null)
    {
        _ = session;
        var rows = StatusRows();
        var ok = rows.Count(r => r.Ok);
        return $"toolchain · {ok}/{rows.Count} ok · go=toolchain";
    }

    static object Scene()
    {
        var rows = StatusRows();
        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "scene",
            seam = "dal",
            adr = "0198",
            count = rows.Count,
            toolchains = rows.Select(RowCard),
            next = new object[]
            {
                new { go = "toolchain_ensure", label = "Ensure python", why = "id=python" },
                new { go = "toolchain_ensure", label = "Ensure gcc", why = "id=gcc" },
                new { go = "toolchain_probe", label = "Probe all", why = "op=probe" },
                new { go = "lsp_ensure", label = "LSP (other axis)", why = "intelligence ≠ toolchain" }
            },
            hint =
                "Any id: op=ensure id=python|gcc|javac|go (or add custom). " +
                "Not lsp_ensure — runtime/compiler on PATH (DAL-adjacent)."
        };
    }

    static object Probe(IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = Opt(args, "id") ?? Opt(args, "toolchain") ?? Opt(args, "lang");
        var rows = StatusRows();
        if (id is { Length: > 0 })
            rows = rows.Where(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();

        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "probe",
            count = rows.Count,
            toolchains = rows.Select(RowCard),
            hint = rows.Any(r => !r.Ok)
                ? "Missing — op=ensure id=… or search + shell install."
                : "All probed toolchains resolve on PATH."
        };
    }

    static object Ensure(IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = Opt(args, "id") ?? Opt(args, "toolchain") ?? Opt(args, "lang");
        if (string.IsNullOrWhiteSpace(id))
            return Fail("id_required", "id=python|gcc|javac|go|…");

        id = id!.Trim().ToLowerInvariant();
        var before = ProbeOne(id);
        if (before.Ok)
        {
            return new
            {
                schema = SchemaVersion,
                ok = true,
                op = "ensure",
                id,
                status = "already_ok",
                toolchain = RowCard(before),
                next = NextAfterOk(before),
                hint = "Already on PATH — use shell / cdp_build / optional lsp_ensure."
            };
        }

        var recipe = FindRecipe(id);
        if (recipe is null)
        {
            return new
            {
                schema = SchemaVersion,
                ok = false,
                op = "ensure",
                id,
                reason = "no_recipe",
                search_q = $"{id} toolchain install windows",
                next = new object[]
                {
                    new { go = "internet_browser_search", label = "Search", why = $"q={id} install" },
                    new { go = "shell_scene", label = "Shell", why = "manual install then toolchain_probe" },
                    new { go = "toolchain_add", label = "Register recipe", why = "op=add id=… bins=…" }
                },
                hint = "No built-in recipe — search, install via shell, or op=add then ensure."
            };
        }

        var via = Opt(args, "via") ?? Opt(args, "manager") ?? recipe.Vias[0].Name;
        var install = InstallCore(recipe, via!);
        var after = ProbeOne(id);
        return new
        {
            schema = SchemaVersion,
            ok = after.Ok,
            op = "ensure",
            id,
            status = after.Ok ? "installed_ok" : "still_missing",
            before = RowCard(before),
            install,
            after = RowCard(after),
            next = after.Ok
                ? NextAfterOk(after)
                : new object[]
                {
                    new { go = "internet_browser_search", label = "Search", why = $"q={recipe.SearchQuery}" },
                    new { go = "shell_last", label = "Shell last", why = "see install output" },
                    new { go = "toolchain_install", label = "Retry", why = $"id={id} via={via}" }
                },
            hint = after.Ok
                ? "Toolchain ready on PATH."
                : "Install ran — if still missing, PATH may need MCP remount / new shell."
        };
    }

    static object Install(IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = Opt(args, "id") ?? Opt(args, "toolchain") ?? Opt(args, "lang");
        if (string.IsNullOrWhiteSpace(id))
            return Fail("id_required", "id=python|gcc|javac|go|…");
        id = id!.Trim().ToLowerInvariant();
        var recipe = FindRecipe(id);
        if (recipe is null)
            return Fail("no_recipe", "op=scene for recipe list or op=add");

        var via = Opt(args, "via") ?? Opt(args, "manager") ?? recipe.Vias[0].Name;
        var install = InstallCore(recipe, via!);
        var after = ProbeOne(id);
        return new
        {
            schema = SchemaVersion,
            ok = after.Ok,
            op = "install",
            id,
            via,
            install,
            after = RowCard(after),
            hint = after.Ok ? "Installed and resolves." : "Ran installer — probe still missing."
        };
    }

    static object Add(IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = Opt(args, "id") ?? Opt(args, "toolchain");
        var bins = ReadStringArray(args, "bins") ?? ReadStringArray(args, "bin");
        if (string.IsNullOrWhiteSpace(id) || bins is not { Length: > 0 })
            return Fail("id_and_bins", "id=foo bins=foo,bar via=winget argv=…");

        id = id!.Trim().ToLowerInvariant();
        var search = Opt(args, "search_q") ?? $"{id} toolchain install windows";
        var pairsLsp = Opt(args, "pairs_lsp");
        var via = Opt(args, "via") ?? "winget";
        var argv = ReadStringArray(args, "argv") ?? ReadStringArray(args, "args")
                   ?? ["winget", "install", "-e", "--id", id];

        var recipe = new Recipe(
            id,
            Opt(args, "label") ?? id,
            bins,
            search,
            pairsLsp,
            [new ViaStep(via, argv)]);

        var user = LoadUserRecipes().Where(r => !r.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
        user.Add(recipe);
        SaveUserRecipes(user);
        var probe = ProbeOne(id);

        return new
        {
            schema = SchemaVersion,
            ok = true,
            op = "add",
            id,
            bins,
            pairs_lsp = pairsLsp,
            probe = RowCard(probe),
            hint = probe.Ok
                ? "User recipe registered + resolves."
                : "Registered — op=ensure / shell then probe."
        };
    }

    static object Which(IReadOnlyDictionary<string, JsonElement> args)
    {
        var id = Opt(args, "id") ?? Opt(args, "toolchain") ?? Opt(args, "lang");
        if (string.IsNullOrWhiteSpace(id))
            return Fail("id_required", "id=python|gcc|…");
        var row = ProbeOne(id!.Trim().ToLowerInvariant());
        return new
        {
            schema = SchemaVersion,
            ok = row.Ok,
            op = "which",
            id = row.Id,
            bins = row.BinResults.Select(b => new { bin = b.Bin, ok = b.Ok, path = b.Path }),
            hint = row.Ok ? "Resolved." : "Missing — op=ensure id=" + row.Id
        };
    }

    static object InstallCore(Recipe recipe, string via)
    {
        if (_shell is null || _shellDefaults is null)
            return Fail("shell_unconfigured", "IdeToolchainChannel.Configure not called");

        var hit = recipe.Vias.FirstOrDefault(v => v.Name.Equals(via, StringComparison.OrdinalIgnoreCase));
        if (hit is null)
            return Fail("unknown_via", $"via={string.Join("|", recipe.Vias.Select(v => v.Name))}");

        try
        {
            var shellJson = _shell.Run(
                _shellDefaults(),
                command: null,
                tabId: "toolchain-install",
                cwd: null,
                shellPrefer: null,
                timeoutSeconds: Math.Max(IdeSettingsHabitat.EffectiveShellTimeout(), 300),
                background: false,
                codepage: IdeSettingsHabitat.EffectiveShellCodepage(),
                argv: hit.Argv);
            return new
            {
                schema = SchemaVersion,
                ok = true,
                op = "install_core",
                id = recipe.Id,
                via,
                shell = JsonSerializer.Deserialize<object>(shellJson)
            };
        }
        catch (Exception ex)
        {
            return Fail("install_failed", ex.Message);
        }
    }

    static List<Row> StatusRows()
    {
        var ids = BuiltIns.Keys
            .Concat(LoadUserRecipes().Select(r => r.Id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        return ids.Select(ProbeOne).ToList();
    }

    static Recipe? FindRecipe(string id)
    {
        if (BuiltIns.TryGetValue(id, out var b))
            return b;
        return LoadUserRecipes().FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    static Row ProbeOne(string id)
    {
        var recipe = FindRecipe(id);
        if (recipe is null)
            return new Row(id, false, [], $"{id} toolchain install", null, "no_recipe");

        var bins = recipe.Bins.Select(bin =>
        {
            var path = ResolveOnPath(bin);
            return new BinHit(bin, path is not null, path);
        }).ToList();
        var ok = bins.Count > 0 && bins.All(b => b.Ok);
        return new Row(id, ok, bins, recipe.SearchQuery, recipe.PairsLsp, ok ? null : "missing_bin");
    }

    static string? ResolveOnPath(string bin)
    {
        try
        {
            if (Path.IsPathRooted(bin) && File.Exists(bin))
                return bin;

            var name = bin;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                OperatingSystem.IsWindows())
                name += ".exe";

            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), name);
                    if (File.Exists(candidate))
                        return candidate;
                    var bare = Path.Combine(dir.Trim(), bin);
                    if (File.Exists(bare))
                        return bare;
                }
                catch
                {
                    /* skip bad PATH entry */
                }
            }

            // where.exe fallback
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where.exe" : "which",
                Arguments = bin,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            if (p.ExitCode != 0) return null;
            var line = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
        }
        catch
        {
            return null;
        }
    }

    static object[] NextAfterOk(Row row)
    {
        var list = new List<object>
        {
            new { go = "toolchain_probe", label = "Probe", why = $"id={row.Id}" },
            new { go = "shell_scene", label = "Shell", why = "use toolchain" }
        };
        if (row.PairsLsp is { Length: > 0 })
            list.Add(new { go = "lsp_ensure", label = "Ensure LSP", why = $"id={row.PairsLsp}" });
        return list.ToArray();
    }

    static object RowCard(Row r) => new
    {
        id = r.Id,
        ok = r.Ok,
        bins = r.BinResults.Select(b => new { bin = b.Bin, ok = b.Ok, path = b.Path }),
        pairs_lsp = r.PairsLsp,
        error = r.Error,
        search_q = r.SearchQuery,
        ensure = r.Ok ? null : $"op=ensure id={r.Id}"
    };

    static List<Recipe> LoadUserRecipes()
    {
        IdeSettingsStore.EnsureLoaded();
        var raw = IdeSettingsStore.GetOrNull(UserRecipesKey);
        if (string.IsNullOrWhiteSpace(raw))
            return [];
        try
        {
            var docs = JsonSerializer.Deserialize<List<UserRecipeDoc>>(raw, Compact) ?? [];
            return docs
                .Where(d => !string.IsNullOrWhiteSpace(d.Id) && d.Bins is { Length: > 0 })
                .Select(d => new Recipe(
                    d.Id!.Trim().ToLowerInvariant(),
                    d.Label ?? d.Id!,
                    d.Bins!,
                    d.SearchQ ?? $"{d.Id} install",
                    d.PairsLsp,
                    (d.Vias ?? [])
                        .Where(v => !string.IsNullOrWhiteSpace(v.Via) && v.Argv is { Length: > 0 })
                        .Select(v => new ViaStep(v.Via!, v.Argv!))
                        .DefaultIfEmpty(new ViaStep("winget", ["winget", "install", "-e", "--id", d.Id!]))
                        .ToArray()))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    static void SaveUserRecipes(List<Recipe> recipes)
    {
        IdeSettingsStore.EnsureLoaded();
        var docs = recipes.Select(r => new UserRecipeDoc
        {
            Id = r.Id,
            Label = r.Label,
            Bins = r.Bins,
            SearchQ = r.SearchQuery,
            PairsLsp = r.PairsLsp,
            Vias = r.Vias.Select(v => new UserViaDoc { Via = v.Name, Argv = v.Argv }).ToArray()
        }).ToList();
        IdeSettingsStore.Set(UserRecipesKey, JsonSerializer.Serialize(docs, Compact));
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
    }

    static string[]? ReadStringArray(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Array)
            return el.EnumerateArray().Select(e => e.GetString() ?? e.ToString()).Where(s => s.Length > 0).ToArray();
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            return s.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return null;
    }

    static object Fail(string reason, string hint) => new
    {
        schema = SchemaVersion,
        ok = false,
        reason,
        hint
    };

    sealed record ViaStep(string Name, string[] Argv);

    sealed record Recipe(
        string Id,
        string Label,
        string[] Bins,
        string SearchQuery,
        string? PairsLsp,
        ViaStep[] Vias);

    sealed record BinHit(string Bin, bool Ok, string? Path);

    sealed record Row(
        string Id,
        bool Ok,
        List<BinHit> BinResults,
        string SearchQuery,
        string? PairsLsp,
        string? Error);

    sealed class UserRecipeDoc
    {
        public string? Id { get; set; }
        public string? Label { get; set; }
        public string[]? Bins { get; set; }
        public string? SearchQ { get; set; }
        public string? PairsLsp { get; set; }
        public UserViaDoc[]? Vias { get; set; }
    }

    sealed class UserViaDoc
    {
        public string? Via { get; set; }
        public string[]? Argv { get; set; }
    }

    static readonly Dictionary<string, Recipe> BuiltIns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["python"] = new(
            "python",
            "Python runtime",
            ["python"],
            "python install windows winget",
            "python",
            [
                new("winget", ["winget", "install", "-e", "--id", "Python.Python.3.12"]),
                new("scoop", ["scoop", "install", "python"])
            ]),
        ["gcc"] = new(
            "gcc",
            "GCC / MinGW",
            ["gcc"],
            "mingw gcc install windows",
            null,
            [
                new("winget", ["winget", "install", "-e", "--id", "BrechtSanders.WinLibs.POSIX.UCRT"]),
                new("scoop", ["scoop", "install", "gcc"])
            ]),
        ["javac"] = new(
            "javac",
            "JDK (javac)",
            ["javac"],
            "jdk javac install windows",
            null,
            [
                new("winget", ["winget", "install", "-e", "--id", "Microsoft.OpenJDK.21"]),
                new("scoop", ["scoop", "install", "temurin-jdk"])
            ]),
        ["go"] = new(
            "go",
            "Go toolchain",
            ["go"],
            "go programming language install windows",
            "go",
            [
                new("winget", ["winget", "install", "-e", "--id", "GoLang.Go"]),
                new("scoop", ["scoop", "install", "go"])
            ]),
    };
}
