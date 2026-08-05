using Xunit;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp.Tests;

public sealed partial class DocumentEditPlaneAnchorPlaceTests
{
    const string FixtureBody =
        """
        namespace Fixture;

        internal static class SceneMap
        {
            public static void KeepMe()
            {
            }
        }
        """;

    const string PulseMember =
        """
            public static string InsertedPulse() => "pulse";

        """;

    const string BodyGuard =
        """
                    if (armed) return;

        """;

    [Fact]
    public async Task Place_before_on_method_inserts_sibling_outside()
    {
        await using var fx = await AnchorFixture.CreateAsync(FixtureBody);
        var json = await fx.EditAnchorAsync(place: "before", text: PulseMember);

        Assert.Contains("\"place\": \"before\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("block_body_start", json, StringComparison.Ordinal);
        Assert.Contains("InsertedPulse", fx.Text, StringComparison.Ordinal);
        Assert.Contains("KeepMe", fx.Text, StringComparison.Ordinal);
        Assert.True(
            fx.Text.IndexOf("InsertedPulse", StringComparison.Ordinal)
            < fx.Text.IndexOf("KeepMe", StringComparison.Ordinal),
            fx.Text);
    }

    [Fact]
    public async Task Place_into_on_method_inserts_inside_body()
    {
        await using var fx = await AnchorFixture.CreateAsync(FixtureBody);
        var json = await fx.EditAnchorAsync(place: "into", text: BodyGuard);

        Assert.Contains("\"place\": \"into\"", json, StringComparison.Ordinal);
        Assert.Contains("block_body_start", json, StringComparison.Ordinal);
        Assert.Contains("if (armed) return;", fx.Text, StringComparison.Ordinal);
        Assert.Contains("KeepMe", fx.Text, StringComparison.Ordinal);
        var open = fx.Text.IndexOf("KeepMe()", StringComparison.Ordinal);
        var guard = fx.Text.IndexOf("if (armed) return;", StringComparison.Ordinal);
        var close = fx.Text.IndexOf('}', guard);
        Assert.True(open >= 0 && guard > open && close > guard, fx.Text);
    }

    [Fact]
    public async Task Place_before_on_type_inserts_inside_type_body()
    {
        await using var fx = await AnchorFixture.CreateAsync(FixtureBody);
        var json = await fx.EditAnchorAsync(
            place: "before",
            text: PulseMember,
            anchor: "[F:SceneMap.cs;M:SceneMap]");

        Assert.Contains("\"place\": \"before\"", json, StringComparison.Ordinal);
        Assert.Contains("InsertedPulse", fx.Text, StringComparison.Ordinal);
        Assert.Contains("KeepMe", fx.Text, StringComparison.Ordinal);
        Assert.True(
            fx.Text.IndexOf("InsertedPulse", StringComparison.Ordinal)
            < fx.Text.IndexOf("KeepMe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Place_pre_alias_is_before_sibling()
    {
        await using var fx = await AnchorFixture.CreateAsync(FixtureBody);
        var json = await fx.EditAnchorAsync(place: "pre", text: PulseMember);

        Assert.Contains("\"place\": \"before\"", json, StringComparison.Ordinal);
        Assert.Contains("InsertedPulse", fx.Text, StringComparison.Ordinal);
        Assert.Contains("KeepMe", fx.Text, StringComparison.Ordinal);
        Assert.True(
            fx.Text.IndexOf("InsertedPulse", StringComparison.Ordinal)
            < fx.Text.IndexOf("KeepMe", StringComparison.Ordinal),
            fx.Text);
    }

    [Fact]
    public async Task Place_end_on_method_inserts_before_closing_brace()
    {
        const string body =
            """
            namespace Fixture;

            internal static class SceneMap
            {
                public static void KeepMe()
                {
                    var x = 1;
                }
            }
            """;

        await using var fx = await AnchorFixture.CreateAsync(body);
        var json = await fx.EditAnchorAsync(
            place: "end",
            text: "\n                    var y = 2;");

        Assert.Contains("\"place\": \"end\"", json, StringComparison.Ordinal);
        Assert.Contains("block_body_end", json, StringComparison.Ordinal);
        Assert.Contains("var x = 1;", fx.Text, StringComparison.Ordinal);
        Assert.Contains("var y = 2;", fx.Text, StringComparison.Ordinal);
        var x = fx.Text.IndexOf("var x = 1;", StringComparison.Ordinal);
        var y = fx.Text.IndexOf("var y = 2;", StringComparison.Ordinal);
        var close = fx.Text.IndexOf('}', y);
        Assert.True(x >= 0 && y > x && close > y, fx.Text);
    }

    [Fact]
    public async Task Place_after_on_method_inserts_sibling_outside()
    {
        const string body =
            """
            namespace Fixture;

            internal static class SceneMap
            {
                public static void KeepMe()
                {
                    var x = 1;
                }
            }
            """;

        await using var fx = await AnchorFixture.CreateAsync(body);
        var json = await fx.EditAnchorAsync(
            place: "after",
            text: "\n    public static void AfterSibling() { }\n");

        Assert.Contains("\"place\": \"after\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("block_body_end", json, StringComparison.Ordinal);
        Assert.Contains("var x = 1;", fx.Text, StringComparison.Ordinal);
        Assert.Contains("AfterSibling", fx.Text, StringComparison.Ordinal);
        Assert.True(
            fx.Text.IndexOf("KeepMe", StringComparison.Ordinal)
            < fx.Text.IndexOf("AfterSibling", StringComparison.Ordinal),
            fx.Text);
        var sibling = fx.Text.IndexOf("AfterSibling", StringComparison.Ordinal);
        var methodClose = fx.Text.IndexOf('}', fx.Text.IndexOf("var x = 1;", StringComparison.Ordinal));
        Assert.True(sibling > methodClose, fx.Text);
    }

    [Fact]
    public async Task Place_after_on_type_inserts_sibling_member_at_type_end()
    {
        await using var fx = await AnchorFixture.CreateAsync(FixtureBody);
        var json = await fx.EditAnchorAsync(
            place: "after",
            text: "\n    public static string AfterPulse() => \"after\";\n",
            anchor: "[F:SceneMap.cs;M:SceneMap]");

        Assert.Contains("\"place\": \"after\"", json, StringComparison.Ordinal);
        Assert.Contains("KeepMe", fx.Text, StringComparison.Ordinal);
        Assert.Contains("AfterPulse", fx.Text, StringComparison.Ordinal);
        Assert.True(
            fx.Text.IndexOf("KeepMe", StringComparison.Ordinal)
            < fx.Text.IndexOf("AfterPulse", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Place_replace_overwrites_locus()
    {
        await using var fx = await AnchorFixture.CreateAsync(FixtureBody);
        var json = await fx.EditAnchorAsync(
            place: "replace",
            text: "    public static void Replaced()\n    {\n    }\n");

        Assert.Contains("\"place\": \"replace\"", json, StringComparison.Ordinal);
        Assert.Contains("Replaced", fx.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("KeepMe", fx.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Place_omit_defaults_to_replace()
    {
        await using var fx = await AnchorFixture.CreateAsync(FixtureBody);
        var json = await fx.EditAnchorAsync(
            place: null,
            text: "    public static void DefaultReplace()\n    {\n    }\n");

        Assert.Contains("\"place\": \"replace\"", json, StringComparison.Ordinal);
        Assert.Contains("DefaultReplace", fx.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("KeepMe", fx.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_place_throws_hard_error()
    {
        await using var fx = await AnchorFixture.CreateAsync(FixtureBody);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            fx.EditAnchorAsync(place: "sideways", text: PulseMember));
        Assert.Contains("Unknown place=", ex.Message, StringComparison.Ordinal);
        Assert.Contains("KeepMe", fx.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Place_sniper_on_anchor_throws()
    {
        await using var fx = await AnchorFixture.CreateAsync(FixtureBody);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            fx.EditAnchorAsync(place: "sniper", text: PulseMember));
        Assert.Contains("place=sniper", ex.Message, StringComparison.Ordinal);
        Assert.Contains("KeepMe", fx.Text, StringComparison.Ordinal);
    }

}
