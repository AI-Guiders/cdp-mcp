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
    public void Scene_without_project_refuses_empty_ok()
    {
        var session = new SessionContext();
        var scene = IdeOnboardChannel.Handle(session, new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("scene")
        });
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(scene));
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("project_required", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("onboard · project_required", doc.RootElement.GetProperty("pulse").GetString());
        Assert.Contains("files", doc.RootElement.GetProperty("hint").GetString(), StringComparison.OrdinalIgnoreCase);
    }


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

    [Fact]
    public void Scan_finds_fsharp_slnx_projects_and_entrypoint()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-onboard-fs-");
        try
        {
            var root = tmp.FullName;
            Directory.CreateDirectory(Path.Combine(root, "src", "Lib"));
            File.WriteAllText(Path.Combine(root, "AIGuiders.Platform.Modeling.slnx"), "{}\n");
            File.WriteAllText(Path.Combine(root, "Program.fs"), "module Program\n[<EntryPoint>]\nlet main _ = 0\n");
            File.WriteAllText(Path.Combine(root, "src", "Lib", "GdlTypes.fs"), "module GdlTypes\n");
            File.WriteAllText(Path.Combine(root, "src", "Lib", "Lib.fsproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");

            var session = new SessionContext { ProjectRoot = root };
            var scan = IdeOnboardChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("scan")
            });
            var json = JsonSerializer.Serialize(scan);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);

            var solutions = doc.RootElement.GetProperty("solutions");
            Assert.True(solutions.GetArrayLength() >= 1, json);
            Assert.Contains("slnx", solutions[0].GetString(), StringComparison.Ordinal);

            Assert.True(doc.RootElement.GetProperty("fsproj_count").GetInt32() >= 1, json);

            var entries = doc.RootElement.GetProperty("entrypoints");
            Assert.True(entries.GetArrayLength() >= 1, json);
            Assert.Contains("Program", entries[0].GetProperty("label").GetString(), StringComparison.Ordinal);

            var verticals = doc.RootElement.GetProperty("verticals");
            Assert.True(verticals.GetArrayLength() >= 1, json);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* ignore */ }
        }
    }
}
