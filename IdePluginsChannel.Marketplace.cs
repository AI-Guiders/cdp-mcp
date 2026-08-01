#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdePluginsChannel
{
    sealed record SearchPack(OpenVsxClient.SearchResult Result, object Card);

    static SearchPack DoSearch(Dictionary<string, JsonElement> merged, CancellationToken cancellationToken)
    {
        var q = Opt(merged, "q") ?? Opt(merged, "query") ?? Opt(merged, "text") ?? "";
        var size = IntOr(merged, "size", OpenVsxClient.DefaultSize);
        var result = OpenVsxClient.Search(q, size, cancellationToken);
        if (result.Ok)
        {
            lock (SearchGate)
            {
                LastSearchHits = result.Hits;
            }
        }

        var card = new
        {
            ok = result.Ok,
            op = "search",
            error = result.Error,
            hint = result.Hint,
            query = result.Query,
            hits = result.Hits.Count
        };
        return new SearchPack(result, card);
    }

    static object BuildSearchBoard(OpenVsxClient.SearchResult result, object action, Snap installed)
    {
        var lines = result.Hits
            .Take(24)
            .Select((h, i) => $"s{i + 1} {h.Id} {h.Version} — {Trunc(h.DisplayName ?? h.Name, 40)}")
            .ToArray();
        if (lines.Length == 0)
            lines = [result.Ok ? "(no hits)" : $"(search failed: {result.Error})"];

        var pulse = result.Ok
            ? $"plugins search · {result.Hits.Count} «{Trunc(result.Query, 24)}»"
            : $"plugins search FAIL · {result.Error}";

        return new
        {
            ok = result.Ok,
            schema = SchemaVersion,
            role = "plugins",
            go = "plugins",
            detail = "search",
            pulse,
            root = CdpPluginQuarantine.Root,
            counts = new
            {
                hits = result.Hits.Count,
                plugins = installed.Count,
                mode_a = installed.ModeA
            },
            view = new { schema = SchemaVersion, lines },
            rows = result.Hits.Select((h, i) => new
            {
                id = $"s{i + 1}",
                plugin_id = "openvsx:" + h.Id,
                display_name = h.DisplayName ?? h.Name,
                version = h.Version,
                description = h.Description,
                ns = h.Namespace,
                name = h.Name,
                go = "plugins",
                go_args = new { op = "install", id = h.Id }
            }).ToArray(),
            action,
            next = new object[]
            {
                new { go = "plugins", label = "Install s1", why = result.Hits.Count > 0 ? "id=" + result.Hits[0].Id : "no hits" },
                new { go = "plugins", label = "Installed", why = "op=list" },
                new { go = "plugins", label = "Search again", why = "op=search q=" }
            },
            hint = result.Ok
                ? "Tap row → op=install id=publisher.name — CDP fetch+unpack. Or cmd: plugins install jebbs.plantuml"
                : (result.Hint ?? "Fix query / network")
        };
    }

    static object DoInstall(Dictionary<string, JsonElement> merged, CancellationToken cancellationToken)
    {
        var path = Opt(merged, "path") ?? Opt(merged, "vsix") ?? Opt(merged, "file");
        var id = Opt(merged, "id") ?? Opt(merged, "extension") ?? Opt(merged, "ext");
        var row = Opt(merged, "row") ?? Opt(merged, "pick");
        var version = Opt(merged, "version") ?? Opt(merged, "ver");

        // row=s1 from last search
        if ((id is null || id.Length == 0) && row is { Length: > 0 }
            && TryResolveSearchRow(row, out var hitId))
            id = hitId;

        if (path is { Length: > 0 })
        {
            path = Path.GetFullPath(path);
            CdpPluginQuarantine.InstallResult local;
            if (Directory.Exists(path))
                local = CdpPluginQuarantine.InstallFromUnpacked(path);
            else if (File.Exists(path))
                local = CdpPluginQuarantine.InstallFromVsix(path);
            else
                return FailInstall("path_not_found", "path= .vsix / extension dir, or id=publisher.name", path);

            return InstallCard(local, source: path);
        }

        if (id is { Length: > 0 })
        {
            if (!OpenVsxClient.TryParseId(id, out var ns, out var name))
            {
                return FailInstall(
                    "id_bad",
                    "Use publisher.name (jebbs.plantuml) or namespace/name",
                    id);
            }

            var dl = OpenVsxClient.Download(ns, name, version, cancellationToken);
            if (!dl.Ok || dl.Path is not { Length: > 0 })
            {
                return new
                {
                    ok = false,
                    op = "install",
                    error = dl.Error ?? "download_failed",
                    hint = dl.Hint ?? "Open VSX resolve/download failed",
                    id = ns + "." + name,
                    version,
                    meta = dl.Meta is null
                        ? null
                        : new { id = dl.Meta.Id, version = dl.Meta.Version, display_name = dl.Meta.DisplayName }
                };
            }

            var installed = CdpPluginQuarantine.InstallFromVsix(dl.Path);
            return new
            {
                ok = installed.Ok,
                op = "install",
                error = installed.Error,
                hint = installed.Hint,
                source = "open-vsx",
                id = ns + "." + name,
                downloaded = dl.Path,
                version = dl.Meta?.Version,
                host = CdpPluginQuarantine.HostProbeCard(installed.Plugin),
                plugin = installed.Plugin is null
                    ? null
                    : new
                    {
                        id = installed.Plugin.Id,
                        display_name = installed.Plugin.DisplayName,
                        version = installed.Plugin.Version,
                        mode = installed.Plugin.Mode,
                        payload = installed.Plugin.PayloadPath,
                        payload_kind = installed.Plugin.PayloadKind,
                        jar = installed.Plugin.JarPath,
                        root = installed.Plugin.RootDir
                    }
            };
        }

        return FailInstall(
            "install_target_required",
            "id=publisher.name (Open VSX) | path=.vsix | row=s1 from last search",
            null);
    }

    static bool TryResolveSearchRow(string row, out string id)
    {
        id = "";
        var k = row.Trim();
        IReadOnlyList<OpenVsxClient.Hit> hits;
        lock (SearchGate) hits = LastSearchHits;
        if (hits.Count == 0)
            return false;

        if (int.TryParse(k, out var n) || (k.StartsWith('s') && int.TryParse(k[1..], out n)))
        {
            if (n >= 1 && n <= hits.Count)
            {
                id = hits[n - 1].Id;
                return true;
            }
        }

        return false;
    }

    static object InstallCard(CdpPluginQuarantine.InstallResult result, string? source)
    {
        var host = CdpPluginQuarantine.HostProbeCard(result.Plugin);
        return new
        {
            ok = result.Ok,
            op = "install",
            error = result.Error,
            hint = result.Hint,
            source,
            host,
            plugin = result.Plugin is null
                ? null
                : new
                {
                    id = result.Plugin.Id,
                    display_name = result.Plugin.DisplayName,
                    version = result.Plugin.Version,
                    mode = result.Plugin.Mode,
                    payload = result.Plugin.PayloadPath,
                    payload_kind = result.Plugin.PayloadKind,
                    jar = result.Plugin.JarPath,
                    root = result.Plugin.RootDir
                }
        };
    }

    static object FailInstall(string error, string hint, string? detail) => new
    {
        ok = false,
        op = "install",
        error,
        hint,
        detail
    };

}
