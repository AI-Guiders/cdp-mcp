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
    [Fact]
    public async Task End_column_beyond_line_clamps_to_line_end()
    {
        await using var fx = await RangeFixture.CreateAsync(FixtureBody);
        await fx.EditReplaceRangeAsync(
            startLine: 2,
            startColumn: 1,
            endLine: 2,
            endColumn: 999,
            text: "REPLACED",
            newString: null);

        Assert.Equal(
            "line-one\nREPLACED\nline-three",
            fx.Text.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task End_line_beyond_buffer_clamps_to_eof()
    {
        await using var fx = await RangeFixture.CreateAsync(FixtureBody);
        await fx.EditReplaceRangeAsync(
            startLine: 1,
            startColumn: 1,
            endLine: 999,
            endColumn: 1,
            text: "ALL",
            newString: null);

        Assert.Equal("ALL", fx.Text);
    }

    [Fact]
    public async Task End_clamp_keeps_trailing_newline_on_whole_line_replace()
    {
        await using var fx = await RangeFixture.CreateAsync("one\ntwo\n");
        await fx.EditReplaceRangeAsync(
            startLine: 2,
            startColumn: 1,
            endLine: 2,
            endColumn: 999,
            text: "TWO",
            newString: null);

        Assert.Equal(
            "one\nTWO\n",
            fx.Text.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_beyond_buffer_still_refuses()
    {
        await using var fx = await RangeFixture.CreateAsync(FixtureBody);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            fx.EditReplaceRangeAsync(
                startLine: 99,
                startColumn: 1,
                endLine: 100,
                endColumn: 1,
                text: "X",
                newString: null));

        Assert.Contains("past end", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CountLines_physical_lines_trailing_newline_adds_none()
    {
        Assert.Equal(2, BufferTextMath.CountLines("a\nb\n"));
        Assert.Equal(2, BufferTextMath.CountLines("a\nb"));
        Assert.Equal(3, BufferTextMath.CountLines("a\nb\nc"));
        Assert.Equal(1, BufferTextMath.CountLines("x"));
        Assert.Equal(1, BufferTextMath.CountLines(""));
    }

    [Fact]
    public void CountLines_parity_with_peek_readlines()
    {
        var dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "cdp-mcp-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "parity.txt");
        File.WriteAllText(path, "one\ntwo\nthree\n");
        try
        {
            Assert.Equal(
                File.ReadLines(path).Count(),
                BufferTextMath.CountLines(File.ReadAllText(path)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

