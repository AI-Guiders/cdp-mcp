#nullable enable

using CdpMcp.Habitat;

namespace CdpMcp;

internal static partial class CdpPluginQuarantine
{
    readonly record struct JarScoreContext(string NameLower, bool UnderLib);

    static int ScoreJarName(string name, bool underLib) =>
        JarScoreRuleChain.Score(new JarScoreContext(name.ToLowerInvariant(), underLib));

    static class JarScoreRuleChain
    {
        static readonly IRule<JarScoreContext, int>[] Ordered =
        [
            new PlantUmlJarScoreRule(),
            new FatJarScoreRule(),
            new CliJarScoreRule(),
            new CanonicalLintJarScoreRule(),
            new LintPrefixJarScoreRule(),
            new PluginJarScoreRule(),
            new DefaultJarScoreRule(),
        ];

        internal static int Score(JarScoreContext context) =>
            RuleChain.FirstMatch(context, Ordered);
    }

    sealed class PlantUmlJarScoreRule : IRule<JarScoreContext, int>
    {
        public bool Applies(JarScoreContext context) => context.NameLower.Contains("plantuml");
        public int Select(JarScoreContext context) => 120;
    }

    sealed class FatJarScoreRule : IRule<JarScoreContext, int>
    {
        public bool Applies(JarScoreContext context) =>
            context.NameLower.EndsWith("-all.jar") || context.NameLower.Contains("-all-");
        public int Select(JarScoreContext context) => 115;
    }

    sealed class CliJarScoreRule : IRule<JarScoreContext, int>
    {
        public bool Applies(JarScoreContext context) => context.NameLower.Contains("cli");
        public int Select(JarScoreContext context) => 112;
    }

    sealed class CanonicalLintJarScoreRule : IRule<JarScoreContext, int>
    {
        public bool Applies(JarScoreContext context) =>
            context.NameLower is "spotbugs.jar" or "checkstyle.jar" or "pmd.jar";
        public int Select(JarScoreContext context) => 110;
    }

    sealed class LintPrefixJarScoreRule : IRule<JarScoreContext, int>
    {
        public bool Applies(JarScoreContext context) =>
            context.NameLower.StartsWith("checkstyle")
            || context.NameLower.StartsWith("pmd-")
            || context.NameLower.StartsWith("spotbugs");
        public int Select(JarScoreContext context) => context.UnderLib ? 100 : 108;
    }

    sealed class PluginJarScoreRule : IRule<JarScoreContext, int>
    {
        public bool Applies(JarScoreContext context) => context.NameLower.Contains("plugin");
        public int Select(JarScoreContext context) => 45;
    }

    sealed class DefaultJarScoreRule : IRule<JarScoreContext, int>
    {
        public bool Applies(JarScoreContext context) => true;
        public int Select(JarScoreContext context) => context.UnderLib ? 70 : 95;
    }
}
