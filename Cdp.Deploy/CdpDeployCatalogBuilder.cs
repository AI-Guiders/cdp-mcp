using AIGuiders.Platform.Execution.CommandPlane;
using AIGuiders.Platform.IntermediateRepresentation.Command;
using AIGuiders.Platform.Modeling.Notations.Argument;
using Cdp.Deploy.Generated;

namespace Cdp.Deploy;

/// <summary>Builds federation <see cref="CommandCatalogIndex"/> from GDL emit + planet partial (GUIDERS-ADR-0065).</summary>
public static class CdpDeployCatalogBuilder
{
    static readonly ArgumentNotationProfile DeployArgsProfile = new(ArgumentReaders.Kv, []);

    public static CommandCatalogIndex Build() =>
        CommandCatalogIndex.FromDescriptors(Expand());

    static IEnumerable<CommandDescriptor> Expand()
    {
        foreach (var (head, meta) in CdpDeployCatalog.Heads)
        {
            if (!CdpDeployCatalog.WireCommandIds.TryGetValue(head, out var commandId))
                continue;

            yield return CommandDescriptors.Describe(commandId)
                .Domain(CdpDeployCatalog.Planet)
                .Object("deploy")
                .Intent(head)
                .Path(head)
                .Help(head)
                .ArgTail("optional")
                .ArgumentNotation(DeployArgsProfile)
                .Surfaces(CdpDeployCatalog.FederationSurfaces)
                .Scope("ops")
                .Build();
        }
    }
}
