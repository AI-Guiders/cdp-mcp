#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;
using CdpMcp.Cockpit.DataAcquisition;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=toolchain</c> / Meta <c>cdp_toolchain</c> (ADR 0198).
/// Runtime/compiler/SDK ensure — orthogonal to <c>lsp_ensure</c>. Hangs on DAL (ADR 0197).
/// </summary>
internal static partial class IdeToolchainChannel
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
        var result = op switch
        {
            "scene" or "status" or "catalog" => Scene(),
            "probe" => Probe(args),
            "ensure" => Ensure(args),
            "install" => Install(args),
            "add" => Add(args),
            "which" => Which(args),
            _ => Fail("unknown_op", "op=scene|probe|ensure|install|add|which")
        };
        if (op is "probe" or "ensure" or "install" or "add")
            PublishGlass();
        return result;
    }

    public static string PulseLine(SessionContext? session = null)
    {
        _ = session;
        var rows = StatusRows();
        var ok = rows.Count(r => r.Ok);
        return $"toolchain · {ok}/{rows.Count} ok · go=toolchain";
    }

    /// <summary>Mirror toolchain health pulse to flat CIDE chrome latch (not EICAS).</summary>
    public static void PublishGlass()
    {
        try
        {
            var rows = StatusRows();
            var ok = rows.Count(r => r.Ok);
            var pulse = $"toolchain · {ok}/{rows.Count} ok · go=toolchain";
            // Dark Cockpit: chrome only while something is missing on PATH.
            var active = rows.Count > 0 && ok < rows.Count;
            CideToolchainLatch.Publish(active, pulse, ok, rows.Count);
        }
        catch
        {
            /* best-effort */
        }
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
                "Any id: op=ensure id=python|gcc|javac|go|rust (or add custom). " +
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

        var viaForced = Opt(args, "via") ?? Opt(args, "manager");
        var via = viaForced ?? recipe.Vias[0].Name;
        var install = InstallCore(recipe, via!);
        var after = ProbeOne(id);

        // Multilang comfort: first via failed PATH probe and user did not pin via= → try remaining.
        object? fallbackInstall = null;
        if (!after.Ok && string.IsNullOrWhiteSpace(viaForced) && recipe.Vias.Length > 1)
        {
            foreach (var alt in recipe.Vias.Skip(1))
            {
                if (alt.Name.Equals(via, StringComparison.OrdinalIgnoreCase))
                    continue;
                fallbackInstall = InstallCore(recipe, alt.Name);
                after = ProbeOne(id);
                if (after.Ok)
                {
                    via = alt.Name;
                    install = fallbackInstall;
                    break;
                }
            }
        }

        return new
        {
            schema = SchemaVersion,
            ok = after.Ok,
            op = "ensure",
            id,
            status = after.Ok ? "installed_ok" : "still_missing",
            via,
            before = RowCard(before),
            install,
            fallback_install = fallbackInstall,
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
}
