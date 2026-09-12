using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class DocumentEditPlaneCreateTests
{
    [Fact]
    public async Task Create_with_content_alias_writes_body()
    {
        await using var fx = await CreateFixture.CreateAsync();
        var path = Path.Combine(fx.Dir, "from-content.cs");

        var json = await fx.CreateAsync(path, content: "namespace X;\n");
        var doc = JsonDocument.Parse(json).RootElement;

        Assert.True(doc.GetProperty("ok").GetBoolean());
        Assert.Equal("namespace X;\n", File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Contains("content= alias", doc.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_text_wins_over_content_alias()
    {
        await using var fx = await CreateFixture.CreateAsync();
        var path = Path.Combine(fx.Dir, "text-wins.cs");

        await fx.CreateAsync(path, text: "alpha\n", content: "beta\n");

        Assert.Equal("alpha\n", File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_empty_code_file_pulses_warn()
    {
        await using var fx = await CreateFixture.CreateAsync();
        var path = Path.Combine(fx.Dir, "empty.cs");

        var json = await fx.CreateAsync(path);
        var doc = JsonDocument.Parse(json).RootElement;

        Assert.Equal(0, doc.GetProperty("meta").GetProperty("char_count").GetInt32());
        Assert.Contains("create landed empty", doc.GetProperty("hint").GetString(), StringComparison.Ordinal);
        var quality = doc.GetProperty("quality");
        Assert.Contains("WARN", quality.GetProperty("pulse").GetString(), StringComparison.Ordinal);
        Assert.Equal("create_empty", quality.GetProperty("findings")[0].GetProperty("id").GetString());
    }

    sealed class CreateFixture : IAsyncDisposable
    {
        readonly DocumentBufferStore _store = new();
        readonly SessionContext _session;
        readonly Dictionary<string, ICdpBackendModule> _byDomain = new(StringComparer.Ordinal);

        CreateFixture(string dir)
        {
            Dir = dir;
            _session = new SessionContext { ProjectRoot = dir, Language = "csharp" };
        }

        public string Dir { get; }

        public static Task<CreateFixture> CreateAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "cdp-mcp-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Task.FromResult(new CreateFixture(dir));
        }

        public Task<string> CreateAsync(string path, string? text = null, string? content = null)
        {
            var args = new Dictionary<string, object?> { ["op"] = "create", ["path"] = path, ["diagnose"] = false };
            if (text is not null)
                args["text"] = text;
            if (content is not null)
                args["content"] = content;

            return DocumentEditPlane.DispatchAsync(
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
            return el.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        }
    }
}
