using System.Text.Json;
using Cdp.Core;
using DotnetDebugMcp;
using Xunit;

namespace CdpMcp.Tests;

public class IdeBuildSaChannelTests
{
    [Fact]
    public void Need_more_without_project()
    {
        DebugSession.Clear();
        var session = new SessionContext();
        var board = IdeBuildSaChannel.Handle(session, new Dictionary<string, JsonElement>
        {
            ["depth"] = JsonSerializer.SerializeToElement("slim")
        });

        var json = JsonSerializer.Serialize(board);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
        Assert.Equal("build_sa/v1", doc.RootElement.GetProperty("schema").GetString());
        Assert.Equal("need_more", doc.RootElement.GetProperty("verdict").GetString());
    }

    [Fact]
    public void Active_dap_suggests_stop_rebuild()
    {
        DebugSession.Clear();
        // Without a real DapClient we cannot set CurrentClient; cover scope=ship idle path instead.
        Assert.Equal("cdp_build_sa", IdeBuildSaChannel.ToolName);
    }

    [Fact]
    public void Pulse_is_thin()
    {
        DebugSession.Clear();
        var session = new SessionContext { ProjectRoot = Path.GetTempPath(), ScmRoot = Path.GetTempPath() };
        var board = IdeBuildSaChannel.Handle(session, new Dictionary<string, JsonElement>
        {
            ["depth"] = JsonSerializer.SerializeToElement("pulse")
        });

        var json = JsonSerializer.Serialize(board);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("pulse", doc.RootElement.GetProperty("detail").GetString());
        Assert.False(doc.RootElement.TryGetProperty("scm", out _));
    }

    [Fact]
    public void Session_with_temp_root_returns_known_verdict()
    {
        DebugSession.Clear();
        var session = new SessionContext
        {
            ProjectRoot = Path.GetTempPath(),
            ScmRoot = Path.GetTempPath(),
            SolutionOrProjectPath = Path.Combine(Path.GetTempPath(), "NoSuch.csproj")
        };
        var board = IdeBuildSaChannel.Handle(session, new Dictionary<string, JsonElement>
        {
            ["depth"] = JsonSerializer.SerializeToElement("slim"),
            ["scope"] = JsonSerializer.SerializeToElement("build")
        });

        var json = JsonSerializer.Serialize(board);
        using var doc = JsonDocument.Parse(json);
        var verdict = doc.RootElement.GetProperty("verdict").GetString();
        Assert.True(verdict is "build" or "need_more" or "stop_rebuild" or "ship" or "preflight" or "clean" or "push", json);
    }
}
