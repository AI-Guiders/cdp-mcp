#nullable enable

using AIGuiders.Platform.Execution.Cockpit.Channels.EnvironmentReadiness;
using AIGuiders.Platform.Execution.Cockpit.Channels.EnvironmentReadiness.ComputingUnits;
using AIGuiders.Platform.Execution.Cockpit.Channels.Primitives;
using Cdp.Core;

namespace CdpMcp.Cockpit.EnvironmentReadiness;

/// <summary>CDP/Glass-only ER rows (not in Avalonia CIDE quarry).</summary>
internal static class EnvironmentReadinessCdpRows
{
    public static IReadOnlyList<AnnunciatorLampItem> Build(
        DevSettings dev,
        CockpitHostSettings cockpitHost,
        CdpServiceSettings service)
    {
        return
        [
            BuildServiceRow(service),
            BuildBackendsRow(dev),
            BuildMemoryRow(dev),
            BuildSeatRow(),
            BuildFreshnessRow()
        ];
    }

    static AnnunciatorLampItem BuildServiceRow(CdpServiceSettings service)
    {
        if (!service.Enabled)
        {
            return new AnnunciatorLampItem(
                EnvironmentReadinessCellIdsCdp.CdpService,
                "CdpService",
                "Отключён в cdp-mcp.toml ([service].enabled=false).",
                AnnunciatorLampLevel.Advisory,
                LampShortLabel: "SVC");
        }

        var bind = $"{service.Bind}:{service.Port}";
        return new AnnunciatorLampItem(
            EnvironmentReadinessCellIdsCdp.CdpService,
            "CdpService",
            $"Включён: {bind} (stdio MCP — проверка HTTP не требуется).",
            AnnunciatorLampLevel.Ok,
            LampShortLabel: "SVC");
    }

    static AnnunciatorLampItem BuildBackendsRow(DevSettings dev)
    {
        var enabled = new List<string>(6);
        if (dev.Roslyn.Enabled) enabled.Add("roslyn");
        if (dev.Build.Enabled) enabled.Add("build");
        if (dev.Debug.Enabled) enabled.Add("debug");
        if (dev.Git.Enabled) enabled.Add("git");
        if (dev.CodebaseIndex.Enabled) enabled.Add("index");
        if (dev.Anui.Enabled) enabled.Add("anui");

        var level = enabled.Count >= 3 ? AnnunciatorLampLevel.Ok : AnnunciatorLampLevel.Caution;
        return new AnnunciatorLampItem(
            EnvironmentReadinessCellIdsCdp.CdpBackends,
            "CDP dev backends",
            enabled.Count == 0
                ? "Все dev-бэкенды выключены в cdp-mcp.toml."
                : $"Включено: {string.Join(", ", enabled)}.",
            level,
            LampShortLabel: "DEV");
    }

    static AnnunciatorLampItem BuildMemoryRow(DevSettings dev)
    {
        // Memory facets are separate from dev toggles; row signals agent-notes MCP readiness path.
        var notesCfg = Environment.GetEnvironmentVariable("AGENT_NOTES_CONFIG")
            ?? Environment.GetEnvironmentVariable("AGENT_NOTES_FILE");
        var level = notesCfg is null ? AnnunciatorLampLevel.Advisory : AnnunciatorLampLevel.Ok;
        return new AnnunciatorLampItem(
            EnvironmentReadinessCellIdsCdp.MemoryBackends,
            "Memory / agent-notes",
            notesCfg is null
                ? "AGENT_NOTES_* не заданы — memory MCP может быть не привязан."
                : "Переменные agent-notes заданы.",
            level,
            LampShortLabel: "MEM");
    }

    static AnnunciatorLampItem BuildSeatRow()
    {
        var seat = IdeIgniteArmHost.Seat;
        var stateRoot = CdpProfile.StateRoot;
        var exists = Directory.Exists(stateRoot);
        return new AnnunciatorLampItem(
            EnvironmentReadinessCellIdsCdp.CdpSeat,
            "CDP seat",
            exists
                ? $"seat={seat}; state={stateRoot}."
                : $"seat={seat}; state root отсутствует: {stateRoot}.",
            exists ? AnnunciatorLampLevel.Ok : AnnunciatorLampLevel.Caution,
            LampShortLabel: "SEAT");
    }

    static AnnunciatorLampItem BuildFreshnessRow()
    {
        try
        {
            var cache = IdeFreshnessCache.Load();
            var count = cache.Entries.Count;
            return new AnnunciatorLampItem(
                EnvironmentReadinessCellIdsCdp.FreshnessCache,
                "KB freshness",
                count == 0
                    ? "Кэш пуст — op=scan на go=freshness."
                    : $"Кэш: {count} URL(ов) с отпечатками.",
                count == 0 ? AnnunciatorLampLevel.Advisory : AnnunciatorLampLevel.Ok,
                LampShortLabel: "FRESH");
        }
        catch
        {
            return new AnnunciatorLampItem(
                EnvironmentReadinessCellIdsCdp.FreshnessCache,
                "KB freshness",
                "Не удалось прочитать freshness-cache.json.",
                AnnunciatorLampLevel.Caution,
                LampShortLabel: "FRESH");
        }
    }

    public static AnnunciatorLampItem BuildCdpSectionRow(IReadOnlyList<AnnunciatorLampItem> children) =>
        EnvironmentReadinessLampRows.BuildSectionRow(
            EnvironmentReadinessCellIdsCdp.CdpSection,
            "CDP habitat",
            children,
            lampShortLabel: "CDP");
}
