using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class QualityGatesDiskTests
{
    [Fact]
    public void EvaluateDisk_maps_warn_and_near_miss_without_open_buffers()
    {
        var root = NewTemp("qg-disk");
        try
        {
            WriteOverlay(root, warn: 100, fail: 500);
            File.WriteAllText(Path.Combine(root, "Fat.cs"), Lines(120));
            File.WriteAllText(Path.Combine(root, "Near.cs"), Lines(70));
            File.WriteAllText(Path.Combine(root, "Slim.cs"), Lines(10));
            Directory.CreateDirectory(Path.Combine(root, "obj"));
            File.WriteAllText(Path.Combine(root, "obj", "Ignored.cs"), Lines(200));

            QualityGates.InvalidateCache();
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(QualityGates.EvaluateDisk(root, limit: 20)));
            var rootEl = doc.RootElement;
            Assert.Equal("disk", rootEl.GetProperty("scope").GetString());
            Assert.True(rootEl.GetProperty("ok").GetBoolean());
            Assert.Equal(1, rootEl.GetProperty("warn").GetInt32());
            Assert.True(rootEl.GetProperty("near_miss").GetInt32() >= 1);

            var ids = rootEl.GetProperty("findings").EnumerateArray()
                .Select(f => f.GetProperty("id").GetString())
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("file_lines", ids);
            Assert.Contains("file_lines_near_miss", ids);
        }
        finally
        {
            TryDelete(root);
            QualityGates.InvalidateCache();
        }
    }

    static void WriteOverlay(string root, int warn, int fail)
    {
        var dir = Path.Combine(root, ".cdp");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "quality-gates.toml"), $"""
            [quality]
            enabled = true
            mode = "warn"

            [quality.gates]
            file_lines_warn = {warn}
            file_lines_fail = {fail}
            method_lines_warn = 70
            method_lines_fail = 120
            suggest_sniper_file_lines = 0
            """);
    }

    static string Lines(int n) => string.Join('\n', Enumerable.Repeat("// line", n)) + "\n";

    static string NewTemp(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-mcp-tests", prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }
}
