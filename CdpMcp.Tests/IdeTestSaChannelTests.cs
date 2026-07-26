using System.Text.Json;
using Cdp.Core;
using DotNetBuildTest.Core;
using Xunit;

namespace CdpMcp.Tests;

public class IdeTestSaChannelTests
{
    [Fact]
    public void No_last_run_suggests_discover()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-test-sa-");
        try
        {
            var proj = Path.Combine(tmp.FullName, "Toy.csproj");
            File.WriteAllText(proj, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>\n");
            TestRunCache.Clear(proj);

            var session = new SessionContext
            {
                ProjectRoot = tmp.FullName,
                SolutionOrProjectPath = proj
            };
            var board = IdeTestSaChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["depth"] = JsonSerializer.SerializeToElement("slim")
            });

            var json = JsonSerializer.Serialize(board);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
            Assert.Equal("test_sa/v1", doc.RootElement.GetProperty("schema").GetString());
            Assert.Equal("discover", doc.RootElement.GetProperty("verdict").GetString());
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Failed_last_run_suggests_retest()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-test-sa-fail-");
        try
        {
            var proj = Path.Combine(tmp.FullName, "Toy.csproj");
            File.WriteAllText(proj, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>\n");
            TestRunCache.Remember(
                proj,
                success: false,
                total: 2,
                passed: 1,
                failed: 1,
                skipped: 0,
                failedTests: [("Toy.Fail", "boom", 12)],
                filter: null);

            var session = new SessionContext
            {
                ProjectRoot = tmp.FullName,
                SolutionOrProjectPath = proj
            };
            var board = IdeTestSaChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["depth"] = JsonSerializer.SerializeToElement("slim")
            });

            var json = JsonSerializer.Serialize(board);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("retest", doc.RootElement.GetProperty("verdict").GetString());
        }
        finally
        {
            TestRunCache.Clear();
            try { tmp.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Pulse_is_thin()
    {
        var session = new SessionContext { ProjectRoot = Path.GetTempPath() };
        var board = IdeTestSaChannel.Handle(session, new Dictionary<string, JsonElement>
        {
            ["depth"] = JsonSerializer.SerializeToElement("pulse")
        });
        var json = JsonSerializer.Serialize(board);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("pulse", doc.RootElement.GetProperty("detail").GetString());
        Assert.False(doc.RootElement.TryGetProperty("last_run", out _));
    }

    [Fact]
    public void ToolName_is_cdp_test_sa() =>
        Assert.Equal("cdp_test_sa", IdeTestSaChannel.ToolName);
}
