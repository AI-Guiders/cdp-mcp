using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public class IdeCrmChannelTests
{
    [Fact]
    public void Call_then_go_around_writes_ssot()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-crm-");
        try
        {
            var session = new SessionContext { ProjectRoot = tmp.FullName };
            var call = IdeCrmChannel.Handle(session, null, null, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("call"),
                ["ask"] = JsonSerializer.SerializeToElement("Land this plan?")
            });
            var callJson = JsonSerializer.Serialize(call);
            using (var doc = JsonDocument.Parse(callJson))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), callJson);
                Assert.Equal("awaiting", doc.RootElement.GetProperty("call").GetProperty("status").GetString());
            }

            var resp = IdeCrmChannel.Handle(session, null, null, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("respond"),
                ["code"] = JsonSerializer.SerializeToElement("go around")
            });
            var respJson = JsonSerializer.Serialize(resp);
            using var rdoc = JsonDocument.Parse(respJson);
            Assert.True(rdoc.RootElement.GetProperty("ok").GetBoolean(), respJson);
            Assert.Equal("go_around", rdoc.RootElement.GetProperty("call").GetProperty("callout").GetString());
            Assert.Contains("go_around", rdoc.RootElement.GetProperty("pulse").GetString());
            Assert.True(File.Exists(Path.Combine(tmp.FullName, ".cdp", "crm", "LATEST.json")));
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void NormCode_maps_confirm_aliases()
    {
        Assert.Equal("approved", IdeCrmChannel.NormCode("confirm"));
        Assert.Equal("approved", IdeCrmChannel.NormCode("cleared"));
        Assert.Equal("go_around", IdeCrmChannel.NormCode("reject"));
        Assert.Equal("go_around", IdeCrmChannel.NormCode("go around"));
        Assert.Equal("stabilized", IdeCrmChannel.NormCode("stable"));
        Assert.Null(IdeCrmChannel.NormCode("because I said so"));
    }

    [Fact]
    public void ToolName_is_cdp_crm() =>
        Assert.Equal("cdp_crm", IdeCrmChannel.ToolName);
}
