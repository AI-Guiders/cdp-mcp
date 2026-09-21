using AIGuiders.Platform.Execution.Language;
using DashSpec.Modeling.Language.Adapters.DashSpec;

namespace CdpMcp;

/// <summary>LRC host wiring for CDP (GUIDERS-ADR-0061 / CDP-ADR-0208).</summary>
internal static class CdpLanguageResolverHost
{
    private static readonly Lazy<LanguageResolverCenter> Lazy = new(Build);

    public static LanguageResolverCenter Center => Lazy.Value;

    static LanguageResolverCenter Build()
    {
        var families = StandardLanguageFamilyManifest
            .LoadFederation(AppContext.BaseDirectory)
            .ToList();

        return LanguageFamilyResolverHost.Create(
            families,
            new LanguageFamilyActivationCatalog(families),
            builder => builder.Register(new DashSpecLanguageBackend()));
    }
}
