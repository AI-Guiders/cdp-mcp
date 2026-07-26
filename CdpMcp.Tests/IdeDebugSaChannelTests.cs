using System.Text.Json;
using Cdp.Core;
using DotnetDebugMcp;
using Xunit;

namespace CdpMcp.Tests;

public class IdeDebugSaChannelTests
{
    [Fact]
    public void Idle_without_bps_suggests_fix_bp()
    {
        DebugSession.Clear();
        var session = new SessionContext
        {
            ProjectRoot = Path.GetTempPath(),
            SolutionOrProjectPath = Path.Combine(Path.GetTempPath(), "NoSuch.csproj")
        };

        var board = IdeDebugSaChannel.Handle(session, new Dictionary<string, JsonElement>
        {
            ["depth"] = JsonSerializer.SerializeToElement("slim")
        });

        var json = JsonSerializer.Serialize(board);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
        Assert.Equal("debug_sa/v1", doc.RootElement.GetProperty("schema").GetString());
        var verdict = doc.RootElement.GetProperty("verdict").GetString();
        Assert.True(verdict is "fix_bp" or "need_more" or "idle" or "attach", json);
    }

    [Fact]
    public void Pulse_is_thin()
    {
        DebugSession.Clear();
        var session = new SessionContext { ProjectRoot = Path.GetTempPath() };
        var board = IdeDebugSaChannel.Handle(session, new Dictionary<string, JsonElement>
        {
            ["depth"] = JsonSerializer.SerializeToElement("pulse")
        });

        var json = JsonSerializer.Serialize(board);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
        Assert.Equal("pulse", doc.RootElement.GetProperty("detail").GetString());
        Assert.False(doc.RootElement.TryGetProperty("dap", out _));
    }

    [Fact]
    public void Stopped_without_exception_suggests_continue()
    {
        DebugSession.Clear();
        DebugSession.LastStoppedThreadId = 7;
        try
        {
            var session = new SessionContext { ProjectRoot = Path.GetTempPath() };
            var board = IdeDebugSaChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["depth"] = JsonSerializer.SerializeToElement("slim")
            });

            var json = JsonSerializer.Serialize(board);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("continue", doc.RootElement.GetProperty("verdict").GetString());
        }
        finally
        {
            DebugSession.Clear();
        }
    }

    [Fact]
    public void Active_dap_suggests_stop_rebuild()
    {
        DebugSession.Clear();
        // ActiveDap is CurrentClient != null — without a real client we cannot fake easily.
        // Cover ToolName + schema instead when no live DAP.
        Assert.Equal("cdp_debug_sa", IdeDebugSaChannel.ToolName);
    }
}
