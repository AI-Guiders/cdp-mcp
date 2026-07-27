using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public class IdeOnboardChannelTests
{
    [Fact]
    public void ToolName_is_cdp_onboard() =>
        Assert.Equal("cdp_onboard", IdeOnboardChannel.ToolName);

    [Fact]
    public void Scan_finds_Program_and_verticals()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-onboard-");
        try
        {
            var root = tmp.FullName;
            Directory.CreateDirectory(Path.Combine(root, "src", "App"));
            Directory.CreateDirectory(Path.Combine(root, "Features", "Billing"));
            File.WriteAllText(Path.Combine(root, "Program.cs"), "class Program { static void Main() {} }\n");
            File.WriteAllText(Path.Combine(root, "README.md"), "# demo\n");
            File.WriteAllText(Path.Combine(root, "src", "App", "Worker.cs"), "class Worker {}\n");
            File.WriteAllText(Path.Combine(root, "Features", "Billing", "Invoice.cs"), "class Invoice {}\n");
            File.WriteAllText(Path.Combine(root, "demo.csproj"), "<Project />\n");

            var session = new SessionContext { ProjectRoot = root };
            var scan = IdeOnboardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("scan")
            });
            var json = JsonSerializer.Serialize(scan);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
            Assert.Equal("unknown", doc.RootElement.GetProperty("profile_hint").GetString());
            Assert.True(doc.RootElement.GetProperty("docs").GetProperty("has_readme").GetBoolean());

            var entries = doc.RootElement.GetProperty("entrypoints");
            Assert.True(entries.GetArrayLength() >= 1, json);
            Assert.Contains("Program", entries[0].GetProperty("label").GetString(), StringComparison.Ordinal);

            var verticals = doc.RootElement.GetProperty("verticals");
            Assert.True(verticals.GetArrayLength() >= 1, json);

            Assert.True(File.Exists(Path.Combine(root, ".cdp", "onboard", "LATEST.json")));
            Assert.True(IdeOnboardChannel.HasScan(session));
            Assert.Contains("onboard", IdeOnboardChannel.PulseLine(session), StringComparison.Ordinal);

            var scene = IdeOnboardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("scene")
            });
            using var sceneDoc = JsonDocument.Parse(JsonSerializer.Serialize(scene));
            Assert.Equal("scene", sceneDoc.RootElement.GetProperty("op").GetString());
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Scan_detects_cide_profile_hint()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-onboard-cide-");
        try
        {
            var root = tmp.FullName;
            Directory.CreateDirectory(Path.Combine(root, "Cockpit", "Channels"));
            Directory.CreateDirectory(Path.Combine(root, "Cockpit", "Composition"));
            Directory.CreateDirectory(Path.Combine(root, "IdeDisplay"));
            File.WriteAllText(Path.Combine(root, "Program.cs"), "class Program {}\n");

            var session = new SessionContext { ProjectRoot = root };
            var scan = IdeOnboardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("scan")
            });
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(scan));
            Assert.Equal("cide", doc.RootElement.GetProperty("profile_hint").GetString());

            var next = doc.RootElement.GetProperty("next");
            var hasAsBuilt = false;
            foreach (var n in next.EnumerateArray())
            {
                if (n.TryGetProperty("go", out var g) &&
                    g.GetString() == "arch_desk")
                {
                    hasAsBuilt = true;
                    break;
                }
            }

            Assert.True(hasAsBuilt, JsonSerializer.Serialize(scan));
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* ignore */ }
        }
    }
}
