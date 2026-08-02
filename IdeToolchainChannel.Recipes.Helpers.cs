#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;
using CdpMcp.Cockpit.DataAcquisition;
using TerminalMcp.Core;

namespace CdpMcp;
internal static partial class IdeToolchainChannel
{
    static List<Row> StatusRows()
    {
        var ids = BuiltIns.Keys.Concat(LoadUserRecipes().Select(r => r.Id)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
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
            new
            {
                go = "toolchain_probe",
                label = "Probe",
                why = $"id={row.Id}"},
            new
            {
                go = "shell_scene",
                label = "Shell",
                why = "use toolchain"
            }
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
        ensure = r.Ok ? null : $"op=ensure id={r.Id}"};
    static List<Recipe> LoadUserRecipes()
    {
        IdeSettingsStore.EnsureLoaded();
        var raw = IdeSettingsStore.GetOrNull(UserRecipesKey);
        if (string.IsNullOrWhiteSpace(raw))
            return[];
        try
        {
            var docs = JsonSerializer.Deserialize<List<UserRecipeDoc>>(raw, Compact) ?? [];
            return docs.Where(d => !string.IsNullOrWhiteSpace(d.Id) && d.Bins is { Length: > 0 }).Select(d => new Recipe(d.Id!.Trim().ToLowerInvariant(), d.Label ?? d.Id!, d.Bins!, d.SearchQ ?? $"{d.Id} install", d.PairsLsp, (d.Vias ?? []).Where(v => !string.IsNullOrWhiteSpace(v.Via) && v.Argv is { Length: > 0 }).Select(v => new ViaStep(v.Via!, v.Argv!)).DefaultIfEmpty(new ViaStep("winget", ["winget", "install", "-e", "--id", d.Id!])).ToArray())).ToList();
        }
        catch
        {
            return[];
        }
    }

    static void SaveUserRecipes(List<Recipe> recipes)
    {
        IdeSettingsStore.EnsureLoaded();
        var docs = recipes.Select(r => new UserRecipeDoc { Id = r.Id, Label = r.Label, Bins = r.Bins, SearchQ = r.SearchQuery, PairsLsp = r.PairsLsp, Vias = r.Vias.Select(v => new UserViaDoc { Via = v.Name, Argv = v.Argv }).ToArray() }).ToList();
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
            if (string.IsNullOrWhiteSpace(s))
                return null;
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
}