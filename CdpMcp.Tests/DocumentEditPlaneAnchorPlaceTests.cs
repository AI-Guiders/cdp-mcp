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

    [Fact]
    public async Task Place_before_inserts_without_wiping_locus()
    {
        await using var fx = await AnchorFixture.CreateAsync(FixtureBody);
        var json = await fx.EditAnchorAsync(place: "before", text: PulseMember);

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
        var json = await fx.EditAnchorAsync(place: "pre", text: PulseMember);

        Assert.Contains("\"place\": \"before\"", json, StringComparison.Ordinal);
        Assert.Contains("KeepMe", fx.Text, StringComparison.Ordinal);
        Assert.Contains("InsertedPulse", fx.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Place_after_inserts_after_locus()
    {
        await using var fx = await AnchorFixture.CreateAsync(FixtureBody);
        var json = await fx.EditAnchorAsync(
            place: "after",
            text: "\n    public static string AfterPulse() => \"after\";\n");

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

        public static Task<AnchorFixture> CreateAsync(string body)
        {
            var dir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cdp-mcp-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "SceneMap.cs");
            File.WriteAllText(path, body.Replace("\r\n", "\n", StringComparison.Ordinal));
            var fx = new AnchorFixture(dir, path);
            fx._store.Open(path);
            return Task.FromResult(fx);
        }

        public async Task<string> EditAnchorAsync(string? place, string text)
        {
            var fileName = System.IO.Path.GetFileName(Path);
            var args = new Dictionary<string, object?>
            {
                ["op"] = "edit",
                ["path"] = Path,
                ["edit_op"] = "anchor",
                ["anchor"] = $"[F:{fileName};M:KeepMe]",
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
