using AIGuiders.Platform.Execution.Language;
using AIGuiders.Platform.Language.CSharp;
using AIGuiders.Platform.Language.Sql;
using AIGuiders.Platform.Modeling.Language.Adapters.Fcs;
using AIGuiders.Platform.Modeling.Language.Adapters.Gdl;
using DashSpec.Modeling.Language.Adapters.DashSpec;

namespace CdpMcp;

/// <summary>LRC host wiring for CDP (GUIDERS-ADR-0061 / CDP-ADR-0208).</summary>
internal static class CdpLanguageResolverHost
{
    private static readonly Lazy<LanguageResolverCenter> Lazy = new(Build);

    public static LanguageResolverCenter Center => Lazy.Value;

    static LanguageResolverCenter Build() =>
        new LanguageResolverBuilder()
            .WithActivation(new PathRulesLanguageActivationCatalog())
            .Register(new FcsLanguageBackend(null))
            .Register(new GdlLanguageBackend())
            .Register(new CsharpLanguageBackend())
            .Register(new SqlPostgresLanguageBackend())
            .Register(new SqlMssqlLanguageBackend())
            .Register(new SqlLanguageBackend())
            .Register(new DashSpecLanguageBackend())
            .Build();
}
