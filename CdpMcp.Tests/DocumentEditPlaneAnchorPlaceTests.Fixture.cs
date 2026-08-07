using Xunit;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp.Tests;
public sealed partial class DocumentEditPlaneAnchorPlaceTests
{
    [Fact]
    public async Task Place_after_with_T_inserts_after_needle_not_member_end()
    {
        const string body = """
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
        var json = await fx.EditAnchorAsync(place: "after", text: "\n                    // after-early", anchor: "[F:RouteMap.cs;M:RouteOne;T:return \"early\";]");
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
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => fx.EditAnchorAsync(place: "after", text: "\n    // nope", anchor: "[F:SceneMap.cs;M:KeepMe;T:this_needle_is_absent]"));
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
            _session = new SessionContext
            {
                ProjectRoot = dir,
                Language = "csharp"
            };
        }

        public string Path { get; }
        public string Text => File.ReadAllText(Path);

        public static Task<AnchorFixture> CreateAsync(string body, string fileName = "SceneMap.cs")
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cdp-mcp-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, fileName);
            File.WriteAllText(path, body.Replace("\r\n", "\n", StringComparison.Ordinal));
            var fx = new AnchorFixture(dir, path);
            fx._store.Open(path);
            return Task.FromResult(fx);
        }

        public async Task<string> EditAnchorAsync(
            string? place,
            string text,
            string? anchor = null,
            string? oldString = null,
            bool force = false)
        {
            var fileName = System.IO.Path.GetFileName(Path);
            var args = new Dictionary<string, object?>
            {
                ["op"] = "edit",
                ["path"] = Path,
                ["edit_op"] = "anchor",
                ["anchor"] = anchor ?? string.Format("[F:{0};M:KeepMe]", fileName),
                ["text"] = text,
                ["diagnose"] = false,
                ["flush"] = true,
            };
            if (place is not null)
                args["place"] = place;
            if (oldString is not null)
                args["old_string"] = oldString;
            if (force)
                args["force"] = true;
            return await DocumentEditPlane.DispatchAsync("cdp_buffer", _store, _session, _byDomain, ToJsonArgs(args), CancellationToken.None);
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
            return el.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        }
    }
}