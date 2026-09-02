#if CDP_FEDERATION_IDE_SESSION
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public class FederationSessionBridgeTests
{
    [Fact]
    public void TryPulse_without_anchor_returns_null()
    {
        var session = new SessionContext();
        Assert.Null(FederationSessionBridge.TryPulse(session));
    }

    [Fact]
    public void BuildSceneJson_without_anchor_returns_reason()
    {
        var session = new SessionContext();
        var json = FederationSessionBridge.BuildSceneJson(session, new Dictionary<string, JsonElement>(), new JsonSerializerOptions { WriteIndented = true });
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("no_anchor", doc.RootElement.GetProperty("reason").GetString());
    }
}
#endif
