using Xunit;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp.Tests;

public sealed class DocumentEditPlaneAnchorPlaceTests
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
    public async Task Place_before_on_method_inserts_inside_body_not_outside()
    {
        await using var fx = await AnchorFixture.CreateAsync(FixtureBody);
        var json = await fx.EditAnchorAsync(place: "before", text: BodyGuard);

        Assert.Contains("\"place\": \"before\"", json, StringComparison.Ordinal);
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
    public async Task Place_pre_alias_is_before()
    {
        await using var fx = await AnchorFixture.CreateAsync(FixtureBody);
        var json = await fx.EditAnchorAsync(place: "pre", text: BodyGuard);

        Assert.Contains("\"place\": \"before\"", json, StringComparison.Ordinal);
        Assert.Contains("KeepMe", fx.Text, StringComparison.Ordinal);
        Assert.Contains("if (armed) return;", fx.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Place_after_on_method_inserts_before_closing_brace()
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
            text: "\n                    var y = 2;");

        Assert.Contains("\"place\": \"after\"", json, StringComparison.Ordinal);
        Assert.Contains("block_body_end", json, StringComparison.Ordinal);
        Assert.Contains("var x = 1;", fx.Text, StringComparison.Ordinal);
        Assert.Contains("var y = 2;", fx.Text, StringComparison.Ordinal);
        var x = fx.Text.IndexOf("var x = 1;", StringComparison.Ordinal);
        var y = fx.Text.IndexOf("var y = 2;", StringComparison.Ordinal);
        var close = fx.Text.IndexOf('}', y);
        Assert.True(x >= 0 && y > x && close > y, fx.Text);
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

    [Fact]
    public async Task Place_after_with_T_inserts_after_needle_not_member_end()
    {
        const string body =
            """
            namespace Fixture;

            internal static class SceneMap
            {
                public static string RouteOne(string raw)
                {
                    if (raw == "a")
                        return "early";
                    return "tail";
                }
            }
            """;

        await using var fx = await AnchorFixture.CreateAsync(body, fileName: "RouteMap.cs");
        var json = await fx.EditAnchorAsync(
            place: "after",
            text: "\n                    // after-early",
            anchor: "[F:RouteMap.cs;M:RouteOne;T:return \"early\";]");

        Assert.Contains("\"place\": \"after\"", json, StringComparison.Ordinal);
        Assert.Contains("return \"early\";", fx.Text, StringComparison.Ordinal);
        var early = fx.Text.IndexOf("return \"early\";", StringComparison.Ordinal);
        var mark = fx.Text.IndexOf("// after-early", StringComparison.Ordinal);
        var tail = fx.Text.IndexOf("return \"tail\";", StringComparison.Ordinal);
        Assert.True(early >= 0 && mark >= 0 && tail >= 0, $"loci early={early} mark={mark} tail={tail}\n{fx.Text}");
        Assert.True(early < mark && mark < tail, $"order early={early} mark={mark} tail={tail}\n{fx.Text}");
    }

    [Fact]
    public async Task Anchor_T_missing_inside_member_fails_without_mutate()
    {
        await using var fx = await AnchorFixture.CreateAsync(FixtureBody);
        var before = fx.Text;
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            fx.EditAnchorAsync(
                place: "after",
                text: "\n    // nope",
                anchor: "[F:SceneMap.cs;M:KeepMe;T:this_needle_is_absent]"));
        Assert.Contains("text_needle_not_found", ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, fx.Text);
    }

    sealed class AnchorFixture : IAsyncDisposable
    {
        readonly string _dir;
        readonly DocumentBufferStore _store = new();
        readonly SessionContext _session;
        readonly Dictionary<string, ICdpBackendModule> _byDomain = new(StringComparer.Ordinal);

        AnchorFixture(string dir, string path)
        {
            _dir = dir;
            Path = path;
            _session = new SessionContext { ProjectRoot = dir, Language = "csharp" };
        }

        public string Path { get; }

        public string Text => File.ReadAllText(Path);

        public static Task<AnchorFixture> CreateAsync(string body, string fileName = "SceneMap.cs")
        {
            var dir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cdp-mcp-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, fileName);
            File.WriteAllText(path, body.Replace("\r\n", "\n", StringComparison.Ordinal));
            var fx = new AnchorFixture(dir, path);
            fx._store.Open(path);
            return Task.FromResult(fx);
        }

        public async Task<string> EditAnchorAsync(string? place, string text, string? anchor = null)
        {
            var fileName = System.IO.Path.GetFileName(Path);
            var args = new Dictionary<string, object?>
            {
                ["op"] = "edit",
                ["path"] = Path,
                ["edit_op"] = "anchor",
                ["anchor"] = anchor ?? $"[F:{fileName};M:KeepMe]",
                ["text"] = text,
                ["diagnose"] = false,
                ["flush"] = true,
            };
            if (place is not null)
                args["place"] = place;

            return await DocumentEditPlane.DispatchAsync(
                "cdp_buffer",
                _store,
                _session,
                _byDomain,
                ToJsonArgs(args),
                CancellationToken.None);
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (Directory.Exists(_dir))
                    Directory.Delete(_dir, recursive: true);
            }
            catch
            {
                // best-effort temp cleanup
            }

            return ValueTask.CompletedTask;
        }

        static Dictionary<string, JsonElement> ToJsonArgs(Dictionary<string, object?> args)
        {
            var el = JsonSerializer.SerializeToElement(args);
            return el.EnumerateObject().ToDictionary(
                p => p.Name,
                p => p.Value,
                StringComparer.Ordinal);
        }
    }
}
