#nullable enable
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CdpMcp;

internal static partial class CdpPluginQuarantine
{
    internal sealed record ModeAPayload(string Kind, string RelPath, string AbsPath);

    internal sealed record HostDep(string Name, bool Ok, string? ResolvedPath);

    sealed record HarvestClass(
        string Mode,
        ModeAPayload? Payload,
        bool Takeable,
        string Hint,
        string Feature,
        string[] Verbs,
        object Delivery,
        object HarvestNode);

    /// <summary>
    /// Discover delivery + triage: recursive assets / LSP / refuse → Mode A|B|D.
    /// </summary>
    static HarvestClass ClassifyExtension(
        string pluginRoot,
        string extensionDir,
        JsonElement pkg,
        string displayName,
        string id)
    {
        var contributesKeys = ListContributesKeys(pkg);
        var assets = FindAllModeAPayloads(pluginRoot, extensionDir);
        var payload = assets.Count > 0 ? assets[0] : null;
        var lsp = DetectLspSignals(pkg, extensionDir);
        var fetchHint = DetectFetchOnActivate(pkg, extensionDir, payload);
        var feature = InferFeature(id, displayName, pkg);

        string mode;
        bool takeable;
        string hint;
        var channels = new List<string>();

        if (payload is not null)
        {
            mode = "A";
            takeable = true;
            channels.Add("bundled_tool");
            hint = $"Mode A — takeable · {payload.Kind} {payload.RelPath}"
                   + (assets.Count > 1 ? $" (+{assets.Count - 1} more)" : "");
        }
        else if (lsp.Hit)
        {
            mode = "B";
            takeable = true;
            channels.Add("lsp");
            hint = "Mode B — takeable (LSP: " + lsp.Why + "); host wiring later";
        }
        else
        {
            mode = "D";
            takeable = false;
            channels.Add(fetchHint is { Length: > 0 } ? "fetch_on_activate" : "ui_only");
            hint = fetchHint is { Length: > 0 }
                ? "Mode D — refuse · " + fetchHint
                : "Mode D — refuse · no bundled tool/PATH asset and no LSP signal";
        }

        if (payload?.Kind is "jar")
            channels.Add("host_java");

        var verbs = InferVerbs(feature, mode, payload);
        var kept = new List<string>();
        if (payload is not null) kept.Add("assets");
        if (lsp.Hit) kept.Add("language_server");
        if (contributesKeys.Contains("languages", StringComparer.OrdinalIgnoreCase)) kept.Add("languages");
        if (contributesKeys.Contains("configuration", StringComparer.OrdinalIgnoreCase)) kept.Add("configuration");

        var refused = new[]
        {
            "menus", "keybindings", "main_extension_host", "webview_preview_ui"
        };

        var assetRows = assets.Take(24).Select(a => new { kind = a.Kind, path = a.RelPath }).ToArray();
        var delivery = new
        {
            channels = channels.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            assets = assetRows,
            primary = payload is null ? null : new { kind = payload.Kind, path = payload.RelPath },
            lsp = lsp.Hit ? lsp.Why : null,
            fetch = fetchHint,
            why = hint
        };

        var harvestNode = new
        {
            mode,
            takeable,
            feature,
            verbs,
            why = hint,
            lsp = lsp.Hit ? lsp.Why : null,
            fetch = fetchHint,
            contributes_keys = contributesKeys,
            contributes_kept = kept.ToArray(),
            contributes_refused = refused,
            assets = assetRows,
            primary = payload is null ? null : new { kind = payload.Kind, path = payload.RelPath }
        };

        return new HarvestClass(mode, payload, takeable, hint, feature, verbs, delivery, harvestNode);
    }

    static string InferFeature(string id, string displayName, JsonElement pkg)
    {
        var blob = (id + " " + displayName).ToLowerInvariant();
        if (blob.Contains("plantuml")) return "plantuml";
        if (blob.Contains("shellcheck")) return "shellcheck";
        if (blob.Contains("checkstyle") || blob.Contains("java-code-checker") || blob.Contains("spotbugs") || blob.Contains("pmd"))
            return "java-lint";
        if (blob.Contains("graphviz") || blob.Contains("dot")) return "graphviz";
        if (blob.Contains("mermaid")) return "mermaid";
        if (pkg.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
            return n.GetString() ?? "feature";
        return "feature";
    }

    static string[] InferVerbs(string feature, string mode, ModeAPayload? payload)
    {
        if (string.Equals(mode, "D", StringComparison.OrdinalIgnoreCase))
            return [];
        if (feature is "plantuml" || payload?.RelPath.Contains("plantuml", StringComparison.OrdinalIgnoreCase) == true)
            return ["preview"];
        if (feature is "shellcheck" or "java-lint")
            return ["lint"];
        if (string.Equals(mode, "B", StringComparison.OrdinalIgnoreCase))
            return ["diags"];
        return ["run"];
    }

    static string? DetectFetchOnActivate(JsonElement pkg, string extensionDir, ModeAPayload? payload)
    {
        if (payload is not null)
            return null;
        var blob = (Prop(pkg, "name") ?? "") + " " + (Prop(pkg, "displayName") ?? "");
        if (blob.Contains("shellcheck", StringComparison.OrdinalIgnoreCase))
            return "downloads shellcheck binary on VS Code activate — not in VSIX";

        try
        {
            var raw = pkg.GetRawText();
            if (raw.Contains("downloadRelease", StringComparison.OrdinalIgnoreCase)
                || raw.Contains("github.com/koalaman/shellcheck", StringComparison.OrdinalIgnoreCase))
                return "extension fetches tool at activate — not bundled";
        }
        catch { /* ignore */ }

        return null;
    }

}
