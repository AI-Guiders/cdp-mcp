#nullable enable

using AIGuiders.Platform.Execution.Cockpit.Channels.IdeHealth;
using AIGuiders.Platform.Execution.Cockpit.Channels.IdeHealth.ComputingUnits;
using Cdp.Core;
using CdpMcp.Cockpit.DataBus;

namespace CdpMcp.Cockpit.IdeHealth;

internal static class IdeHealthSnapshotHost
{
    static readonly Lazy<IdeHealthSnapshotUnit> Unit = new(() => new IdeHealthSnapshotUnit(DeskDataBusHost.Current));

    public static IdeHealthOutputSnapshot BuildOutput(SessionContext session)
    {
        IdeHealthDeskProbe.PublishFromHabitat(session, DeskDataBusHost.Current);
        var input = Unit.Value.Build(IdeHealthChannelContext.Default);
        return IdeHealthOutputComposer.Compose(input);
    }
}
