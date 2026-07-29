#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CdpProfileIsolation")]
public sealed class IdePostmortemChannelTests
{
    [Fact]
    public void Template_and_scene_ok()
    {
        var session = new SessionContext();
        using var scene = JsonDocument.Parse(IdePostmortemChannel.HandleJson(session));
        Assert.True(scene.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("postmortem_channel/v1", scene.RootElement.GetProperty("schema").GetString());

        using var tmpl = JsonDocument.Parse(IdePostmortemChannel.HandleJson(session, new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("template")
        }));
        Assert.True(tmpl.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(tmpl.RootElement.GetProperty("axes").GetArrayLength() >= 5);
    }

    [Fact]
    public void Draft_refuses_blame_and_scrubs_secrets()
    {
        var iso = Path.Combine(Path.GetTempPath(), "cdp-pm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iso);
        try
        {
            var session = new SessionContext { ProjectRoot = iso };
            using var blame = JsonDocument.Parse(IdePostmortemChannel.HandleJson(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("draft"),
                ["happened"] = JsonSerializer.SerializeToElement("cockpit hung"),
                ["system_root"] = JsonSerializer.SerializeToElement("your fault agent broke the desk")
            }));
            Assert.False(blame.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("ethics_refuse", blame.RootElement.GetProperty("reason").GetString());

            using var scrub = JsonDocument.Parse(IdePostmortemChannel.HandleJson(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("draft"),
                ["happened"] = JsonSerializer.SerializeToElement("token leak api_key=sk-abcdefghijklmnopqrstuvwxyz123456"),
                ["system_root"] = JsonSerializer.SerializeToElement("docs overloaded full to mean desk spray"),
                ["fix"] = JsonSerializer.SerializeToElement("go_detail=full organ dump only")
            }));
            Assert.True(scrub.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("[redacted]", scrub.RootElement.GetProperty("axes").GetProperty("happened").GetString());
            Assert.False(scrub.RootElement.GetProperty("persisted").GetBoolean());
        }
        finally
        {
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Record_persists_failure_finding_and_fdr_wake()
    {
        var iso = Path.Combine(Path.GetTempPath(), "cdp-pm-rec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iso);
        IdeFlightDataRecorder.PathOverrideForTests = Path.Combine(iso, "fdr-tape.jsonl");
        IdeFlightDataRecorder.SuppressWriteForTests = false;
        try
        {
            var session = new SessionContext { ProjectRoot = iso };
            var callId = "call-hang-fixture";
            using var rec = JsonDocument.Parse(IdePostmortemChannel.HandleJson(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("record"),
                ["title"] = JsonSerializer.SerializeToElement("go_detail=full desk spray"),
                ["happened"] = JsonSerializer.SerializeToElement("cdp_cockpit go=fdr go_detail=full built full desk (~133s)"),
                ["system_root"] = JsonSerializer.SerializeToElement("go_detail=full disabled desk pulse fast-path; overloaded semantics"),
                ["why_repeated"] = JsonSerializer.SerializeToElement("docs said full=organ dump while runtime meant desk spray"),
                ["fix"] = JsonSerializer.SerializeToElement("go_detail=full no longer disables desk fast-path"),
                ["do_not"] = JsonSerializer.SerializeToElement("do not use go_detail=full for organ dump; prefer go=fdr pulse"),
                ["tool"] = JsonSerializer.SerializeToElement("cdp_cockpit"),
                ["fdr_call_id"] = JsonSerializer.SerializeToElement(callId),
                ["category"] = JsonSerializer.SerializeToElement("tool_bug")
            }));

            Assert.True(rec.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(rec.RootElement.GetProperty("persisted").GetBoolean());
            Assert.Equal("postmortem", rec.RootElement.GetProperty("fdr_kind").GetString());
            Assert.NotNull(rec.RootElement.GetProperty("failure").GetProperty("id").GetString());
            Assert.NotNull(rec.RootElement.GetProperty("finding").GetProperty("path").GetString());

            var findingPath = Path.Combine(iso, rec.RootElement.GetProperty("finding").GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(findingPath));

            var wake = IdeFlightDataRecorder.ReadTail(20);
            Assert.Contains(wake, e =>
                string.Equals(e.Kind, "postmortem", StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.CallId, callId, StringComparison.Ordinal));

            using var list = JsonDocument.Parse(IdePostmortemChannel.HandleJson(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("list"),
                ["limit"] = JsonSerializer.SerializeToElement(5)
            }));
            Assert.True(list.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(list.RootElement.GetProperty("count").GetInt32() >= 1);
        }
        finally
        {
            IdeFlightDataRecorder.PathOverrideForTests = null;
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }
}
