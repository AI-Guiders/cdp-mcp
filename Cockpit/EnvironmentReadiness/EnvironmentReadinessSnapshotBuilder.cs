#nullable enable

using AIGuiders.Platform.Cockpit.Channels.EnvironmentReadiness;
using AIGuiders.Platform.Cockpit.Channels.EnvironmentReadiness.ComputingUnits;
using AIGuiders.Platform.Cockpit.Channels.EnvironmentReadiness.DataAcquisition;
using AIGuiders.Platform.Cockpit.Channels.Primitives;
using AIGuiders.Platform.Cockpit.DataBus;
using CdpMcp.Cockpit.DataBus;
using Cdp.Core;

namespace CdpMcp.Cockpit.EnvironmentReadiness;

/// <summary>Headless ER snapshot: platform W4 kit + CDP habitat rows (ADR-0002).</summary>
internal static class EnvironmentReadinessSnapshotBuilder
{
    public readonly record struct Input(
        EnvironmentReadinessChannelContext Channel,
        DevSettings Dev,
        CdpServiceSettings Service,
        CockpitHostSettings CockpitHost);

    public static async Task<EnvironmentReadinessSnapshot> BuildAsync(
        Input input,
        CancellationToken cancellationToken = default)
    {
        var ctx = input.Channel;
        var csharp = new EnvironmentReadinessCSharpProbeOptions(
            input.Dev.Roslyn.Enabled,
            "CDP: диагностика C# через in-process Roslyn (cdp_health backends). Внешний csharp-ls не обязателен.");

        var core = await EnvironmentReadinessSnapshotUnit.BuildCoreAsync(
            new EnvironmentReadinessSnapshotUnit.Input(
                ctx,
                csharp,
                "Не задан: укажи memory.notes_config в cdp-mcp.toml (тот же файл, что --config в mcp.json)."),
            cancellationToken).ConfigureAwait(false);

        var cdpDetails = EnvironmentReadinessCdpRows.Build(input.Dev, input.CockpitHost, input.Service);
        var extension = new List<AnnunciatorLampItem>(cdpDetails.Count + 1)
        {
            EnvironmentReadinessCdpRows.BuildCdpSectionRow(cdpDetails),
        };
        extension.AddRange(cdpDetails);

        PublishLspState(ctx.Lsp);
        return EnvironmentReadinessSnapshotUnit.MergeExtension(core, extension);
    }

    static void PublishLspState(IdeHostStateChanged lsp)
    {
        try
        {
            DeskDataBusHost.Current.Publish(lsp);
        }
        catch
        {
            /* bus optional in tests */
        }
    }
}
