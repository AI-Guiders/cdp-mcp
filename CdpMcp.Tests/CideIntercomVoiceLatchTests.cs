using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class CideIntercomVoiceLatchTests : IDisposable
{
    readonly string _root;

    public CideIntercomVoiceLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-icm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideIntercomVoiceLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideIntercomVoiceLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_agent_to_pm_writes_latch()
    {
        var doc = CideIntercomVoiceLatch.Publish(
            CideIntercomVoiceLatch.SeatPf,
            CideIntercomVoiceLatch.SeatPm,
            "status check",
            CideIntercomVoiceLatch.OriginAgent);

        Assert.NotNull(doc);
        Assert.True(File.Exists(CideIntercomVoiceLatch.LatchPath));
        using var json = JsonDocument.Parse(File.ReadAllText(CideIntercomVoiceLatch.LatchPath));
        Assert.Equal(CideIntercomVoiceLatch.Schema, json.RootElement.GetProperty("schema").GetString());
        Assert.Equal("pf", json.RootElement.GetProperty("from_seat").GetString());
        Assert.Equal("pm", json.RootElement.GetProperty("to_seat").GetString());
        Assert.Equal("agent", json.RootElement.GetProperty("origin").GetString());
    }

    [Fact]
    public void Unread_for_pf_requires_human_origin()
    {
        CideIntercomVoiceLatch.Publish("pm", "pf", "hi PF", CideIntercomVoiceLatch.OriginAgent);
        Assert.Null(CideIntercomVoiceLatch.TryUnreadForPf());

        CideIntercomVoiceLatch.Publish("pm", "pf", "hi PF", CideIntercomVoiceLatch.OriginHuman);
        var unread = CideIntercomVoiceLatch.TryUnreadForPf();
        Assert.NotNull(unread);
        Assert.Equal("hi PF", unread!.Body);
        Assert.Contains("Message for you, sir!", CideIntercomVoiceLatch.DeskPulseLine());
    }

    [Fact]
    public void Ack_clears_unread()
    {
        var pub = CideIntercomVoiceLatch.Publish("pm", "pf", "ack me", CideIntercomVoiceLatch.OriginHuman);
        Assert.NotNull(pub);
        Assert.NotNull(CideIntercomVoiceLatch.TryUnreadForPf());

        var acked = CideIntercomVoiceLatch.Ack(pub!.Id);
        Assert.NotNull(acked);
        Assert.True(acked!.Acked);
        Assert.Null(CideIntercomVoiceLatch.TryUnreadForPf());
        Assert.Null(CideIntercomVoiceLatch.DeskPulseLine());
    }

    [Fact]
    public void NormalizeSeat_accepts_at_tags()
    {
        Assert.Equal(CideIntercomVoiceLatch.SeatPf, CideIntercomVoiceLatch.NormalizeSeat("@PF"));
        Assert.Equal(CideIntercomVoiceLatch.SeatPm, CideIntercomVoiceLatch.NormalizeSeat("@PM"));
        Assert.Equal(CideIntercomVoiceLatch.SeatPm, CideIntercomVoiceLatch.NormalizeSeat("operator"));
        Assert.Null(CideIntercomVoiceLatch.NormalizeSeat("nope"));
    }

    [Fact]
    public void Channel_send_and_scene_roundtrip()
    {
        var sendJson = IdeCideIntercomChannel.HandleJson(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["op"] = JsonSerializer.SerializeToElement("send"),
            ["to"] = JsonSerializer.SerializeToElement("@PM"),
            ["body"] = JsonSerializer.SerializeToElement("glass up")
        });
        using (var send = JsonDocument.Parse(sendJson))
        {
            Assert.True(send.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("pm", send.RootElement.GetProperty("message").GetProperty("to").GetString());
        }

        var sceneJson = IdeCideIntercomChannel.HandleJson(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["op"] = JsonSerializer.SerializeToElement("scene")
        });
        using var scene = JsonDocument.Parse(sceneJson);
        Assert.True(scene.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("glass up", scene.RootElement.GetProperty("latest").GetProperty("body").GetString());
    }

    [Fact]
    public void Publish_appends_journal_and_history_reads_tail()
    {
        var a = CideIntercomVoiceLatch.Publish("pm", "pf", "vh-one", CideIntercomVoiceLatch.OriginHuman);
        var b = CideIntercomVoiceLatch.Publish("pf", "pm", "vh-two", CideIntercomVoiceLatch.OriginAgent);
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.True(File.Exists(CideIntercomVoiceLatch.JournalPath));
        Assert.Equal(2, CideIntercomVoiceLatch.JournalCount());

        // dedupe same id
        CideIntercomVoiceLatch.AppendJournal(a!);
        Assert.Equal(2, CideIntercomVoiceLatch.JournalCount());

        var hist = IdeCideIntercomChannel.HandleJson(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["op"] = JsonSerializer.SerializeToElement("history"),
            ["limit"] = JsonSerializer.SerializeToElement(10)
        });
        using var doc = JsonDocument.Parse(hist);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("vh-two", doc.RootElement.GetProperty("entries")[1].GetProperty("body").GetString());
    }
}