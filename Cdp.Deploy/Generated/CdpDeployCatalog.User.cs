namespace Cdp.Deploy.Generated;

/// <summary>
/// Planet partial — deploy head metadata not yet emitted by CatalogCatalogEmitter (v1).
/// Source of truth: authoring/catalog/deploy.catalog.gdl commands table.
/// </summary>
public static partial class CdpDeployCatalog
{
    public static readonly IReadOnlyDictionary<string, DeployHeadMeta> Heads =
        new Dictionary<string, DeployHeadMeta>(StringComparer.OrdinalIgnoreCase)
        {
            ["deploy"] = new("deploy", CdpDeployMode.Ship),
            ["hard_deploy"] = new("hard_deploy", CdpDeployMode.Hard),
            ["soft_deploy"] = new("soft_deploy", CdpDeployMode.Soft),
        };

    public readonly record struct DeployHeadMeta(string Go, CdpDeployMode DefaultMode);
}
