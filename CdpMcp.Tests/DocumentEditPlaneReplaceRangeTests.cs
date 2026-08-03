using Xunit;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp.Tests;

public sealed class DocumentEditPlaneReplaceRangeTests
{
    const string FixtureBody =
        """
        line-one
        KEEP-ME
        line-three
        """;

    [Fact]
    public async Task New_string_alias_replaces_span()
    {
        await using var fx = await RangeFixture.CreateAsync(FixtureBody);
        await fx.EditReplaceRangeAsync(
            startLine: 2,
            startColumn: 1,
            endLine: 3,
            endColumn: 1,
            text: null,
            newString: "REPLACED\n");

        Assert.Contains("REPLACED", fx.Text, StringComparison.Ordinal);
        Assert.Contains("line-one", fx.Text, StringComparison.Ordinal);
        Assert.Contains("line-three", fx.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("KEEP-ME", fx.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Text_empty_deletes_span()
    {
        await using var fx = await RangeFixture.CreateAsync(FixtureBody);
        await fx.EditReplaceRangeAsync(
            startLine: 2,
            startColumn: 1,
            endLine: 3,
            endColumn: 1,
            text: "",
            newString: null);

        Assert.DoesNotContain("KEEP-ME", fx.Text, StringComparison.Ordinal);
        Assert.Contains("line-one", fx.Text, StringComparison.Ordinal);
        Assert.Contains("line-three", fx.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_body_refuses_without_eating()
    {
        await using var fx = await RangeFixture.CreateAsync(FixtureBody);
        var before = fx.Text;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            fx.EditReplaceRangeAsync(
                startLine: 2,
                startColumn: 1,
                endLine: 3,
                endColumn: 1,
                text: null,
                newString: null));

        Assert.Contains("text=", ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, fx.Text);
        Assert.Contains("KEEP-ME", fx.Text, StringComparison.Ordinal);
    }

    sealed class RangeFixture : IAsyncDisposable
    {
        readonly string _dir;
        readonly DocumentBufferStore _store = new();
        readonly SessionContext _session;
        readonly Dictionary<string, ICdpBackendModule> _byDomain = new(StringComparer.Ordinal);

        RangeFixture(string dir, string path)
        {
            _dir = dir;
            Path = path;
            _session = new SessionContext { ProjectRoot = dir, Language = "csharp" };
        }

        public string Path { get; }

        public string Text => File.ReadAllText(Path);

        public static Task<RangeFixture> CreateAsync(string body)
        {
            var dir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cdp-mcp-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "range.txt");
            File.WriteAllText(path, body.Replace("\r\n", "\n", StringComparison.Ordinal));
            var fx = new RangeFixture(dir, path);
            fx._store.Open(path);
            return Task.FromResult(fx);
        }

        public async Task<string> EditReplaceRangeAsync(
            int startLine,
            int startColumn,
            int endLine,
            int endColumn,
            string? text,
            string? newString)
        {
            var args = new Dictionary<string, object?>
            {
                ["op"] = "edit",
                ["path"] = Path,
                ["edit_op"] = "replace_range",
                ["start_line"] = startLine,
                ["start_column"] = startColumn,
                ["end_line"] = endLine,
                ["end_column"] = endColumn,
                ["diagnose"] = false,
                ["flush"] = true,
            };
            if (text is not null)
                args["text"] = text;
            if (newString is not null)
                args["new_string"] = newString;

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
