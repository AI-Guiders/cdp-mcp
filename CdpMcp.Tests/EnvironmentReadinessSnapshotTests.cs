#nullable enable

using System.Text.Json;
using AIGuiders.Platform.Execution.Cockpit.Channels.EnvironmentReadiness;
using AIGuiders.Platform.Modeling.Cockpit.DataBus;
using Cdp.Core;
using CdpMcp.Cockpit.DataBus;
using AIGuiders.Platform.Execution.Cockpit.Channels.EnvironmentReadiness.DataAcquisition;
using CdpMcp.Cockpit.EnvironmentReadiness;
using Xunit;

namespace CdpMcp.Tests;

public sealed class EnvironmentReadinessSnapshotTests
{
    [Fact]
    public void PathAcquisition_unset_agent_notes_is_ok()
    {
        var kind = EnvironmentReadinessPathAcquisition.ClassifyAgentNotesFilePath(null);
        Assert.Equal(AgentNotesFilePathKind.Unset, kind);
    }

    [Fact]
    public async Task BuildAsync_includes_cdp_section_rows()
    {
        var ctx = new EnvironmentReadinessChannelContext(
            new EnvironmentReadinessSettings(null),
            null,
            new IdeHostStateChanged
            {
                CSharpLspProcessActive = false,
                MarkdownLspProcessActive = false,
                CSharpLspHostPresent = false,
                MarkdownLspHostPresent = false,
            },
            IsMcpStdioHost: true);
        var input = new EnvironmentReadinessSnapshotBuilder.Input(
            ctx,
            new DevSettings(),
            new CdpServiceSettings(),
            new CockpitHostSettings());
        var snap = await EnvironmentReadinessSnapshotBuilder.BuildAsync(input);
        Assert.Contains(snap.Rows, r => r.Id == EnvironmentReadinessCellIdsCdp.CdpSection);
        Assert.Contains(snap.Rows, r => r.Id == EnvironmentReadinessCellIdsCdp.CdpBackends);
        Assert.Contains(snap.Rows, r => r.Id == EnvironmentReadinessCellIds.Agent);
    }

    [Fact]
    public void IdeEnvironmentReadinessChannel_scene_returns_json()
    {
        var session = new SessionContext();
        var json = IdeEnvironmentReadinessChannel.HandleJson(session);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("scene", doc.RootElement.GetProperty("op").GetString());
        Assert.True(doc.RootElement.GetProperty("row_count").GetInt32() > 5);
    }

    [Fact]
    public async Task Publish_lsp_state_on_build()
    {
        IdeHostStateChanged? got = null;
        using var sub = DeskDataBusHost.Current.Subscribe<IdeHostStateChanged>(e => got = e);
        var ctx = new EnvironmentReadinessChannelContext(
            new EnvironmentReadinessSettings(null),
            null,
            new IdeHostStateChanged
            {
                CSharpLspProcessActive = true,
                MarkdownLspProcessActive = false,
                CSharpLspHostPresent = true,
                MarkdownLspHostPresent = false,
            });
        var input = new EnvironmentReadinessSnapshotBuilder.Input(
            ctx, new DevSettings(), new CdpServiceSettings(), new CockpitHostSettings());
        await EnvironmentReadinessSnapshotBuilder.BuildAsync(input);
        Assert.NotNull(got);
        Assert.True(got!.CSharpLspProcessActive);
    }
}
