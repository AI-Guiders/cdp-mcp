#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;
using CdpMcp.Cockpit.DataAcquisition;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>Recipe catalog / probe / install helpers for go=toolchain.</summary>
internal static partial class IdeToolchainChannel
{
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

    static string? ResolveOnPath(string bin) => ToolchainPathProbe.Resolve(bin);

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
        ["rust"] = new(
            "rust",
            "Rust toolchain (rustc + cargo)",
            ["rustc", "cargo"],
            "rustup install rust windows",
            "rust",
            [
                new("winget", ["winget", "install", "-e", "--id", "Rustlang.Rustup"]),
                new("scoop", ["scoop", "install", "rustup"])
            ]),
    };
}
