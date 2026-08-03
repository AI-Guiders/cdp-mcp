using Xunit;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp.Tests;

public sealed class DocumentEditPlaneSetTextRefuseTests
{
    const string FixtureBody = "alpha\nbeta\n";

    [Fact]
    public async Task Set_text_on_existing_refuses_without_mutate()
    {
        await using var fx = await SetTextFixture.CreateAsync(FixtureBody);
        var before = fx.Text;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fx.EditSetTextAsync("rewritten\n", force: false));

        Assert.Contains("ADX-HX-001", ex.Message, StringComparison.Ordinal);
        Assert.Contains("force=true", ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, fx.Text);
    }

    [Fact]
    public async Task Set_text_on_existing_with_force_rewrites()
    {
        await using var fx = await SetTextFixture.CreateAsync(FixtureBody);

        await fx.EditSetTextAsync("forced\n", force: true);

        Assert.Equal("forced\n", fx.Text);
    }

    [Fact]
    public async Task Set_text_on_missing_path_still_needs_create()
    {
        await using var fx = await SetTextFixture.CreateAsync(FixtureBody);
        var missing = System.IO.Path.Combine(fx.Dir, "brand-new.txt");

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            fx.EditSetTextAsync("first\n", force: false, path: missing));

        Assert.Contains("brand-new.txt", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(missing));
    }

    sealed class SetTextFixture : IAsyncDisposable
    {
        readonly DocumentBufferStore _store = new();
        readonly SessionContext _session;
        readonly Dictionary<string, ICdpBackendModule> _byDomain = new(StringComparer.Ordinal);

        SetTextFixture(string dir, string path)
        {
            Dir = dir;
            Path = path;
            _session = new SessionContext { ProjectRoot = dir, Language = "csharp" };
        }

        public string Dir { get; }

        public string Path { get; }

        public string Text => File.ReadAllText(Path);

        public static Task<SetTextFixture> CreateAsync(string body)
        {
            var dir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cdp-mcp-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "existing.txt");
            File.WriteAllText(path, body.Replace("\r\n", "\n", StringComparison.Ordinal));
            var fx = new SetTextFixture(dir, path);
            fx._store.Open(path);
            return Task.FromResult(fx);
        }

        public async Task<string> EditSetTextAsync(string text, bool force, string? path = null)
        {
            var args = new Dictionary<string, object?>
            {
                ["op"] = "edit",
                ["path"] = path ?? Path,
                ["edit_op"] = "set_text",
                ["text"] = text,
                ["force"] = force,
                ["diagnose"] = false,
                ["flush"] = true,
                ["allow_shrink"] = true,
            };

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
                if (Directory.Exists(Dir))
                    Directory.Delete(Dir, recursive: true);
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
