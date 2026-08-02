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

}
