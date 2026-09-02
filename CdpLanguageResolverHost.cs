using AIGuiders.Platform.Execution.Language;
using AIGuiders.Platform.Modeling.Language.Adapters.Fcs;
using AIGuiders.Platform.Modeling.Language.Adapters.Gdl;

namespace CdpMcp;

/// <summary>Federation LRC host wiring for CDP (GUIDERS-ADR-0061 / CDP-ADR-0208).</summary>
internal static class CdpLanguageResolverHost
{
    private static readonly Lazy<LanguageResolverCenter> Lazy = new(Build);

    public static LanguageResolverCenter Center => Lazy.Value;

    static LanguageResolverCenter Build()
    {
        return new LanguageResolverBuilder()
            .Register(new FcsLanguageBackend(projectOptionsSource: null))
            .Register(new GdlLanguageBackend())
            .Build();
    }
}
