using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public class IdeArchBoardChannelTests
{
    [Fact]
    public void ToolName_is_cdp_arch() =>
        Assert.Equal("cdp_arch", IdeArchBoardChannel.ToolName);

    [Fact]
    public void AddRole_candidates_elect_edge_promote_writes_ssot()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-arch-");
        try
        {
            var session = new SessionContext { ProjectRoot = tmp.FullName };

            var addRole = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("add_role"),
                ["role"] = JsonSerializer.SerializeToElement("ccu"),
                ["id"] = JsonSerializer.SerializeToElement("ccu-build"),
                ["note"] = JsonSerializer.SerializeToElement("BuildAsync ownership")
            });
            AssertOk(addRole);

            var addCand = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("add_candidates"),
                ["role"] = JsonSerializer.SerializeToElement("ccu-build"),
                ["anchors"] = JsonSerializer.SerializeToElement(new[]
                {
                    "[F:IdeCockpit.Build.cs;M:BuildAsync]",
                    "IdeCockpit.cs::BuildAsync"
                })
            });
            AssertOk(addCand);

            var elect = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("elect"),
                ["role"] = JsonSerializer.SerializeToElement("ccu-build"),
                ["candidate"] = JsonSerializer.SerializeToElement("[F:IdeCockpit.Build.cs;M:BuildAsync]")
            });
            AssertOk(elect);

            IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("add_role"),
                ["role"] = JsonSerializer.SerializeToElement("channel"),
                ["id"] = JsonSerializer.SerializeToElement("ch-build")
            });

            var edge = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("edge"),
                ["from"] = JsonSerializer.SerializeToElement("ccu-build"),
                ["to"] = JsonSerializer.SerializeToElement("ch-build"),
                ["kind"] = JsonSerializer.SerializeToElement("feeds")
            });
            AssertOk(edge);

            var promote = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("promote"),
                ["role"] = JsonSerializer.SerializeToElement("ccu-build")
            });
            var promoteJson = JsonSerializer.Serialize(promote);
            using (var doc = JsonDocument.Parse(promoteJson))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), promoteJson);
                Assert.Contains("plan only", doc.RootElement.GetProperty("pulse").GetString());
            }

            var latest = Path.Combine(tmp.FullName, ".cdp", "arch-board", "LATEST.json");
            Assert.True(File.Exists(latest), latest);
            var boardText = File.ReadAllText(latest);
            Assert.Contains("ccu-build", boardText);
            Assert.Contains("BuildAsync", boardText);
            Assert.Contains("promoted", boardText);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Bare_path_candidate_is_label_only_until_wire()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-arch-bare-");
        try
        {
            var session = new SessionContext { ProjectRoot = tmp.FullName };
            IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("add_role"),
                ["role"] = JsonSerializer.SerializeToElement("surface")
            });
            var add = IdeArchBoardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("add_candidates"),
                ["role"] = JsonSerializer.SerializeToElement("surface"),
                ["anchors"] = JsonSerializer.SerializeToElement("JustALabel")
            });
            AssertOk(add);
            var latest = File.ReadAllText(Path.Combine(tmp.FullName, ".cdp", "arch-board", "LATEST.json"));
            Assert.Contains("JustALabel", latest);
            Assert.DoesNotContain("[F:JustALabel", latest);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    static void AssertOk(object result)
    {
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
    }
}
