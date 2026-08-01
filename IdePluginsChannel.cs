#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=plugins</c> — Open VSX search/install + groups (auto+manual) + attention enable/disable.
/// </summary>
internal static partial class IdePluginsChannel
{
    public const string SchemaVersion = "plugins_channel/v1";

    static readonly object SearchGate = new();
    static IReadOnlyList<OpenVsxClient.Hit> LastSearchHits = [];

    public sealed record Snap(
        bool Ok,
        string Pulse,
        int Count,
        int ModeA,
        int Hidden,
        IReadOnlyList<CdpPluginQuarantine.PluginInfo> Plugins,
        bool ShowAll);

    public static Snap Build(bool showAll = false)
    {
        var attention = CdpPluginQuarantine.List(attentionOnly: true);
        var all = showAll ? CdpPluginQuarantine.List(attentionOnly: false) : attention;
        var hidden = showAll
            ? all.Count(p => !p.Attention)
            : CdpPluginQuarantine.List(attentionOnly: false).Count - attention.Count;
        var modeA = attention.Count(p => string.Equals(p.Mode, "A", StringComparison.OrdinalIgnoreCase));
        var pulse = attention.Count == 0
            ? (hidden > 0
                ? $"plugins · attention empty ({hidden} off — enable group/plugin)"
                : "plugins · empty — search Open VSX")
            : $"plugins · {attention.Count} attn ({modeA} Mode A)"
              + (hidden > 0 ? $" · {hidden} hidden" : "");
        return new Snap(true, pulse, attention.Count, modeA, Math.Max(0, hidden), all, showAll);
    }

    public static object Handle(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null,
        CancellationToken cancellationToken = default)
    {
        var merged = FlattenArgs(args);
        var op = (Opt(merged, "op") ?? Opt(merged, "pulse") ?? "list").Trim().ToLowerInvariant();

        if (op is "list" or "installed"
            && (Opt(merged, "q") ?? Opt(merged, "query")) is { Length: > 0 })
            op = "search";

        object? action = null;
        var showAll = Flag(merged, "all") || Flag(merged, "show_all") || Flag(merged, "hidden");

        if (op is "search" or "find" or "query")
        {
            var search = DoSearch(merged, cancellationToken);
            action = search.Card;
            var searchBoard = BuildSearchBoard(search.Result, action, Build());
            return PublishThen(searchBoard);
        }

        if (op is "groups" or "grouplist")
            return PublishThen(BuildGroupsBoard(DoGroupsAction(merged)));

        if (op is "group" or "tag")
        {
            action = DoGroupAssign(merged);
            op = Flag(merged, "list_groups") ? "groups" : "list";
            if (op is "groups")
                return PublishThen(BuildGroupsBoard(action));
        }
        else if (op is "enable" or "on")
        {
            action = DoEnableDisable(merged, enable: true);
            op = "list";
            showAll = true;
        }
        else if (op is "disable" or "off")
        {
            action = DoEnableDisable(merged, enable: false);
            op = "list";
            showAll = true;
        }
        else if (op is "install" or "add")
        {
            action = DoInstall(merged, cancellationToken);
            op = "list";
        }
        else if (op is "want" or "need" or "get")
        {
            return DoWant(merged, cancellationToken);
        }
        else if (op is "reharvest" or "rescan" or "reclassify")
        {
            var n = CdpPluginQuarantine.ReharvestInstalled();
            action = new
            {
                ok = true,
                op = "reharvest",
                updated = n,
                hint = "Intent→Discover→Triage rewritten (delivery/host_deps)"
            };
            op = "list";
            showAll = true;
        }
        else if (op is "preview" or "render" or "png")
        {
            action = DoPreview(store, session, merged, cancellationToken);
        }

        return PublishThen(BuildListBoard(Build(showAll), action));
    }

    public static string PulseLine()
    {
        var snap = Build();
        return snap.Count == 0 && snap.Hidden == 0
            ? "plugins · idle · go=plugins"
            : $"{snap.Pulse} · go=plugins";
    }

    /// <summary>Mirror plugins attention pulse to flat CIDE chrome latch (not EICAS).</summary>
    public static void PublishGlass()
    {
        try
        {
            var snap = Build();
            var pulse = PulseLine();
            // Dark Cockpit: Mode A takeable, or attention empty while plugins are hidden/off.
            var active = snap.ModeA > 0 || (snap.Count == 0 && snap.Hidden > 0);
            CidePluginsLatch.Publish(active, pulse, snap.Count, snap.ModeA, snap.Hidden);
        }
        catch
        {
            /* best-effort */
        }
    }

    static object PublishThen(object board)
    {
        PublishGlass();
        return board;
    }

    public static object PulseCard(Snap snap) => new
    {
        schema = SchemaVersion,
        ok = snap.Ok,
        pulse = snap.Pulse,
        plugins = snap.Count,
        mode_a = snap.ModeA,
        hidden = snap.Hidden
    };

}
