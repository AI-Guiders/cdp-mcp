using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>ADX-HX-003: replace_all must distinguish "regex invalid" and diagnose zero matches.</summary>
public sealed class EditorComfortReplaceAllTests
{
    static async Task<string> ReplaceAllAsync(string text, string query, string replacement, bool regex)
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-mcp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Doc.txt");
        File.WriteAllText(path, text);

        try
        {
            var store = new DocumentBufferStore();
            store.Open(path);
            var session = new SessionContext { ProjectRoot = dir, Language = "text" };
            var el = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["op"] = "replace_all",
                ["path"] = path,
                ["query"] = query,
                ["text"] = replacement,
                ["regex"] = regex,
            });
            var args = el.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
            return await DocumentEditPlane.DispatchAsync(
                "cdp_buffer", store, session, new Dictionary<string, ICdpBackendModule>(), args, CancellationToken.None);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // best-effort temp cleanup
            }
        }
    }

    [Fact]
    public async Task Regex_invalid_returns_error_not_no_matches()
    {
        var json = await ReplaceAllAsync("alpha beta\r\n", "(unclosed", "x", regex: true);
        Assert.Contains("\"error\": \"regex_invalid\"", json, StringComparison.Ordinal);
        Assert.Contains("Pattern failed to compile", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zero_matches_on_crlf_anchors_hint_line_endings()
    {
        var json = await ReplaceAllAsync("alpha \r\nbeta\r\n", "^alpha [ \t]*$", "x", regex: true);
        Assert.Contains("\"replaced\": 0", json, StringComparison.Ordinal);
        Assert.Contains("CRLF", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zero_matches_with_absent_literal_hint_wrong_text()
    {
        var json = await ReplaceAllAsync("alpha beta\r\n", "zzz (\\d+)", "x", regex: true);
        Assert.Contains("absent from the buffer", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zero_matches_with_present_literal_hint_pattern_fault()
    {
        var json = await ReplaceAllAsync("alpha beta\r\n", "alpha (\\d+)", "x", regex: true);
        Assert.Contains("IS present", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Backreference_replacement_works()
    {
        var json = await ReplaceAllAsync("filter date usage\r\n", "(filter) (date)", "$2-$1", regex: true);
        Assert.Contains("\"replaced\": 1", json, StringComparison.Ordinal);
    }
}
