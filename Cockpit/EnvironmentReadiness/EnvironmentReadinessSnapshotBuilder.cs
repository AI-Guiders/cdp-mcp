#nullable enable

using AIGuiders.Platform.Cockpit.Channels.EnvironmentReadiness;
using AIGuiders.Platform.Cockpit.Channels.Primitives;
using AIGuiders.Platform.Cockpit.DataBus;
using CdpMcp.Cockpit.DataAcquisition;
using CdpMcp.Cockpit.DataBus;

namespace CdpMcp.Cockpit.EnvironmentReadiness;

/// <summary>Headless ER snapshot: CIDE quarry + CDP habitat rows (ADR-0002).</summary>
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
        var env = EnvironmentReadinessEnvSnapshot.FromProcess(ctx.Settings.AgentNotesConfigPath);
        var agent = EnvironmentReadinessLampRows.BuildAgentRow(ctx.IsMcpStdioHost, ctx.ActiveAiProvider);
        var envRows = EnvironmentReadinessLampRows.BuildEnvProbeRows(env, ctx.Settings.AgentNotesConfigPath);
        var lspRows = EnvironmentReadinessLampRows.BuildLspRows(input.Dev, ctx.Lsp);
        var dotnet = await EnvironmentReadinessLampRows.ProbeDotnetAsync(cancellationToken).ConfigureAwait(false);

        var devDetails = new List<AnnunciatorLampItem>(1 + lspRows.Count + 1) { agent };
        devDetails.AddRange(lspRows);
        devDetails.Add(dotnet);

        var cdpDetails = EnvironmentReadinessCdpRows.Build(input.Dev, input.CockpitHost, input.Service);

        var rows = new List<AnnunciatorLampItem>(devDetails.Count + envRows.Count + cdpDetails.Count + 4);
        rows.Add(EnvironmentReadinessLampRows.BuildDevToolsSectionRow(devDetails));
        rows.AddRange(devDetails);
        rows.Add(EnvironmentReadinessLampRows.BuildEnvSectionRow(envRows));
        rows.AddRange(envRows);
        rows.Add(EnvironmentReadinessCdpRows.BuildCdpSectionRow(cdpDetails));
        rows.AddRange(cdpDetails);

        PublishLspState(ctx.Lsp);
        return new EnvironmentReadinessSnapshot(rows);
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
