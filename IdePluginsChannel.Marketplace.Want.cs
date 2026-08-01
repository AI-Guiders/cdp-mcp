#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Feature-want flow for plugins marketplace (search → takeable install).</summary>
internal static partial class IdePluginsChannel
{
    /// <summary>Feature intent: search Open VSX → ingest first takeable (A/B).</summary>
    static object DoWant(Dictionary<string, JsonElement> merged, CancellationToken cancellationToken)
    {
        var q = Opt(merged, "q") ?? Opt(merged, "query") ?? Opt(merged, "feature") ?? Opt(merged, "text") ?? "";
        if (q.Length == 0)
        {
            return PublishThen(BuildListBoard(Build(showAll: true), new
            {
                ok = false,
                op = "want",
                error = "feature_required",
                hint = "plugins want plantuml | checkstyle | shellcheck"
            }));
        }

        var search = DoSearch(new Dictionary<string, JsonElement>
        {
            ["q"] = JsonSerializer.SerializeToElement(q)
        }, cancellationToken);

        if (!search.Result.Ok || search.Result.Hits.Count == 0)
        {
            return PublishThen(BuildSearchBoard(search.Result, new
            {
                ok = false,
                op = "want",
                error = search.Result.Ok ? "no_hits" : search.Result.Error,
                hint = search.Result.Hint ?? "No Open VSX hits for feature",
                feature = q
            }, Build()));
        }

        var tried = new List<object>();
        object? primaryRefuse = null;
        foreach (var hit in search.Result.Hits.Take(5))
        {
            var dl = OpenVsxClient.Download(hit.Namespace, hit.Name, version: null, cancellationToken);
            if (!dl.Ok || dl.Path is not { Length: > 0 })
            {
                tried.Add(new { id = hit.Id, ok = false, error = dl.Error ?? "download_failed" });
                continue;
            }

            var installed = CdpPluginQuarantine.InstallFromVsix(dl.Path);
            var host = CdpPluginQuarantine.HostProbeCard(installed.Plugin);
            var fits = WantHitFitsFeature(hit, q, installed.Plugin);
            var takeable = installed is { Ok: true, Plugin: not null }
                && installed.Plugin.Attention
                && installed.Plugin.Mode is "A" or "B"
                && fits;
            tried.Add(new
            {
                id = hit.Id,
                ok = installed.Ok,
                mode = installed.Plugin?.Mode,
                takeable,
                feature_fit = fits,
                hint = installed.Hint,
                host
            });

            if (takeable)
            {
                return PublishThen(BuildListBoard(Build(showAll: true), new
                {
                    ok = true,
                    op = "want",
                    feature = q,
                    chosen = hit.Id,
                    hint = installed.Hint,
                    host,
                    tried,
                    plugin = new
                    {
                        id = installed.Plugin!.Id,
                        display_name = installed.Plugin.DisplayName,
                        version = installed.Plugin.Version,
                        mode = installed.Plugin.Mode,
                        payload = installed.Plugin.PayloadPath,
                        payload_kind = installed.Plugin.PayloadKind,
                        root = installed.Plugin.RootDir
                    }
                }));
            }

            if (primaryRefuse is null
                && installed.Plugin?.Mode is "D"
                && WantHitNameMatch(hit, q))
            {
                primaryRefuse = new
                {
                    id = hit.Id,
                    mode = "D",
                    hint = installed.Hint
                };
            }
        }

        return PublishThen(BuildSearchBoard(search.Result, new
        {
            ok = false,
            op = "want",
            feature = q,
            error = "no_takeable",
            hint = primaryRefuse is not null
                ? "Feature-matched candidate is Mode D — refuse (see tried); not auto-picking tag-only mega extensions"
                : "Candidates found but none Mode A/B takeable for this feature — pick sN or refuse (Mode D)",
            refuse = primaryRefuse,
            tried
        }, Build(showAll: true)));
    }

    /// <summary>
    /// Auto-want only when the hit is about the feature — not a kitchen-sink that merely tags it.
    /// </summary>
    internal static bool WantHitFitsFeature(
        OpenVsxClient.Hit hit,
        string featureQuery,
        CdpPluginQuarantine.PluginInfo? plugin)
    {
        if (WantHitNameMatch(hit, featureQuery))
            return true;
        if (plugin is null)
            return false;
        // Mega marketplace packs (Trunk, …) list dozens of tools as tags — never auto-want them
        // just because one tag matches the query.
        if (plugin.Groups.Count > 24)
            return false;
        var ql = featureQuery.Trim();
        return plugin.Groups.Any(g =>
            g.Equals(ql, StringComparison.OrdinalIgnoreCase)
            || g.Contains(ql, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool WantHitNameMatch(OpenVsxClient.Hit hit, string featureQuery)
    {
        var ql = featureQuery.Trim();
        if (ql.Length == 0)
            return false;
        return hit.Name.Contains(ql, StringComparison.OrdinalIgnoreCase)
               || hit.Id.Contains(ql, StringComparison.OrdinalIgnoreCase)
               || (hit.DisplayName?.Contains(ql, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
