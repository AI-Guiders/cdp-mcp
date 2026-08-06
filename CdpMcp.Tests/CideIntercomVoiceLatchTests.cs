using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection(nameof(IntercomLatchSerial))]
public class CideIntercomVoiceLatchTests : IDisposable
{
    readonly string _root;

    public CideIntercomVoiceLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-icm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideIntercomVoiceLatch.RootOverrideForTests = _root;
        CideIntercomIdentityLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideIntercomVoiceLatch.RootOverrideForTests = null;
        CideIntercomIdentityLatch.RootOverrideForTests = null;
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
    public void Publish_defaults_guest_name_kir()
    {
        var doc = CideIntercomVoiceLatch.Publish(
            CideIntercomVoiceLatch.SeatPf,
            CideIntercomVoiceLatch.SeatPm,
            "hello glass",
            CideIntercomVoiceLatch.OriginAgent);

        Assert.NotNull(doc);
        Assert.Equal(CideIntercomVoiceLatch.DefaultNameGuest, doc!.Name);
        Assert.Equal(CideIntercomVoiceLatch.KindGuest, doc.Kind);
        Assert.Equal(
            "Кир · guest @PF → @PM",
            CideIntercomVoiceLatch.FormatRoleLabel(doc.FromSeat, doc.ToSeat, doc.Name!, doc.Kind!));
    }

    [Fact]
    public void Publish_citizen_kind_explicit()
    {
        var doc = CideIntercomVoiceLatch.Publish(
            "pf", "pm", "citizen peer", CideIntercomVoiceLatch.OriginAgent,
            name: "Neumann", kind: "citizen");
        Assert.NotNull(doc);
        Assert.Equal("Neumann", doc!.Name);
        Assert.Equal(CideIntercomVoiceLatch.KindCitizen, doc.Kind);
    }

    [Fact]
    public void Channel_send_from_pm_defaults_operator_bootstrap()
    {
        var sendJson = IdeCideIntercomChannel.HandleJson(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["op"] = JsonSerializer.SerializeToElement("send"),
            ["from"] = JsonSerializer.SerializeToElement("pm"),
            ["to"] = JsonSerializer.SerializeToElement("pf"),
            ["body"] = JsonSerializer.SerializeToElement("operator bootstrap")
        });
        using var send = JsonDocument.Parse(sendJson);
        Assert.True(send.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("pm", send.RootElement.GetProperty("message").GetProperty("from").GetString());
        Assert.Equal("human", send.RootElement.GetProperty("message").GetProperty("origin").GetString());
        Assert.Equal(CideIntercomVoiceLatch.DefaultNameOperator, send.RootElement.GetProperty("message").GetProperty("name").GetString());
        Assert.Equal("operator", send.RootElement.GetProperty("message").GetProperty("kind").GetString());
        Assert.Contains("Operator · operator", send.RootElement.GetProperty("message").GetProperty("role_label").GetString());
        Assert.NotNull(CideIntercomVoiceLatch.TryUnreadForPf());
    }

    [Fact]
    public void Sticky_identity_claims_and_shapes_send_without_name()
    {
        var setJson = IdeCideIntercomChannel.HandleJson(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["op"] = JsonSerializer.SerializeToElement("identity"),
            ["action"] = JsonSerializer.SerializeToElement("set"),
            ["seat"] = JsonSerializer.SerializeToElement("pf"),
            ["name"] = JsonSerializer.SerializeToElement("Морж"),
            ["kind"] = JsonSerializer.SerializeToElement("guest")
        });
        using var set = JsonDocument.Parse(setJson);
        Assert.True(set.RootElement.GetProperty("ok").GetBoolean());

        var sendJson = IdeCideIntercomChannel.HandleJson(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["op"] = JsonSerializer.SerializeToElement("send"),
            ["to"] = JsonSerializer.SerializeToElement("pm"),
            ["body"] = JsonSerializer.SerializeToElement("sticky who")
        });
        using var send = JsonDocument.Parse(sendJson);
        Assert.True(send.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("Морж", send.RootElement.GetProperty("message").GetProperty("name").GetString());
        Assert.Contains("Морж · guest", send.RootElement.GetProperty("message").GetProperty("role_label").GetString());
    }

    [Fact]
    public void Send_explicit_name_claims_sticky()
    {
        var sendJson = IdeCideIntercomChannel.HandleJson(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["op"] = JsonSerializer.SerializeToElement("send"),
            ["to"] = JsonSerializer.SerializeToElement("pm"),
            ["body"] = JsonSerializer.SerializeToElement("claiming"),
            ["name"] = JsonSerializer.SerializeToElement("Neumann"),
            ["kind"] = JsonSerializer.SerializeToElement("citizen")
        });
        using var send = JsonDocument.Parse(sendJson);
        Assert.True(send.RootElement.GetProperty("ok").GetBoolean());

        var slot = CideIntercomIdentityLatch.TrySeat("pf");
        Assert.NotNull(slot);
        Assert.Equal("Neumann", slot!.Name);
        Assert.Equal(CideIntercomVoiceLatch.KindCitizen, slot.Kind);
    }

    [Fact]
    public void Publish_AutoI_does_not_stomp_sticky_who()
    {
        Assert.NotNull(CideIntercomIdentityLatch.Claim(
            "pf", "Sierra", "citizen", "zai-org/GLM-5.1"));

        var voice = CideIntercomVoiceLatch.Publish(
            fromSeat: CideIntercomVoiceLatch.SeatPf,
            toSeat: CideIntercomVoiceLatch.SeatPm,
            body: "Autoi · remount\n→ PFD.NEXT",
            origin: CideIntercomVoiceLatch.OriginAgent,
            name: "AutoI",
            kind: "guest");
        Assert.NotNull(voice);
        Assert.Equal("AutoI", voice!.Name);

        var slot = CideIntercomIdentityLatch.TrySeat("pf");
        Assert.NotNull(slot);
        Assert.Equal("Sierra", slot!.Name);
        Assert.Equal(CideIntercomVoiceLatch.KindCitizen, slot.Kind);
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
    public void Channel_send_dm_lands_in_journal_and_card()
    {
        var sendJson = IdeCideIntercomChannel.HandleJson(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["op"] = JsonSerializer.SerializeToElement("send"),
            ["to"] = JsonSerializer.SerializeToElement("@PM"),
            ["body"] = JsonSerializer.SerializeToElement("dm-nested-axb"),
            ["channel"] = JsonSerializer.SerializeToElement("dm")
        });
        using var send = JsonDocument.Parse(sendJson);
        Assert.True(send.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("dm", send.RootElement.GetProperty("message").GetProperty("channel").GetString());

        var tail = CideIntercomVoiceLatch.LoadJournalTail(1);
        Assert.Equal("dm", tail[^1].Channel);
        Assert.Equal("dm-nested-axb", tail[^1].Body);
    }

    [Fact]
    public void Channel_send_invalid_refuses()
    {
        var sendJson = IdeCideIntercomChannel.HandleJson(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["op"] = JsonSerializer.SerializeToElement("send"),
            ["to"] = JsonSerializer.SerializeToElement("pm"),
            ["body"] = JsonSerializer.SerializeToElement("nope"),
            ["channel"] = JsonSerializer.SerializeToElement("irc")
        });
        using var send = JsonDocument.Parse(sendJson);
        Assert.False(send.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("channel_invalid", send.RootElement.GetProperty("error").GetString());
    }


    [Fact]
    public void Publish_appends_journal_and_history_reads_tail()
    {
        var a = CideIntercomVoiceLatch.Publish("pm", "pf", "vh-one", CideIntercomVoiceLatch.OriginHuman);
        var b = CideIntercomVoiceLatch.Publish(
            "pf", "pm", "vh-two", CideIntercomVoiceLatch.OriginAgent, channel: "crew");
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.True(File.Exists(CideIntercomVoiceLatch.JournalPath)); // intercom.witdb
        Assert.Equal(2, CideIntercomVoiceLatch.JournalCount());

        // dedupe same id
        CideIntercomVoiceLatch.AppendJournal(a!);
        Assert.Equal(2, CideIntercomVoiceLatch.JournalCount());

        var tail = CideIntercomVoiceLatch.LoadJournalTail(2);
        Assert.Equal("crew", tail[^1].Channel);

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
    [Fact]
    public void Publish_requires_durable_journal_not_latest_alone()
    {
        var doc = CideIntercomVoiceLatch.Publish(
            "pf", "pm", "journal-hard letter", CideIntercomVoiceLatch.OriginAgent,
            name: "Citizen", kind: "citizen", channel: "radio");
        Assert.NotNull(doc);
        Assert.True(CideIntercomVoiceLatch.AppendJournal(doc!)); // already appended; dedupe true
        Assert.True(File.Exists(CideIntercomVoiceLatch.JournalPath));
        var tail = CideIntercomVoiceLatch.LoadJournalTail(20);
        Assert.Contains(tail, e => e.Id == doc!.Id && e.Body.Contains("journal-hard letter", StringComparison.Ordinal));
    }

    [Fact]
    public void AppendJournal_survives_abandoned_mutex_style_retry()
    {
        var doc = CideIntercomVoiceLatch.Publish(
            "pf", "pm", "witdb durable", CideIntercomVoiceLatch.OriginAgent,
            name: "Citizen", kind: "citizen", channel: "radio");
        Assert.NotNull(doc);
        Assert.True(File.Exists(CideIntercomVoiceLatch.JournalPath));
        Assert.Contains(
            CideIntercomVoiceLatch.LoadJournalTail(5),
            e => e.Id == doc!.Id && e.Channel == "radio");
    }
}