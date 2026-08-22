#nullable enable

using System.Reflection;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpPluginQuarantineJarScoreTests
{
    static int Score(string name, bool underLib)
    {
        var method = typeof(CdpPluginQuarantine).GetMethod(
            "ScoreJarName",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (int)method!.Invoke(null, [name, underLib])!;
    }

    [Theory]
    [InlineData("plantuml.jar", false, 120)]
    [InlineData("foo-all.jar", false, 115)]
    [InlineData("foo-cli.jar", false, 112)]
    [InlineData("checkstyle.jar", false, 110)]
    [InlineData("checkstyle-10.jar", false, 108)]
    [InlineData("checkstyle-10.jar", true, 100)]
    [InlineData("my-plugin.jar", false, 45)]
    [InlineData("misc.jar", false, 95)]
    [InlineData("misc.jar", true, 70)]
    public void ScoreJarName_matches_legacy_tiers(string name, bool underLib, int expected) =>
        Assert.Equal(expected, Score(name, underLib));
}
