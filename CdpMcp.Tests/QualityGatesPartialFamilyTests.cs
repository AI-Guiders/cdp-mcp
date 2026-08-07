using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class QualityGatesPartialFamilyTests
{
    [Fact]
    public void Partial_family_warns_when_many_peels_silence_file_lines()
    {
        var root = NewTemp("qg-partial");
        try
        {
            WriteOverlay(root, warn: 100, fail: 500, familyWarn: 4);
            File.WriteAllText(Path.Combine(root, "Monster.cs"), Lines(20));
            File.WriteAllText(Path.Combine(root, "Monster.A.cs"), Lines(20));
            File.WriteAllText(Path.Combine(root, "Monster.B.cs"), Lines(20));
            File.WriteAllText(Path.Combine(root, "Monster.C.cs"), Lines(20));

            QualityGates.InvalidateCache();
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(QualityGates.EvaluateDisk(root, limit: 40)));
            var ids = doc.RootElement.GetProperty("findings").EnumerateArray()
                .Select(f => f.GetProperty("id").GetString())
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("partial_family", ids);

            var hit = doc.RootElement.GetProperty("findings").EnumerateArray()
                .First(f => f.GetProperty("id").GetString() == "partial_family");
            Assert.Equal("warn", hit.GetProperty("severity").GetString());
            Assert.Equal("Monster", hit.GetProperty("symbol").GetString());
            Assert.True(hit.GetProperty("message").GetString()!.Contains("partial ≠ split", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
            QualityGates.InvalidateCache();
        }
    }

    [Fact]
    public void Partial_family_skips_solo_file()
    {
        var root = NewTemp("qg-solo");
        try
        {
            WriteOverlay(root, warn: 50, fail: 500, familyWarn: 4);
            File.WriteAllText(Path.Combine(root, "Solo.cs"), Lines(80));

            QualityGates.InvalidateCache();
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(QualityGates.EvaluateDisk(root, limit: 20)));
            var ids = doc.RootElement.GetProperty("findings").EnumerateArray()
                .Select(f => f.GetProperty("id").GetString())
                .ToHashSet(StringComparer.Ordinal);
            Assert.DoesNotContain("partial_family", ids);
            Assert.Contains("file_lines", ids);
        }
        finally
        {
            TryDelete(root);
            QualityGates.InvalidateCache();
        }
    }

    static void WriteOverlay(string root, int warn, int fail, int familyWarn)
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
            partial_family_files_warn = {familyWarn}
            partial_family_files_fail = 0
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
