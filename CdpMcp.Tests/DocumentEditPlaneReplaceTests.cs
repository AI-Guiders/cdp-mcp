using Xunit;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp.Tests;

/// <summary>
/// edit_op=replace: text= must work as new_string= alias; missing body must refuse
/// (lived 2026-08-04: text=-only wiped PublishGlass via silent "").
/// </summary>
public sealed class DocumentEditPlaneReplaceTests
{
    const string FixtureBody =
        """
        before
        KEEP-ME
        after
        """;

    [Fact]
    public async Task Text_alias_replaces_span()
    {
        await using var fx = await ReplaceFixture.CreateAsync(FixtureBody);
        await fx.EditReplaceAsync(oldString: "KEEP-ME", text: "REPLACED", newString: null);

        Assert.Contains("REPLACED", fx.Text, StringComparison.Ordinal);
        Assert.Contains("before", fx.Text, StringComparison.Ordinal);
        Assert.Contains("after", fx.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("KEEP-ME", fx.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_string_primary_replaces_span()
    {
        await using var fx = await ReplaceFixture.CreateAsync(FixtureBody);
        await fx.EditReplaceAsync(oldString: "KEEP-ME", text: null, newString: "VIA-NEW");

        Assert.Contains("VIA-NEW", fx.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("KEEP-ME", fx.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explicit_empty_new_string_deletes_span()
    {
        await using var fx = await ReplaceFixture.CreateAsync(FixtureBody);
        await fx.EditReplaceAsync(oldString: "KEEP-ME\n", text: null, newString: "");

        Assert.DoesNotContain("KEEP-ME", fx.Text, StringComparison.Ordinal);
        Assert.Contains("before", fx.Text, StringComparison.Ordinal);
        Assert.Contains("after", fx.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_body_refuses_without_eating()
    {
        await using var fx = await ReplaceFixture.CreateAsync(FixtureBody);
        var before = fx.Text;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            fx.EditReplaceAsync(oldString: "KEEP-ME", text: null, newString: null));

        Assert.Contains("new_string=", ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, fx.Text);
        Assert.Contains("KEEP-ME", fx.Text, StringComparison.Ordinal);
    }

    sealed class ReplaceFixture : IAsyncDisposable
    {
        readonly string _dir;
        readonly DocumentBufferStore _store = new();
        readonly SessionContext _session;
        readonly Dictionary<string, ICdpBackendModule> _byDomain = new(StringComparer.Ordinal);

        ReplaceFixture(string dir, string path)
        {
            _dir = dir;
            Path = path;
            _session = new SessionContext { ProjectRoot = dir, Language = "csharp" };
        }

        public string Path { get; }

        public string Text => File.ReadAllText(Path);

        public static Task<ReplaceFixture> CreateAsync(string body)
        {
            var dir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cdp-mcp-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "replace.txt");
            File.WriteAllText(path, body.Replace("\r\n", "\n", StringComparison.Ordinal));
            var fx = new ReplaceFixture(dir, path);
            fx._store.Open(path);
            return Task.FromResult(fx);
        }

        public async Task<string> EditReplaceAsync(string oldString, string? text, string? newString)
        {
            var args = new Dictionary<string, object?>
            {
                ["op"] = "edit",
                ["path"] = Path,
                ["edit_op"] = "replace",
                ["old_string"] = oldString,
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
